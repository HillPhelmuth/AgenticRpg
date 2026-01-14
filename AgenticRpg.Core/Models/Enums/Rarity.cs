using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Rarity levels for items
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    VeryRare,
    Legendary,
    Artifact
}
