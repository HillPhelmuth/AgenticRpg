using System.ComponentModel;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Models.Game;

/// <summary>
/// Represents a non-player character
/// </summary>
public class NPC
{
    [Description("Unique identifier for the NPC")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Description("NPC display name")]
    public string Name { get; set; } = string.Empty;
    
    [Description("NPC description and background")]
    public string Description { get; set; } = string.Empty;
    
    [Description("NPC's role or occupation")]
    public string Role { get; set; } = string.Empty;
    
    [Description("NPC's disposition towards players (e.g., Friendly, Neutral, Hostile)")]
    public NPCDisposition Disposition { get; set; } = NPCDisposition.Neutral;

    [Description("Current location ID where the NPC is located")]
    public string CurrentLocationId { get; set; } = string.Empty;
    
    [Description("NPC's current status (e.g., Alive, Dead, Unknown)")]
    public string Status { get; set; } = "Alive"; // Alive, Dead, Unknown
    
    [Description("Additional NPC attributes and properties")]
    public Dictionary<string, object> Attributes { get; set; } = [];
}