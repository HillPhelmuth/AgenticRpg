using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Agents.Tools.Results;

// GenerateLocation result type
public class GenerateLocationResult
{
    public bool Success { get; set; }
    public Location? Location { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

// CreateNPC result type
public class CreateNPCResult
{
    public bool Success { get; set; }
    public NPC? NPC { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

// DesignQuest result type
public class DesignQuestResult
{
    public bool Success { get; set; }
    public Quest? Quest { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

// PopulateEncounterTable result type
public class PopulateEncounterTableResult
{
    public bool Success { get; set; }
    public string? LocationId { get; set; }
    public required List<EncounterEntry> Encounters { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

public class EncounterEntry
{
    public required string EncounterType { get; set; }
    public required string Description { get; set; }
    public int DifficultyLevel { get; set; }
    public int Probability { get; set; }
}

// BuildWorldLore result type
public class BuildWorldLoreResult
{
    public bool Success { get; set; }
    public WorldEvent? LoreEntry { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

// SaveWorld result type
public class SaveWorldResult
{
    public bool Success { get; set; }
    public string? WorldId { get; set; }
    public string? WorldName { get; set; }
    public int LocationCount { get; set; }
    public int NPCCount { get; set; }
    public int QuestCount { get; set; }
    public int LoreEntryCount { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

// QuickCreateWorld result type
public class QuickCreateWorldResult
{
    public bool Success { get; set; }
    public World? World { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}
