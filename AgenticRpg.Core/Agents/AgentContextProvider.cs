using AgenticRpg.Core.Models;
using AgenticRpg.Core.State;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Core.Agents;

/// <summary>
/// Provides campaign context and conversation history to agents
/// </summary>
public class AgentContextProvider(
    IGameStateManager gameStateManager,
    ISessionStateManager sessionStateManager,
    ILogger<AgentContextProvider> logger)
    : IAgentContextProvider
{
    private readonly IGameStateManager _gameStateManager = gameStateManager ?? throw new ArgumentNullException(nameof(gameStateManager));
    private readonly ISessionStateManager _sessionStateManager = sessionStateManager ?? throw new ArgumentNullException(nameof(sessionStateManager));
    private readonly ILogger<AgentContextProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    

    /// <inheritdoc/>
    public async Task<GameState?> GetGameStateAsync(string campaignId)
    {
        if (string.IsNullOrEmpty(campaignId))
        {
            _logger.LogWarning("GetGameStateAsync called with null or empty campaignId");
            return null;
        }
        
        try
        {
            var gameState = await _gameStateManager.GetCampaignStateAsync(campaignId);
            
            if (gameState == null)
            {
                _logger.LogWarning("Game state not found for campaign: {CampaignId}", campaignId);
            }
            
            return gameState;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving game state for campaign: {CampaignId}", campaignId);
            return null;
        }
    }
    
    /// <inheritdoc/>
    public async Task UpdateGameStateAsync(GameState gameState)
    {
        if (gameState == null)
        {
            throw new ArgumentNullException(nameof(gameState));
        }
        
        if (string.IsNullOrEmpty(gameState.CampaignId))
        {
            throw new ArgumentException("GameState must have a valid CampaignId", nameof(gameState));
        }
        
        try
        {
            var success = await _gameStateManager.UpdateCampaignStateAsync(gameState);
            
            if (success)
            {
                _logger.LogDebug("Game state updated for campaign: {CampaignId}", gameState.CampaignId);
            }
            else
            {
                _logger.LogWarning("Failed to update game state for campaign: {CampaignId}", gameState.CampaignId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating game state for campaign: {CampaignId}", gameState.CampaignId);
            throw;
        }
    }

    // ===== Session-specific methods =====
    
    /// <inheritdoc/>
    public async Task<SessionState?> GetSessionStateAsync(string sessionId)
    {
        return await _sessionStateManager.GetSessionStateAsync(sessionId);
    }

    /// <inheritdoc/>
    public async Task<bool> IsSessionAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }
        
        // Check if it's a session first (sessions are more likely for this check)
        var session = await _sessionStateManager.GetSessionStateAsync(id);
        return session != null;
    }
    public async Task<string> GetSessionOrCampaignModel(string sessionOrCampaignId)
    {
        if (string.IsNullOrEmpty(sessionOrCampaignId))
        {
            throw new ArgumentNullException(nameof(sessionOrCampaignId));
        }
        // Check if it's a session
        var session = await _sessionStateManager.GetSessionStateAsync(sessionOrCampaignId);
        if (session != null)
        {
            return session.SelectedModel ?? "gpt-5.1";
        }
        // Otherwise, treat it as a campaign
        var gameState = await _gameStateManager.GetCampaignStateAsync(sessionOrCampaignId);
        if (gameState != null)
        {
            return gameState.SelectedModel ?? "gpt-5.1";
        }
        return "gpt-5.1";
    }
    public async Task SetSessionOrCampaignModel(string sessionOrCampaignId, string modelName)
    {
        if (string.IsNullOrEmpty(sessionOrCampaignId))
        {
            throw new ArgumentNullException(nameof(sessionOrCampaignId));
        }
        // Check if it's a session
        var session = await _sessionStateManager.GetSessionStateAsync(sessionOrCampaignId);
        if (session != null)
        {
            _logger.LogInformation("Saving model for session: {SessionId} to {ModelName}", sessionOrCampaignId, modelName);
            session.SelectedModel = modelName;
            await _sessionStateManager.UpdateSessionStateAsync(session);
            return;
        }
        // Otherwise, treat it as a campaign
        var gameState = await _gameStateManager.GetCampaignStateAsync(sessionOrCampaignId);
        if (gameState != null)
        {
            _logger.LogInformation("Saving model for campaign: {CampaignId} to {ModelName}", sessionOrCampaignId, modelName);
            gameState.SelectedModel = modelName;
            await _gameStateManager.UpdateCampaignStateAsync(gameState);
        }
    }
}
