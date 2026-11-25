using AgenticRpg.Core.Models.Enums;
using System.Text.Json.Serialization;
using AgenticRpg.Core.Models.Game;

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
    public CampaignStatus Status
    {
        get
        {
            return GetCampaignStatus();
        }
    }

    /// <summary>
    /// Timestamp when the campaign was first started.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? StartedAt { get; set; }

    public CampaignStatus GetCampaignStatus()
    {
        var hasJoinedPlayers = PlayerIds.Count != 0 || CharacterIds.Count != 0 || PlayerReadyStatuses.Count != 0;
        var hasStarted = HasStarted();
        var activePlayerCount = GetActiveConnectedPlayerIds().Count;

        if (!hasStarted)
        {
            return hasJoinedPlayers
                ? CampaignStatus.Lobby
                : CampaignStatus.Setup;
        }

        return activePlayerCount > 0
            ? CampaignStatus.Active
            : CampaignStatus.Paused;
    }
    /// <summary>
    /// List of player IDs participating in the campaign
    /// </summary>
    public List<string> PlayerIds { get; set; } = [];
    
    /// <summary>
    /// List of character IDs in the campaign
    /// </summary>
    public List<string> CharacterIds { get; set; } = [];

    /// <summary>
    /// Player IDs with at least one active SignalR connection in this campaign.
    /// </summary>
    public List<string> ActiveAndConnectedPlayerIds { get; set; } = [];

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
    public Quest? PrimaryQuest { get; set; }

    /// <summary>
    /// Marks the campaign as started.
    /// </summary>
    public void MarkStarted()
    {
        StartedAt ??= DateTime.UtcNow;
        SessionCount = Math.Max(SessionCount, 1);
        UpdatedAt = DateTime.UtcNow;
        LastAccessedAt = UpdatedAt;
    }

    /// <summary>
    /// Registers a player as a participant in the campaign.
    /// </summary>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="characterId">The controlled character identifier.</param>
    public void MarkPlayerJoined(string playerId, string characterId)
    {
        if (!string.IsNullOrWhiteSpace(playerId) && !PlayerIds.Contains(playerId, StringComparer.OrdinalIgnoreCase))
        {
            PlayerIds.Add(playerId);
        }

        if (!string.IsNullOrWhiteSpace(characterId) && !CharacterIds.Contains(characterId, StringComparer.OrdinalIgnoreCase))
        {
            CharacterIds.Add(characterId);
        }

        UpdatedAt = DateTime.UtcNow;
        LastAccessedAt = UpdatedAt;
    }

    /// <summary>
    /// Removes a player from the active connection roster.
    /// </summary>
    /// <param name="playerId">The player identifier.</param>
    public void MarkPlayerDisconnected(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        ActiveAndConnectedPlayerIds = ActiveAndConnectedPlayerIds
            .Where(id => !string.Equals(id, playerId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        UpdatedAt = DateTime.UtcNow;
        LastAccessedAt = UpdatedAt;
    }

    /// <summary>
    /// Adds a player to the active connection roster.
    /// </summary>
    /// <param name="playerId">The player identifier.</param>
    public void MarkPlayerConnected(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        if (!ActiveAndConnectedPlayerIds.Contains(playerId, StringComparer.OrdinalIgnoreCase))
        {
            ActiveAndConnectedPlayerIds.Add(playerId);
        }

        UpdatedAt = DateTime.UtcNow;
        LastAccessedAt = UpdatedAt;
    }

    /// <summary>
    /// Removes a player from the campaign roster.
    /// </summary>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="characterId">Optional character identifier to remove.</param>
    public void MarkPlayerLeft(string playerId, string? characterId = null)
    {
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            PlayerIds = PlayerIds
                .Where(id => !string.Equals(id, playerId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            ActiveAndConnectedPlayerIds = ActiveAndConnectedPlayerIds
                .Where(id => !string.Equals(id, playerId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            PlayerReadyStatuses.Remove(playerId);
        }

        if (!string.IsNullOrWhiteSpace(characterId))
        {
            CharacterIds = CharacterIds
                .Where(id => !string.Equals(id, characterId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        UpdatedAt = DateTime.UtcNow;
        LastAccessedAt = UpdatedAt;
    }
    
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

    public string SelectedModel { get; set; } = "openai/gpt-5.4";

    private bool HasStarted()
    {
        return StartedAt is not null
               || SessionCount > 0
               || !string.IsNullOrWhiteSpace(CurrentTurnCharacterId)
               || !string.IsNullOrWhiteSpace(CurrentCombatId);
    }

    private List<string> GetActiveConnectedPlayerIds()
    {
        var activePlayerIds = ActiveAndConnectedPlayerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        foreach (var readyStatus in PlayerReadyStatuses.Values)
        {
            if (!string.IsNullOrWhiteSpace(readyStatus.PlayerId)
                && !string.IsNullOrWhiteSpace(readyStatus.ConnectionId)
                && !activePlayerIds.Contains(readyStatus.PlayerId, StringComparer.OrdinalIgnoreCase))
            {
                activePlayerIds.Add(readyStatus.PlayerId);
            }
        }

        return activePlayerIds;
    }
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
    /// Starting character level
    /// </summary>
    public int StartingLevel { get; set; } = 1;
    
    /// <summary>
    /// Difficulty setting (1-15)
    /// </summary>
    public int Difficulty { get; set; } = 1;
    
    public string DefaultModel { get; set; } = "openai/gpt-5.4";
    public int MaxPlayers { get; set; } = 5;
    public string? PrimaryQuestDescription { get; set; }
    public bool GeneratePrimaryQuest { get; set; }
}
