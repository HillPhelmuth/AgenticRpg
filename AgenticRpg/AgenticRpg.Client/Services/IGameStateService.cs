using AgenticRpg.Core.Models;

namespace AgenticRpg.Client.Services;

/// <summary>
/// Service for managing game state from the client
/// </summary>
public interface IGameStateService
{
    /// <summary>
    /// Gets the complete game state for a campaign
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <returns>The game state, or null if not found</returns>
    Task<GameState?> GetGameStateAsync(string campaignId);
    
    /// <summary>
    /// Forces a save of the game state to persistent storage
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <returns>True if save was successful</returns>
    Task<bool> SaveGameStateAsync(string campaignId);
}
