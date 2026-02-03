using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Types of items in the game
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemType
{
    Weapon,
    Armor,
    Shield,
    Helmet,
    Consumable,
    Tool,
    Container,
    Scroll,
    Ring,
    Wand,
    Drink,
    Food,
    Service,
    MagicItem,
    Quest,
    Amulet,
    Spell,
    Miscellaneous,
    Ability

}