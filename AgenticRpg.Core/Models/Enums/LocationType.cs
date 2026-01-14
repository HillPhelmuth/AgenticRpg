using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Types of locations in the game world
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LocationType
{
    Town,
    City,
    Dungeon,
    Wilderness,
    Castle,
    Village,
    Temple,
    Cave,
    Ruins,
    Forest,
    Mountain,
    Swamp,
    Desert,
    Tavern,
    Shop
}
