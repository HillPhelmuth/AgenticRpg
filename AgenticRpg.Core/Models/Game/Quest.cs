using System.ComponentModel;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Models.Game;

/// <summary>
/// Represents a quest
/// </summary>
public class Quest
{
    [Description("Unique identifier for the quest")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Description("Quest display name")]
    public string Name { get; set; } = string.Empty;
    
    [Description("Quest description and details")]
    public string Description { get; set; } = string.Empty;
    
    [Description("Name of the NPC or entity that gives this quest")]
    public string QuestGiver { get; set; } = string.Empty;
    
    [Description("Location ID where the quest is available or starts")]
    public string LocationId { get; set; } = string.Empty;
    
    [Description("List of quest objectives")]
    public List<string> Objectives { get; set; } = [];
    
    [Description("Tracking of completed objectives")]
    public Dictionary<string, bool> CompletedObjectives { get; set; } = [];
    
    [Description("Current status of the quest")]
    public QuestStatus Status { get; set; } = QuestStatus.Available;
    
    [Description("Rewards given upon quest completion")]
    public QuestReward Reward { get; set; } = new();
}
/// <summary>
/// Quest reward information
/// </summary>
public class QuestReward
{
    [Description("Amount of gold awarded")]
    public int Gold { get; set; } = 0;

    [Description("Amount of experience points awarded")]
    public int Experience { get; set; } = 0;

    [Description("List of items awarded as rewards")]
    public List<InventoryItem> Items { get; set; } = [];
}