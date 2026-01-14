using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Defines the five core attributes used in the RPG system
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AttributeType
{
    /// <summary>
    /// Physical strength and melee combat capability
    /// </summary>
    Might,
    
    /// <summary>
    /// Reflexes, dexterity, and ranged combat capability
    /// </summary>
    Agility,
    
    /// <summary>
    /// Health, endurance, and resistance to effects
    /// </summary>
    Vitality,
    
    /// <summary>
    /// Intelligence, knowledge, and magical aptitude
    /// </summary>
    Wits,
    
    /// <summary>
    /// Charisma, social influence, and willpower
    /// </summary>
    Presence,
    AC,

    
}
