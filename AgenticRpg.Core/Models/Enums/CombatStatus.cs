using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Represents the current status of a combat encounter
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CombatStatus
{
    /// <summary>
    /// Combat is being initialized
    /// </summary>
    Initializing,
    
    /// <summary>
    /// Combat is actively in progress
    /// </summary>
    Active,
    
    /// <summary>
    /// Combat ended with party victory
    /// </summary>
    Victory,
    
    /// <summary>
    /// Combat ended with party defeat
    /// </summary>
    Defeat,
    
    /// <summary>
    /// Combat ended in a draw or escape
    /// </summary>
    Ended
}
