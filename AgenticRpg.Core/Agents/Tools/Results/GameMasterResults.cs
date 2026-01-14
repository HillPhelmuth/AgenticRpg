using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Agents.Tools.Results;

// RollSkillCheck result type
public class SkillCheckResult
{
    public bool Success { get; set; }
    public string? Character { get; set; }
    public string? Skill { get; set; }
    public string? Attribute { get; set; }
    public int Roll { get; set; }
    public int AttributeModifier { get; set; }
    public int SkillRank { get; set; }
    public int Total { get; set; }
    public int DC { get; set; }
    public required string Description { get; set; }
    public string? Error { get; set; }
}

// InitiateCombat result type
public class InitiateCombatResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? CombatId { get; set; }
    public string[]? Enemies { get; set; }
    public string? Description { get; set; }
    public string? Error { get; set; }
}

// RecordNarrative result type
public class RecordNarrativeResult
{
    public bool Success { get; set; }
    public string? NarrativeId { get; set; }
    public required string Message { get; set; }
}

// AwardExperience result types
public class AwardExperienceResult
{
    public bool Success { get; set; }
    public required List<ExperienceAwardDetail> Results { get; set; }
    public required string Message { get; set; }
}

public class ExperienceAwardDetail
{
    public string? CharacterName { get; set; }
    public string? CharacterId { get; set; }
    public bool Success { get; set; }
    public int OldXP { get; set; }
    public int NewXP { get; set; }
    public int XPGained { get; set; }
    public int OldLevel { get; set; }
    public int NewLevel { get; set; }
    public bool LeveledUp { get; set; }
    public required string Reason { get; set; }
    public string? Error { get; set; }
}

// UpdateWorldState result type
public class UpdateWorldStateResult
{
    public bool Success { get; set; }
    public required List<string> Updates { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

// HandoffToAgent result type
public class HandoffToAgentResult
{
    public bool Success { get; set; }
    public string? PreviousAgent { get; set; }
    public string? NewAgent { get; set; }
    public string? Context { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

public class CharacterDetailsResult
{
    public bool Success { get; set; }
    public CharacterDetailSnapshot? Character { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public class CharacterDetailSnapshot
{
    public string? CharacterId { get; set; }
    public string? Name { get; set; }
    public string? PlayerId { get; set; }
    public string? Race { get; set; }
    public string? Class { get; set; }
    public int Level { get; set; }
    public int Experience { get; set; }
    public int CurrentHP { get; set; }
    public int MaxHP { get; set; }
    public int CurrentMP { get; set; }
    public int MaxMP { get; set; }
    public int TempHP { get; set; }
    public int ArmorClass { get; set; }
    public int Initiative { get; set; }
    public int Speed { get; set; }
    public Dictionary<AttributeType, int> Attributes { get; set; } = new();
    public Dictionary<string, int>? Skills { get; set; }
    public List<InventoryItem>? Inventory { get; set; }
    public EquippedItems? Equipment { get; set; }
    public List<Spell>? KnownSpells { get; set; }
    public Dictionary<int, int>? CurrentSpellSlots { get; set; }
    public Dictionary<int, int>? MaxSpellSlots { get; set; }
    public int Gold { get; set; }
    public string? Background { get; set; }
    public string? PortraitUrl { get; set; }
    public bool IsAlive { get; set; }
    public bool IsUnconscious { get; set; }
}

public class WorldDetailsResult
{
    public bool Success { get; set; }
    public WorldDetailSnapshot? World { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public class WorldDetailSnapshot
{
    public string? WorldId { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Theme { get; set; }
    public string? Geography { get; set; }
    public string? Politics { get; set; }
    public string? CurrentLocationId { get; set; }
    public string? CurrentLocationName { get; set; }
    public Location? CurrentLocation { get; set; }
    public List<Location>? Locations { get; set; }
    public List<NPC>? NPCs { get; set; }
    public List<Quest>? Quests { get; set; }
    public List<WorldEvent>? Events { get; set; }
    public string? Weather { get; set; }
    public string? TimeOfDay { get; set; }
}

public class NarrativeSummaryResult
{
    public bool Success { get; set; }
    public List<NarrativeSummaryItem> Narratives { get; set; } = new();
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public class NarrativeSummaryItem
{
    public string? NarrativeId { get; set; }
    public string? Type { get; set; }
    public NarrativeVisibility Visibility { get; set; }
    public string? Content { get; set; }
    public string? AgentType { get; set; }
    public string? TargetCharacterId { get; set; }
    public DateTime Timestamp { get; set; }
}

public class CharacterUpdateResult
{
    public bool Success { get; set; }
    public CharacterDetailSnapshot? Character { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public class WorldUpdateResult
{
    public bool Success { get; set; }
    public WorldDetailSnapshot? World { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}
