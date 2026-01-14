using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Agents.Tools.Results;

// ValidateRaceChoice result types
public class RaceValidationResult
{
    public bool Valid { get; set; }
    public RaceDetails? Race { get; set; }
    public string? Error { get; set; }
}

public class RaceDetails
{
    public required string Name { get; set; }
    public required AttributeBonuses AttributeBonus { get; set; }
    public int MovementSpeed { get; set; }
    public required string SpecialAbility { get; set; }
    public required string[] AvailableClasses { get; set; }
}

public class AttributeBonuses
{
    public int? Any { get; set; }
    public int? Might { get; set; }
    public int? Agility { get; set; }
    public int? Vitality { get; set; }
    public int? Wits { get; set; }
    public int? Presence { get; set; }
}

// ValidateClassChoice result types
public class ClassValidationResult
{
    public bool Valid { get; set; }
    public ClassDetails? Class { get; set; }
    public string? Error { get; set; }
}

public class ClassDetails
{
    public required string Name { get; set; }
    public required string HitDice { get; set; }
    public required string ManaDice { get; set; }
    public required string[] PrimeAttributes { get; set; }
    public required string[] Weapons { get; set; }
    public required string[] Armor { get; set; }
    public required string[] StartingEquipment { get; set; }
}

// ValidateAttributeAllocation result types
public class AttributeAllocationResult
{
    public bool Valid { get; set; }
    public Dictionary<AttributeType, int>? BaseAttributes { get; set; }
    public Dictionary<AttributeType, int>? FinalAttributes { get; set; }
    public Dictionary<AttributeType, int>? Modifiers { get; set; }
    public string? Error { get; set; }
}

// GenerateCharacterSheet result types
public class CharacterSheetResult
{
    public bool Valid { get; set; }
    public Character? Character { get; set; }
    public string? Error { get; set; }
}

// SaveCharacter result types
public class SaveCharacterResult
{
    public bool Success { get; set; }
    public string? CharacterId { get; set; }
    public required string Message { get; set; }
}
