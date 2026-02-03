using AgenticRpg.Core.Models.Enums;
using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models;

/// <summary>
/// Represents a campaign instance in the RPG system
/// </summary>
public class Campaign
{
    /// <summary>
    /// Unique identifier for the campaign
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Campaign display name
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Campaign description or summary
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// ID of the player who created/owns this campaign
    /// </summary>
    public string OwnerId { get; set; } = string.Empty;

    public List<string> InvitedUserIds { get; set; } = [];
    
    /// <summary>
    /// ID of the world this campaign takes place in
    /// </summary>
    public string WorldId { get; set; } = string.Empty;
    /// <summary>
    /// Current status of the campaign
    /// </summary>
    public CampaignStatus Status { get; set; } = CampaignStatus.Setup;
    
    /// <summary>
    /// List of player IDs participating in the campaign
    /// </summary>
    public List<string> PlayerIds { get; set; } = [];
    
    /// <summary>
    /// List of character IDs in the campaign
    /// </summary>
    public List<string> CharacterIds { get; set; } = [];
    
    /// <summary>
    /// ID of the character whose turn it currently is
    /// </summary>
    public string? CurrentTurnCharacterId { get; set; }
    
    /// <summary>
    /// Current combat encounter ID (if in combat)
    /// </summary>
    public string? CurrentCombatId { get; set; }
    
    /// <summary>
    /// Maximum number of players allowed
    /// </summary>
    public int MaxPlayers { get; set; } = 6;
    
    /// <summary>
    /// Campaign invitation code for joining
    /// </summary>
    public string InvitationCode { get; set; } = GenerateInvitationCode();
    
    /// <summary>
    /// Campaign settings and preferences
    /// </summary>
    public CampaignSettings Settings { get; set; } = new();
    
    /// <summary>
    /// Timestamp when campaign was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Timestamp when campaign was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Timestamp when campaign was last accessed
    /// </summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Session count
    /// </summary>
    public int SessionCount { get; set; } = 0;
    
    /// <summary>
    /// Generates a random invitation code for campaign joining
    /// </summary>
    private static string GenerateInvitationCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 6)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
    public Dictionary<string, PlayerReadyStatus> PlayerReadyStatuses { get; set; } = [];
    /// <summary>
    /// Checks if campaign is full
    /// </summary>
    public bool IsFull => PlayerIds.Count >= MaxPlayers;

    public string SelectedModel { get; set; } = "gpt-5.1";
}

/// <summary>
/// Campaign settings and preferences
/// </summary>
public class CampaignSettings
{
    /// <summary>
    /// Whether to allow late joining
    /// </summary>
    public bool AllowLateJoining { get; set; } = true;
    
    /// <summary>
    /// Whether to allow spectators
    /// </summary>
    public bool AllowSpectators { get; set; } = false;
    
    /// <summary>
    /// Starting character level
    /// </summary>
    public int StartingLevel { get; set; } = 1;
    
    /// <summary>
    /// Difficulty setting (1-5)
    /// </summary>
    public int Difficulty { get; set; } = 1;
    
    /// <summary>
    /// Whether to use critical hit rules
    /// </summary>
    public bool UseCriticalHits { get; set; } = true;
    
    /// <summary>
    /// Whether to track encumbrance
    /// </summary>
    public bool TrackEncumbrance { get; set; } = false;
}
