using AgenticRpg.Core.Models.Enums;
using System.Text.Json.Serialization;
using AgenticRpg.Core.Agents.Tools;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Models;

/// <summary>
/// Represents the complete game state for a campaign
/// </summary>
public class GameState
{

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// ID of the campaign
    /// </summary>
    public string CampaignId => Campaign.Id;
    public string? SelectedModel { get; set; }
    /// <summary>
    /// Campaign information
    /// </summary>
    public Campaign Campaign { get; set; } = new();

    /// <summary>
    /// World information
    /// </summary>
    public World World { get; set; } = new();

    /// <summary>
    /// All characters in the campaign
    /// </summary>
    public List<Character> Characters { get; set; } = [];

    /// <summary>
    /// Current combat encounter (if in combat)
    /// </summary>
    public CombatEncounter? CurrentCombat { get; set; }

    public List<CombatRewards> PartyCombatRewards { get; set; } = [];
    /// <summary>
    /// Recent narrative entries (limited for performance)
    /// </summary>
    public List<Narrative> RecentNarratives { get; set; } = [];

    /// <summary>
    /// Current location of the party
    /// </summary>
    public string? CurrentLocationId { get; set; }

    /// <summary>
    /// Active agent type (GameMaster, Combat, Economy, etc.)
    /// </summary>
    public string ActiveAgent { get; set; } = "GameMaster";
    public AgentType ActiveAgentType
    {
        get => Enum.TryParse<AgentType>(ActiveAgent, out var agentType)
            ? agentType
            : AgentType.GameMaster;
        set => ActiveAgent = value.ToString();
        
    }
    /// <summary>
    /// Whether the game is currently processing an action
    /// </summary>
    public bool IsProcessing { get; set; } = false;

    /// <summary>
    /// Timestamp of last state update
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Version number for optimistic concurrency
    /// </summary>
    public long Version { get; set; } = 0;

    /// <summary>
    /// Additional metadata for storing temporary state or agent-specific data
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>
    /// Player ready status tracking (PlayerId -> PlayerReadyStatus)
    /// </summary>
    public Dictionary<string, PlayerReadyStatus> PlayerReadyStatuses { get; set; } = [];

    /// <summary>
    /// Gets a character by ID
    /// </summary>
    public Character? GetCharacter(string characterId)
    {
        return Characters.FirstOrDefault(c => c.Id == characterId || c.Name == characterId);
    }

    /// <summary>
    /// Gets all alive party members
    /// </summary>
    public List<Character> GetAlivePartyMembers()
    {
        return Characters.Where(c => c.IsAlive).ToList();
    }

    /// <summary>
    /// Checks if the campaign is in combat
    /// </summary>
    public bool IsInCombat => CurrentCombat is { Status: CombatStatus.Active };

    /// <summary>
    /// Gets ready status for a player
    /// </summary>
    public PlayerReadyStatus? GetReadyStatus(string playerId)
    {
        return PlayerReadyStatuses.GetValueOrDefault(playerId);
    }

    /// <summary>
    /// Sets ready status for a player
    /// </summary>
    public void SetReadyStatus(string playerId, string characterId, string playerName, bool isReady, string? connectionId = null)
    {
        if (!PlayerReadyStatuses.TryGetValue(playerId, out var status))
        {
            status = new PlayerReadyStatus
            {
                PlayerId = playerId,
                CharacterId = characterId,
                PlayerName = playerName
            };
            PlayerReadyStatuses[playerId] = status;
        }

        status.IsReady = isReady;
        status.ReadyAt = isReady ? DateTime.UtcNow : null;
        status.ConnectionId = connectionId;
    }

    /// <summary>
    /// Checks if all players in the campaign are ready
    /// </summary>
    public bool AreAllPlayersReady()
    {
        // Must have at least one player
        if (PlayerReadyStatuses.Count == 0)
            return false;

        // All players must be ready
        return PlayerReadyStatuses.Values.All(s => s.IsReady);
    }

    /// <summary>
    /// Resets all player ready statuses to false
    /// </summary>
    public void ResetReadyStatuses()
    {
        foreach (var status in PlayerReadyStatuses.Values)
        {
            status.IsReady = false;
            status.ReadyAt = null;
        }
    }

    /// <summary>
    /// Gets the character ID for a player
    /// </summary>
    public string? GetPlayerCharacterId(string playerId)
    {
        return PlayerReadyStatuses.TryGetValue(playerId, out var status) ? status.CharacterId : null;
    }
}
