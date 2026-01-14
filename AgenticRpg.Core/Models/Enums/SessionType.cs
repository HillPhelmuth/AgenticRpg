using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Type of pre-game session
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionType
{
    /// <summary>
    /// Character creation session
    /// </summary>
    CharacterCreation = 0,
    
    /// <summary>
    /// World building session
    /// </summary>
    WorldBuilding = 1,
    
    /// <summary>
    /// Campaign setup session
    /// </summary>
    CampaignSetup = 2,
    
    /// <summary>
    /// General chat or exploration session
    /// </summary>
    General = 3
}
