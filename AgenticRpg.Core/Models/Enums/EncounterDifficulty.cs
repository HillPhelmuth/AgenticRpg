using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Difficulty levels for encounters
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EncounterDifficulty
{
    Trivial = 1,
    Easy = 3,
    Moderate = 5,
    Hard = 7,
    Deadly = 9,
    Impossible = 10
}
