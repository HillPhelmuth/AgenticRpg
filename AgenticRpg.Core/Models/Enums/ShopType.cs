using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Types of shops available in the game
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShopType
{
    Weapon,
    Armor,
    General,
    Magic,
    Tavern
   
}
