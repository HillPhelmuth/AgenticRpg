using System.ComponentModel;
using System.Text;
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
    [Description("List of monsters involved in the quest, either as targets or allies")]
    public List<string> MonsterNames { get; set; } = [];

    public string AsMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### Quest: {Name}");
        sb.AppendLine($"**Description**: {Description}");
        sb.AppendLine($"**Quest Giver**: {QuestGiver}");
        sb.AppendLine($"**Location**: {LocationId}");
        sb.AppendLine($"**Objectives**: {string.Join(", ", Objectives)}");
        sb.AppendLine($"**Status**: {Status}");
        sb.AppendLine($"**Reward**: {Reward.AsMarkdown()}");
        return sb.ToString();
    }
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

    [Description("List of items awarded as rewards. Includes gems, jewels, exotic or everyday items, weapons, armor, and magical gear.")]
    public List<InventoryItem> Items { get; set; } = [];
    //[Description("Optional list of armor items awarded as rewards for completing quest")]
    //public List<ArmorItem> ArmorRewards { get; set; } = [];
    //[Description("Optional list of weapon items awarded as rewards for completing quest")]
    //public List<WeaponItem> WeaponRewards { get; set; } = [];
    //[Description("Optional list of magic items awarded as rewards for completing quest")]
    //public List<MagicItem> MagicItemRewards { get; set; } = [];

    public string AsMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Gold**: {Gold}");
        sb.AppendLine($"**Experience**: {Experience}");
        sb.AppendLine($"**Items**: {string.Join(", ", Items.Select(item => item.Name))}");
        //sb.AppendLine($"**Armor Rewards**: {string.Join(", ", ArmorRewards.Select(item => item.Name))}");
        //sb.AppendLine($"**Weapon Rewards**: {string.Join(", ", WeaponRewards.Select(item => item.Name))}");
        //sb.AppendLine($"**Magic Item Rewards**: {string.Join(", ", MagicItemRewards.Select(item => item.Name))}");
        return sb.ToString();
    }
}