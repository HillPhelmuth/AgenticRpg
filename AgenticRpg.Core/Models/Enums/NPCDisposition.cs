using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// NPC attitudes and dispositions toward player characters
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NPCDisposition
{
    Friendly,
    Neutral,
    Hostile,
    Suspicious,
    Helpful,
    Indifferent,
    Afraid,
    Desperate
}
