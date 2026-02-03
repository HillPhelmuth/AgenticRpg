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
}
