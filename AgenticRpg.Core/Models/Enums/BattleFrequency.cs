using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BattleFrequency
{
    [Description("Low frequency of battles. Campaign focus is on the narrative")]
    Low,
    [Description("Medium frequency of battles. Balanced approach between narrative and combat")]
    Medium,
    [Description("High frequency of battles. Campaign focus is on combat encounters")]
    High,
    [Description("Constant battles. Campaign is heavily combat-oriented with nearly all encounters being combat-focused")]
    Constant
}