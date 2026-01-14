using System.ComponentModel;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Converters;
using NewtonConverter = Newtonsoft.Json.JsonConverterAttribute;

namespace AgenticRpg.DiceRoller.Models;

/// <summary>
/// Represents the types of dice used in the RPG system.
/// </summary>
//[JsonConverter(typeof(JsonStringEnumConverter))]
//[NewtonConverter(typeof(StringEnumConverter))]

public enum DieType
{
    None,
    [Description("A four-sided die.")]
    D4 = 4,
    [Description("A six-sided die.")]
    D6 = 6,
    [Description("An eight-sided die.")]
    D8 = 8,
    [Description("A ten-sided die.")]
    D10 = 10,
    [Description("A twelve-sided die.")]
    D12 = 12,
    [Description("A twenty-sided die.")]
    D20 = 20
}