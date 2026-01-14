using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Roles or professions for NPCs in the game
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NPCRole
{
    Merchant,
    Guard,
    QuestGiver,
    Innkeeper,
    Blacksmith,
    Priest,
    Noble,
    Commoner,
    Scholar,
    Wizard,
    Thief,
    Farmer,
    Soldier,
    Healer
}
