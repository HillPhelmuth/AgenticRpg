using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Models.Game;

/// <summary>
/// Represents a campaign world/setting
/// </summary>
public class World
{
    /// <summary>
    /// Unique identifier for the world
    /// </summary>
    [JsonPropertyName("id")]
    [Description("Unique identifier for the world")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// World display name
    /// </summary>
    [Description("The name of the world for a campaign")]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// World description and lore
    /// </summary>
    [Description("World description and lore")]
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// World genre/theme (e.g., High Fantasy, Dark Fantasy)
    /// </summary>
    [Description("World genre/theme (e.g., High Fantasy, Dark Fantasy, Light Comedic Fantasy)")]
    public string Theme { get; set; } = string.Empty;
    [Description("The frequency of combat a player can expect in the world")]
    public BattleFrequency BattleFrequency { get; set; } = BattleFrequency.Medium;
    /// <summary>
    /// Geography and climate details
    /// </summary>
    [Description("Geography and climate details")]
    public string Geography { get; set; } = string.Empty;
    
    /// <summary>
    /// Political structure and factions
    /// </summary>
    [Description("Political structure and factions")]
    public string Politics { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    /// <summary>
    /// List of locations in the world
    /// </summary>
    [Description("List of locations in the world")]
    public List<Location> Locations { get; set; } = [];
    
    /// <summary>
    /// List of NPCs in the world
    /// </summary>
    [Description("List of NPCs in the world")]
    public List<NPC> NPCs { get; set; } = [];
    
    /// <summary>
    /// List of quests available in the world
    /// </summary>
    [Description("List of quests available in the world")]
    public List<Quest> Quests { get; set; } = [];
    
    /// <summary>
    /// List of world events
    /// </summary>
    [Description("List of world events")]
    public List<WorldEvent> Events { get; set; } = [];
    
    /// <summary>
    /// Whether this is a premade template world
    /// </summary>
    [Description("Whether this is a premade template world")]
    public bool IsTemplate { get; set; } = false;
    
    /// <summary>
    /// ID of the user who created this world
    /// </summary>
    [Description("ID of the user who created this world")]
    public string CreatorId { get; set; } = string.Empty;
    
    /// <summary>
    /// Timestamp when world was created
    /// </summary>
    [Description("Timestamp when world was created")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Timestamp when world was last updated
    /// </summary>
    [Description("Timestamp when world was last updated")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string AsMarkdown()
    {
        var markdownBuilder = new StringBuilder();
        markdownBuilder.AppendLine($"### World: {Name}");
        markdownBuilder.AppendLine($"**Description:** {Description}");
        markdownBuilder.AppendLine($"**Theme:** {Theme}");
        markdownBuilder.AppendLine($"**Geography:** {Geography}");
        markdownBuilder.AppendLine($"**Politics:** {Politics}");
        // ToDo Replace this with `GetLocations` Tool call
        markdownBuilder.AppendLine("#### Locations");
        foreach (var location in Locations)
        {
            markdownBuilder.AppendLine($"**{location.Name}:** {location.Description}");
        }
        markdownBuilder.AppendLine("#### NPCs");
        foreach (var npc in NPCs)
        {
            markdownBuilder.AppendLine($"**{npc.Name}:** {npc.Description}");
        }
        markdownBuilder.AppendLine("#### Available Quests");
        foreach (var quest in Quests.Where(x => x.Status is not QuestStatus.Completed or QuestStatus.Failed))
        {
            markdownBuilder.AppendLine($"**{quest.Name}:** {quest.Description}");
        }
        markdownBuilder.AppendLine("#### World Events");
        foreach (var worldEvent in Events)
        {
            markdownBuilder.AppendLine($"**{worldEvent.Name}:** {worldEvent.Description}");
        }
        return markdownBuilder.ToString();
    }
}