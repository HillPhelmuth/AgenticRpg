namespace AgenticRpg.Core.Models;

/// <summary>
/// Tracks a player's ready status in the campaign lobby
/// </summary>
public class PlayerReadyStatus
{
    /// <summary>
    /// The player's unique identifier
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;
    
    /// <summary>
    /// The character ID this player controls in the campaign
    /// </summary>
    public string CharacterId { get; set; } = string.Empty;
    
    /// <summary>
    /// The player's display name
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether the player is ready to start the campaign
    /// </summary>
    public bool IsReady { get; set; } = false;
    
    /// <summary>
    /// Timestamp when the player marked themselves as ready
    /// </summary>
    public DateTime? ReadyAt { get; set; }
    
    /// <summary>
    /// SignalR connection ID for this player (transient, not persisted)
    /// </summary>
    public string? ConnectionId { get; set; }
}
