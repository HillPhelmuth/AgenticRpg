using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Possible victors of a combat encounter
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CombatVictor
{
    Party,
    Enemies,
    Draw
}
