using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Times of day in the game world
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TimeOfDay
{
    Dawn,
    Morning,
    Afternoon,
    Dusk,
    Evening,
    Night,
    Midnight
}
