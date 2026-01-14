using AgenticRpg.Core.Models.Enums;
using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models;

/// <summary>
/// Represents a narrative entry in the campaign story
/// </summary>
public class Narrative
{
    /// <summary>
    /// Unique identifier for the narrative entry
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// ID of the campaign this narrative belongs to
    /// </summary>
    public string CampaignId { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of narrative entry (e.g., Story, Combat, Dialogue, System)
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// The agent that created this narrative (e.g., GameMaster, Combat, etc.)
    /// </summary>
    public string AgentType { get; set; } = "GameMaster";
    
    /// <summary>
    /// The narrative content (supports Markdown)
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Who can see this narrative entry
    /// </summary>
    public NarrativeVisibility Visibility { get; set; } = NarrativeVisibility.Global;
    
    /// <summary>
    /// Specific character ID if visibility is CharacterSpecific
    /// </summary>
    public string? TargetCharacterId { get; set; }
    
    /// <summary>
    /// ID of the speaker (character, NPC, or system)
    /// </summary>
    public string SpeakerId { get; set; } = string.Empty;
    
    /// <summary>
    /// Name of the speaker for display
    /// </summary>
    public string SpeakerName { get; set; } = string.Empty;
    
    /// <summary>
    /// Related location ID
    /// </summary>
    public string? LocationId { get; set; }
    
    /// <summary>
    /// Related quest ID
    /// </summary>
    public string? QuestId { get; set; }
    
    /// <summary>
    /// Tags for categorization and filtering
    /// </summary>
    public List<string> Tags { get; set; } = [];
    
    /// <summary>
    /// Timestamp when this narrative was created
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Sequence number for ordering
    /// </summary>
    public long SequenceNumber { get; set; }
}
