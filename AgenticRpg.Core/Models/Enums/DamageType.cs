using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Types of damage that can be dealt in combat
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DamageType
{
    Physical,
    Slashing,
    Piercing,
    Bludgeoning,
    Magical,
    Fire,
    Cold,
    Lightning,
    Poison,
    Necrotic,
    Radiant,
    Acid,
    Thunder,
    Force,
    Psychic
}
