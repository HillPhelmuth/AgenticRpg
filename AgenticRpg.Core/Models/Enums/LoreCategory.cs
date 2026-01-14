using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Categories of world lore entries
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LoreCategory
{
    History,
    Legend,
    Culture,
    Religion,
    Geography,
    Politics,
    Magic,
    Mythology
}
