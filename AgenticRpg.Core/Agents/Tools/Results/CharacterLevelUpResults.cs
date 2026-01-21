using System;
using System.Collections.Generic;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Agents.Tools.Results;

/// <summary>
/// Result of calculating new stats when leveling up
/// </summary>
public class CalculateNewStatsResult
{
    public bool Success { get; set; }
    public int NewLevel { get; set; }
    public int PreviousHP { get; set; }
    public int NewHP { get; set; }
    public int HPIncrease { get; set; }
    public int PreviousMP { get; set; }
    public int NewMP { get; set; }
    public int MPIncrease { get; set; }
    public int NewProficiencyBonus { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of allocating skill points during level up
/// </summary>
public class AllocateSkillPointsResult
{
    public bool Success { get; set; }
    public string SkillName { get; set; } = string.Empty;
    public int PreviousRank { get; set; }
    public int NewRank { get; set; }
    public int PointsAllocated { get; set; }
    public int RemainingSkillPoints { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of getting available spells for spellcaster class/level
/// </summary>
public class GetAvailableSpellsResult
{
    public bool Success { get; set; }
    public int Level { get; set; }
    public string SpellcasterClass { get; set; } = string.Empty;
    public int MaxSpellLevel { get; set; }
    public List<Spell> AvailableSpells { get; set; } = [];
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of selecting a new spell during level up
/// </summary>
public class SelectNewSpellResult
{
    public bool Success { get; set; }
    public string SpellName { get; set; } = string.Empty;
    public int SpellLevel { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of finalizing the level up process
/// </summary>
public class FinalizeLevelResult
{
    public bool Success { get; set; }
    public string CharacterId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public int PreviousLevel { get; set; }
    public int NewLevel { get; set; }
    public int CurrentXP { get; set; }
    public int NextLevelXP { get; set; }
    public List<string> NewAbilities { get; set; } = [];
    public List<string> NewSpells { get; set; } = [];
    public Dictionary<string, int> SkillIncreases { get; set; } = [];
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of updating character core stats.
/// </summary>
public class UpdateCharacterStatsResult
{
    public bool Success { get; set; }
    public string CharacterId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public int Level { get; set; }
    public int Experience { get; set; }
    public int CurrentHP { get; set; }
    public int MaxHP { get; set; }
    public int CurrentMP { get; set; }
    public int MaxMP { get; set; }
    public int Gold { get; set; }
    public int Speed { get; set; }
    public int Initiative { get; set; }
    public List<string> Updates { get; set; } = [];
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of updating a character skill.
/// </summary>
public class UpdateSkillResult
{
    public bool Success { get; set; }
    public string CharacterId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string SkillName { get; set; } = string.Empty;
    public int PreviousRank { get; set; }
    public int NewRank { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>
/// Result of adding or removing a known spell.
/// </summary>
public class UpdateSpellResult
{
    public bool Success { get; set; }
    public string CharacterId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string SpellName { get; set; } = string.Empty;
    public int SpellLevel { get; set; }
    public bool Removed { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}
