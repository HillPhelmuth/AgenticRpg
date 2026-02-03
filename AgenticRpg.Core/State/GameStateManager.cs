using AgenticRpg.Core.Hubs;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Repositories;
using Azure.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.State;

/// <summary>
/// Manages game state with in-memory caching and persistent storage
/// </summary>
public class GameStateManager(
    ICampaignRepository campaignRepository,
    ICharacterRepository characterRepository,
    IWorldRepository worldRepository,
    ILogger<GameStateManager> logger,
    INarrativeRepository narrativeRepository, IGameStateRepository gameStateRepository, IHubContext<GameHub> hubContext)
    : IGameStateManager
{
    private readonly ConcurrentDictionary<string, GameState> _stateCache = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    /// <inheritdoc/>
    public event EventHandler<GameStateChangedEventArgs>? StateChanged;

    
    /// <inheritdoc/>
    public async Task<GameState?> GetCampaignStateAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        // Try to get from cache first
        if (_stateCache.TryGetValue(campaignId, out var cachedState))
        {
            logger.LogDebug("Retrieved campaign state from cache: {CampaignId}", campaignId);
            await EnsureStateHydratedAsync(campaignId, cachedState, cancellationToken);
            return cachedState;
        }
        
        // If not in cache, load from storage
        logger.LogDebug("Campaign state not in cache, loading from storage: {CampaignId}", campaignId);
        var gameState = await LoadStateAsync(campaignId, cancellationToken);
        if (gameState == null)
        {
            logger.LogWarning("Campaign state not found in storage: {CampaignId}. Returning new gamestate", campaignId);
            var state = new GameState();
            // Reason: CampaignId is derived from Campaign.Id. Ensure it matches the requested campaign so
            // cache keys, SignalR groups, and Cosmos partition keys all align.
            state.Campaign.Id = campaignId;
            _stateCache.AddOrUpdate(campaignId, state, (key, oldValue) => state);
            return state;
        }

        await EnsureStateHydratedAsync(campaignId, gameState, cancellationToken);
        return gameState;
    }

    private async Task EnsureStateHydratedAsync(string campaignId, GameState state, CancellationToken cancellationToken)
    {
        if (state.Campaign is null || state.Campaign.Id != campaignId || string.IsNullOrWhiteSpace(state.Campaign.Name))
        {
            var campaign = await campaignRepository.GetByIdAsync(campaignId, cancellationToken);
            if (campaign is not null)
            {
                state.Campaign = campaign;
            }
            else
            {
                // Reason: Avoid cascading null refs; keep the Campaign Id consistent.
                state.Campaign ??= new Campaign();
                state.Campaign.Id = campaignId;
            }
        }

        state.Characters ??= [];

        var expectedCharacterIds = state.Campaign.CharacterIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList() ?? [];
        var hasMissingCharacters = expectedCharacterIds.Count > 0
            && expectedCharacterIds.Any(id => state.Characters.All(c => c.Id != id));

        if (state.Characters.Count == 0 || hasMissingCharacters)
        {
            var characters = await characterRepository.GetByCampaignIdAsync(campaignId, cancellationToken);
            state.Characters = characters.ToList();

            // Reason: Keep Campaign.CharacterIds in sync so readiness/turn logic can rely on it.
            if (state.Campaign.CharacterIds is null)
            {
                state.Campaign.CharacterIds = [];
            }

            var refreshedIds = state.Characters
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => c.Id)
                .Distinct()
                .ToList();

            state.Campaign.CharacterIds = state.Campaign.CharacterIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Concat(refreshedIds)
                .Distinct()
                .ToList();
        }

        state.World ??= new World();
        if (!string.IsNullOrWhiteSpace(state.Campaign.WorldId) && state.World.Id != state.Campaign.WorldId)
        {
            var world = await worldRepository.GetByIdAsync(state.Campaign.WorldId, cancellationToken);
            state.World = world ?? state.World;
        }
    }
    
    /// <inheritdoc/>
    public async Task<bool> UpdateCampaignStateAsync(GameState state, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(state.CampaignId))
        {
            logger.LogError("Cannot update state with empty campaign ID");
            return false;
        }
        // ------------------------------------Temptorary Logging------------------------------------
        //logger.LogInformation("Updating campaign state: {CampaignId}, Version:\n\n {Version}", state.CampaignId, JsonSerializer.Serialize(state));
        //-------------------------------------------------------------------------------------------
        // Update timestamp and version
        state.LastUpdated = DateTime.UtcNow;
        state.Version++;
        await hubContext.Clients.Group(GameHub.GetCampaignGroupName(state.CampaignId)).SendAsync("StateUpdated", state, cancellationToken);
        // Update cache
        _stateCache.AddOrUpdate(state.CampaignId, state, (key, oldValue) => state);
        
        logger.LogDebug("Updated campaign state in cache: {CampaignId}, Version: {Version}", 
            state.CampaignId, state.Version);
        
        // Persist critical state changes to database immediately
        // This ensures characters, campaign settings, and world state are saved
        try
        {
            // Save game state
            await gameStateRepository.UpdateAsync(state, cancellationToken);
            logger.LogDebug("Persisted game state updates: {CampaignId}", state.CampaignId);
            // Save campaign (status, settings, current turn, etc.)
            if (state.Campaign != null && !string.IsNullOrEmpty(state.Campaign.Name))
            {
                await campaignRepository.UpdateAsync(state.Campaign, cancellationToken);
                logger.LogDebug("Persisted campaign updates: {CampaignId}", state.CampaignId);
            }
            
            // Save all characters (HP, stats, inventory changes, etc.)
            foreach (var character in state.Characters)
            {
                await characterRepository.UpdateAsync(character, cancellationToken);
            }
            logger.LogDebug("Persisted {Count} character updates for campaign: {CampaignId}", 
                state.Characters.Count, state.CampaignId);
            
            // Save world if it has been modified
            if (state.World != null && !string.IsNullOrEmpty(state.World.Name))
            {
                await worldRepository.UpdateAsync(state.World, cancellationToken);
                logger.LogDebug("Persisted world updates: {CampaignId}", state.CampaignId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error persisting state updates for campaign: {CampaignId}", state.CampaignId);
            // Don't fail the update - cache is updated, persistence will be retried
        }
        
        // Raise state changed event
        OnStateChanged(new GameStateChangedEventArgs
        {
            CampaignId = state.CampaignId,
            State = state,
            ChangeDescription = $"State updated to version {state.Version}"
        });
        
        return true;
    }
    
    /// <inheritdoc/>
    public async Task<bool> SaveStateAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        if (!_stateCache.TryGetValue(campaignId, out var state))
        {
            logger.LogWarning("Cannot save state for campaign not in cache: {CampaignId}", campaignId);
            return false;
        }
        
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            logger.LogInformation("Saving state for campaign: {CampaignId}", campaignId);
            await gameStateRepository.UpdateAsync(state, cancellationToken);
            // Save campaign
            if (state.Campaign != null)
            {
                await campaignRepository.UpdateAsync(state.Campaign, cancellationToken);
                logger.LogDebug("Saved campaign data: {CampaignId}", campaignId);
            }
            
            // Save all characters
            foreach (var character in state.Characters)
            {
                await characterRepository.UpdateAsync(character, cancellationToken);
            }
            logger.LogDebug("Saved {Count} characters for campaign: {CampaignId}", 
                state.Characters.Count, campaignId);
            
            // Save world
            if (state.World != null)
            {
                await worldRepository.UpdateAsync(state.World, cancellationToken);
                logger.LogDebug("Saved world data for campaign: {CampaignId}", campaignId);
            }
            
            logger.LogInformation("Successfully saved all state for campaign: {CampaignId}", campaignId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving state for campaign: {CampaignId}", campaignId);
            return false;
        }
        finally
        {
            _saveLock.Release();
        }
    }
    
    /// <inheritdoc/>
    public async Task<GameState?> LoadStateAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Loading state for campaign: {CampaignId}", campaignId);
            var gameState = await gameStateRepository.GetStateByCampaignIdAsync(campaignId, cancellationToken);
            if (gameState != null)
            {
                // Add to cache
                _stateCache.AddOrUpdate(campaignId, gameState, (key, oldValue) => gameState);
                logger.LogInformation("Successfully loaded state from storage for campaign: {CampaignId}", campaignId);
                return gameState;
            }
            // Load campaign
            var campaign = await campaignRepository.GetByIdAsync(campaignId, cancellationToken);
            if (campaign == null)
            {
                logger.LogWarning("Campaign not found: {CampaignId}", campaignId);
                return null;
            }
            
            // Load characters
            var characters = await characterRepository.GetByCampaignIdAsync(campaignId, cancellationToken);
            
            // Load world
            var world = await worldRepository.GetByIdAsync(campaign.WorldId, cancellationToken);
            
            // Load narratives
            var narratives = await narrativeRepository.GetByCampaignIdAsync(campaignId, cancellationToken: cancellationToken);
            // Build game state
            var state = new GameState
            {
                
                Campaign = campaign,
                World = world ?? new World(),
                Characters = characters.ToList(),
                LastUpdated = DateTime.UtcNow,
                RecentNarratives = narratives.OrderByDescending(n => n.Timestamp).Take(10).ToList(),
            };
            
            // Add to cache
            _stateCache.AddOrUpdate(campaignId, state, (key, oldValue) => state);
            
            logger.LogInformation("Successfully loaded state for campaign: {CampaignId}", campaignId);
            return state;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading state for campaign: {CampaignId}", campaignId);
            return null;
        }
    }

    /// <summary>
    /// Raises the StateChanged event
    /// </summary>
    protected virtual void OnStateChanged(GameStateChangedEventArgs e)
    {
        StateChanged?.Invoke(this, e);
    }
}
