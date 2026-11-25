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
    public string Id { get => string.IsNullOrWhiteSpace(CampaignId) ? field : CampaignId; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// ID of the campaign
    /// </summary>
    public string CampaignId => Campaign.Id;

    public string? SelectedModel
    {
        get =>  field ?? Campaign?.SelectedModel;
        set
        {
            field = value;
        }
    }

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
    /// Registers a player join or reconnect for the campaign.
    /// </summary>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="characterId">The character identifier.</param>
    /// <param name="playerName">The player display name.</param>
    /// <param name="connectionId">The active SignalR connection identifier.</param>
    public void PlayerJoinGame(string playerId, string characterId, string playerName, string? connectionId = null)
    {
        Campaign.MarkPlayerJoined(playerId, characterId);
        Campaign.MarkPlayerConnected(playerId);

        // Reason: Preserve an existing ready flag on reconnect, but refresh who and what the player is bound to.
        if (!PlayerReadyStatuses.TryGetValue(playerId, out var status))
        {
            status = new PlayerReadyStatus
            {
                PlayerId = playerId,
                CharacterId = characterId,
                PlayerName = playerName,
                IsReady = false
            };
            PlayerReadyStatuses[playerId] = status;
        }

        status.CharacterId = characterId;
        status.PlayerName = playerName;
        status.ConnectionId = connectionId;
    }

    /// <summary>
    /// Marks a player as disconnected without removing them from the campaign.
    /// </summary>
    /// <param name="playerId">The player identifier.</param>
    public void PlayerDisconnect(string playerId)
    {
        Campaign.MarkPlayerDisconnected(playerId);

        if (PlayerReadyStatuses.TryGetValue(playerId, out var readyStatus))
        {
            readyStatus.ConnectionId = null;
            readyStatus.IsReady = false;
            readyStatus.ReadyAt = null;
        }
    }

    /// <summary>
    /// Current location of the party
    /// </summary>
    public string? CurrentLocationId { get; set; }

    /// <summary>
    /// Removes a player and their campaign character from the campaign.
    /// </summary>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="characterId">Optional character identifier when known.</param>
    public void PlayerLeaveGame(string playerId, string? characterId = null)
    {
        Campaign.MarkPlayerLeft(playerId, characterId);

        var charList = Characters.ToList();
        foreach (var character in charList.Where(character => character.PlayerId == playerId || character.Id == characterId))
        {
            var removedChar = Characters.FirstOrDefault(c => c.Id == character.Id);
            if (removedChar is null)
            {
                continue;
            }

            Campaign.MarkPlayerLeft(playerId, removedChar.Id);
            Characters.Remove(removedChar);
        }
    }
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
    public Dictionary<string, PlayerReadyStatus> PlayerReadyStatuses
    {
        get => Campaign.PlayerReadyStatuses;
        set => Campaign.PlayerReadyStatuses = value;
    }

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
