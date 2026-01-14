using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Methods for generating character attributes during character creation
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AttributeGenerationMethod
{
    /// <summary>
    /// Use the standard array of values: 15, 14, 13, 12, 10
    /// </summary>
    StandardArray,
    
    /// <summary>
    /// Roll 4d6 and drop the lowest die
    /// </summary>
    Rolled
}
