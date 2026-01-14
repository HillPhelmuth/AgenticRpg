using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Status of a quest in the game
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuestStatus
{
    Available,
    Active,
    Completed,
    Failed,
    Abandoned
}
