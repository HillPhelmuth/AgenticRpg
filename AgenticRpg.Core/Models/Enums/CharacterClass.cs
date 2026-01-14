using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Available character classes in the game
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterClass
{
    None,
    Cleric,
    Wizard,
    Warrior,
    Rogue,
    Paladin,
    WarMage
}
