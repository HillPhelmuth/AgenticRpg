using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Defines who can see a particular narrative entry
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NarrativeVisibility
{
    /// <summary>
    /// Visible to all players in the campaign
    /// </summary>
    Global,
    
    /// <summary>
    /// Visible only to the GM (or system)
    /// </summary>
    GMOnly,
    
    /// <summary>
    /// Visible to a specific character or player
    /// </summary>
    CharacterSpecific
}
