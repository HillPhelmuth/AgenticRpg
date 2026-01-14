using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Available character races in the game
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterRace
{
    None,
    Human,
    Duskborn,
    Ironforged,
    Wildkin,
    Emberfolk,
    Stoneborn
}
