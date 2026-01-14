using System.ComponentModel;

namespace AgenticRpg.Core.Models.Game;

/// <summary>
/// Represents a location in the world
/// </summary>
public class Location
{
    [Description("Unique identifier for the location")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Description("Location display name")]
    public string Name { get; set; } = string.Empty;
    
    [Description("Location description")]
    public string Description { get; set; } = string.Empty;
    
    [Description("Type of location (e.g., Town, Dungeon, Wilderness)")]
    public string Type { get; set; } = string.Empty; // Town, Dungeon, Wilderness, etc.
    
    [Description("Descriptive tags for the location")]
    public List<string> Tags { get; set; } = [];
    
    [Description("Notable points of interest at this location")]
    public List<string> PointsOfInterest { get; set; } = [];
    
    [Description("Connected locations mapped to their distance")]
    public Dictionary<string, int> ConnectedLocations { get; set; } = []; // LocationId -> Distance
}