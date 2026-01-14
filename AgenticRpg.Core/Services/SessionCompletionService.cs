using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.State;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Core.Services;

/// <summary>
/// Service for finalizing sessions and saving resulting entities
/// </summary>
public interface ISessionCompletionService
{
    /// <summary>
    /// Finalizes a character creation session and saves the character
    /// </summary>
    Task<string> FinalizeCharacterAsync(string sessionId);
    
    /// <summary>
    /// Finalizes a world building session and saves the world
    /// </summary>
    Task<string> FinalizeWorldAsync(string sessionId);
    
    /// <summary>
    /// Finalizes a campaign setup session and saves the campaign
    /// </summary>
    Task<string> FinalizeCampaignAsync(string sessionId);
}

/// <summary>
/// Implementation of session completion service
/// </summary>
public class SessionCompletionService(
    ISessionStateManager sessionStateManager,
    ICharacterRepository characterRepository,
    IWorldRepository worldRepository,
    ICampaignRepository campaignRepository,
    ILogger<SessionCompletionService> logger,
    IAgentThreadStore threadStore)
    : ISessionCompletionService
{
    /// <summary>
    /// Finalizes a character creation session and saves the character to the database
    /// </summary>
    public async Task<string> FinalizeCharacterAsync(string sessionId)
    {
        logger.LogInformation("Finalizing character from session {SessionId}", sessionId);
        
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }
        
        if (session.SessionType != SessionType.CharacterCreation)
        {
            throw new InvalidOperationException($"Session {sessionId} is not a character creation session");
        }
        
        var draftCharacter = session.Context.DraftCharacter;
        if (draftCharacter == null)
        {
            throw new InvalidOperationException($"No draft character found in session {sessionId}");
        }
        
        // Validate character has required fields
        if (string.IsNullOrWhiteSpace(draftCharacter.Name))
        {
            throw new InvalidOperationException("Character must have a name");
        }
        
        if (draftCharacter.Race == default)
        {
            throw new InvalidOperationException("Character must have a race");
        }
        
        if (draftCharacter.Class == default)
        {
            throw new InvalidOperationException("Character must have a class");
        }
        
        // Ensure character has a valid ID
        if (string.IsNullOrWhiteSpace(draftCharacter.Id))
        {
            draftCharacter.Id = Guid.NewGuid().ToString();
        }
        
        // Set player ID from session if not already set
        if (string.IsNullOrWhiteSpace(draftCharacter.PlayerId))
        {
            draftCharacter.PlayerId = session.PlayerId;
        }
        
        // Set timestamps
        draftCharacter.CreatedAt = DateTime.UtcNow;
        
        // Save character to database
        try
        {
            await characterRepository.CreateAsync(draftCharacter);
            logger.LogInformation("Character {CharacterId} saved successfully from session {SessionId}", 
                draftCharacter.Id, sessionId);
            
            // Mark session as completed
            await sessionStateManager.CompleteSessionAsync(sessionId, draftCharacter.Id);
            
            threadStore.TryRemoveScope(sessionId);

            return draftCharacter.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving character from session {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// Finalizes a world building session and saves the world to the database
    /// </summary>
    public async Task<string> FinalizeWorldAsync(string sessionId)
    {
        logger.LogInformation("Finalizing world from session {SessionId}", sessionId);
        
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }
        
        if (session.SessionType != SessionType.WorldBuilding)
        {
            throw new InvalidOperationException($"Session {sessionId} is not a world building session");
        }
        
        var draftWorld = session.Context.DraftWorld;
        if (draftWorld == null)
        {
            throw new InvalidOperationException($"No draft world found in session {sessionId}");
        }
        
        // Validate world has required fields
        if (string.IsNullOrWhiteSpace(draftWorld.Name))
        {
            throw new InvalidOperationException("World must have a name");
        }
        
        // Ensure world has a valid ID
        if (string.IsNullOrWhiteSpace(draftWorld.Id))
        {
            draftWorld.Id = Guid.NewGuid().ToString();
        }
        
        // Set timestamps
        draftWorld.CreatedAt = DateTime.UtcNow;
        
        // Save world to database
        try
        {
            await worldRepository.CreateAsync(draftWorld);
            logger.LogInformation("World {WorldId} saved successfully from session {SessionId}", 
                draftWorld.Id, sessionId);
            
            // Mark session as completed
            await sessionStateManager.CompleteSessionAsync(sessionId, draftWorld.Id);
            
            threadStore.TryRemoveScope(sessionId);

            return draftWorld.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving world from session {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// Finalizes a campaign setup session and saves the campaign to the database
    /// </summary>
    public async Task<string> FinalizeCampaignAsync(string sessionId)
    {
        logger.LogInformation("Finalizing campaign from session {SessionId}", sessionId);
        
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }
        
        if (session.SessionType != SessionType.CampaignSetup)
        {
            throw new InvalidOperationException($"Session {sessionId} is not a campaign setup session");
        }
        
        var draftCampaign = session.Context.DraftCampaign;
        if (draftCampaign == null)
        {
            throw new InvalidOperationException($"No draft campaign found in session {sessionId}");
        }
        
        // Validate campaign has required fields
        if (string.IsNullOrWhiteSpace(draftCampaign.Name))
        {
            throw new InvalidOperationException("Campaign must have a name");
        }
        
        // Ensure campaign has a valid ID
        if (string.IsNullOrWhiteSpace(draftCampaign.Id))
        {
            draftCampaign.Id = Guid.NewGuid().ToString();
        }
        
        // Set owner ID from session if not already set
        if (string.IsNullOrWhiteSpace(draftCampaign.OwnerId))
        {
            draftCampaign.OwnerId = session.PlayerId;
        }
        
        // Set timestamps
        draftCampaign.CreatedAt = DateTime.UtcNow;
        draftCampaign.UpdatedAt = DateTime.UtcNow;
        
        // Save campaign to database
        try
        {
            await campaignRepository.CreateAsync(draftCampaign);
            logger.LogInformation("Campaign {CampaignId} saved successfully from session {SessionId}", 
                draftCampaign.Id, sessionId);
            
            // Mark session as completed
            await sessionStateManager.CompleteSessionAsync(sessionId, draftCampaign.Id);
            
            threadStore.TryRemoveScope(sessionId);

            return draftCampaign.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving campaign from session {SessionId}", sessionId);
            throw;
        }
    }
}
