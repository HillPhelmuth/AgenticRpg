using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Represents the current status of a campaign
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CampaignStatus
{
    /// <summary>
    /// Campaign is being set up, players are joining
    /// </summary>
    Setup,
    
    /// <summary>
    /// Campaign is in the lobby, waiting for all players to ready up
    /// </summary>
    Lobby,
    
    /// <summary>
    /// Campaign is actively being played
    /// </summary>
    Active,

    /// <summary>
    /// No campaign players are currently active, but the campaign is not yet concluded. This could be due to a temporary pause in play or players taking a break.
    /// </summary>
    Paused,
    
    /// <summary>
    /// Campaign has concluded
    /// </summary>
    Completed,
    
    /// <summary>
    /// Campaign is archived (inactive for extended period)
    /// </summary>
    Archived
}
