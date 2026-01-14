using AgenticRpg.Core.Models;

namespace AgenticRpg.Core.State;

/// <summary>
/// Interface for managing game state across campaigns
/// </summary>
public interface IGameStateManager
{
    /// <summary>
    /// Gets the current state for a campaign
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The game state, or null if not found</returns>
    Task<GameState?> GetCampaignStateAsync(string campaignId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates the state for a campaign
    /// </summary>
    /// <param name="state">The updated game state</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if update was successful</returns>
    Task<bool> UpdateCampaignStateAsync(GameState state, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Saves the current state to persistent storage
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if save was successful</returns>
    Task<bool> SaveStateAsync(string campaignId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when state changes for a campaign
    /// </summary>
    event EventHandler<GameStateChangedEventArgs>? StateChanged;

}

/// <summary>
/// Event arguments for state change notifications
/// </summary>
public class GameStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// The campaign ID
    /// </summary>
    public string CampaignId { get; init; } = string.Empty;
    
    /// <summary>
    /// The updated game state
    /// </summary>
    public GameState State { get; init; } = new();
    
    /// <summary>
    /// Description of what changed
    /// </summary>
    public string ChangeDescription { get; init; } = string.Empty;
}
