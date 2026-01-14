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
    /// Campaign is temporarily paused
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
