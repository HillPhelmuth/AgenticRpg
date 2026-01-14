using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Models;

/// <summary>
/// Represents a combat encounter
/// </summary>
public class CombatEncounter
{
    /// <summary>
    /// Unique identifier for the combat encounter
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// ID of the campaign this combat belongs to
    /// </summary>
    public string CampaignId { get; set; } = string.Empty;
    
    /// <summary>
    /// Current status of the combat
    /// </summary>
    public CombatStatus Status { get; set; } = CombatStatus.Initializing;
    
    public List<Character> PartyCharacters { get; set; } = [];
    public List<RpgMonster> EnemyMonsters { get; set; } = [];

    /// <summary>
    /// The rolled initiative values for each participant (keyed by participant id).
    /// Characters use Character.Id; monsters use Monster.Name.
    /// </summary>
    public Dictionary<string, int> InitiativeRolls { get; set; } = [];
    /// <summary>
    /// Initiative order (combatant IDs in turn order)
    /// </summary>
    public List<string> InitiativeOrder { get; set; } = [];
    
    /// <summary>
    /// Index of current combatant in initiative order
    /// </summary>
    public int CurrentTurnIndex { get; set; } = 0;
    
    /// <summary>
    /// Current round number
    /// </summary>
    public int Round { get; set; } = 1;
    
    /// <summary>
    /// Combat log entries
    /// </summary>
    public List<CombatLogEntry> CombatLog { get; set; } = [];
    
    /// <summary>
    /// Terrain or environmental effects
    /// </summary>
    public string Terrain { get; set; } = string.Empty;
    
    /// <summary>
    /// Timestamp when combat started
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Timestamp when combat ended
    /// </summary>
    public DateTime? EndedAt { get; set; }
    
    public string? GetCurrentTurnId()
    {
        if (InitiativeOrder.Count == 0 || CurrentTurnIndex < 0 || CurrentTurnIndex >= InitiativeOrder.Count)
        {
            return null;
        }

        return InitiativeOrder[CurrentTurnIndex];
    }

    public string? GetCurrentTurnName()
    {
        var id = GetCurrentTurnId();
        return id == null ? null : GetParticipantName(id);
    }

    public Character? FindPartyCharacter(string idOrName)
    {
        return PartyCharacters.FirstOrDefault(c =>
            string.Equals(c.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public RpgMonster? FindEnemyMonster(string idOrName)
    {
        return EnemyMonsters.FirstOrDefault(m =>
            string.Equals(m.Name, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Id, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsEnemyParticipant(string idOrName)
    {
        return FindEnemyMonster(idOrName) != null;
    }

    public string? GetParticipantName(string idOrName)
    {
        var pc = FindPartyCharacter(idOrName);
        if (pc != null) return pc.Name;

        var monster = FindEnemyMonster(idOrName);
        return monster?.Name;
    }
    
    /// <summary>
    /// Advances to the next turn
    /// </summary>
    public void NextTurn()
    {
        CurrentTurnIndex++;
        if (CurrentTurnIndex >= InitiativeOrder.Count)
        {
            CurrentTurnIndex = 0;
            Round++;
        }
    }
    
    /// <summary>
    /// Checks if all enemies are defeated
    /// </summary>
    public bool AllEnemiesDefeated()
    {
        return EnemyMonsters.All(m => m.CurrentHP <= 0);
    }
    
    /// <summary>
    /// Checks if all party members are defeated
    /// </summary>
    public bool AllPartyDefeated()
    {
        return PartyCharacters.All(c => c.CurrentHP <= 0);
    }
}


