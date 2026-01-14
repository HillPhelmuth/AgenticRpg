using System.ComponentModel;

namespace AgenticRpg.Core.Models.Game;

/// <summary>
/// Represents a world event
/// </summary>
public class WorldEvent
{
    [Description("Unique identifier for the world event")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Description("World event display name")]
    public string Name { get; set; } = string.Empty;
    
    [Description("World event description and details")]
    public string Description { get; set; } = string.Empty;
    
    [Description("Current status of the event (e.g., Active, Resolved)")]
    public string Status { get; set; } = "Active"; // Active, Resolved
    
    [Description("Timestamp when the event started")]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    
    [Description("List of location IDs affected by this event")]
    public List<string> AffectedLocationIds { get; set; } = [];
    
    [Description("List of NPC IDs affected by this event")]
    public List<string> AffectedNPCIds { get; set; } = [];
    
    [Description("Descriptive tags for the event")]
    public List<string> Tags { get; set; } = [];
    
    [Description("Additional notes about the event")]
    public string Notes { get; set; } = string.Empty;
}