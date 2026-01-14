using System.ComponentModel;

namespace AgenticRpg.Core.Models.Game;

/// <summary>
/// Represents a combatant in combat (character or monster)
/// </summary>
public class Combatant
{
    [Description("Unique identifier for the combatant")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Description("Name of the combatant")]
    public string Name { get; set; } = string.Empty;
    
    [Description("Indicates whether this combatant is an enemy")]
    public bool IsEnemy { get; set; } = true;
    
    [Description("Character ID if this combatant is a player character, empty if monster")]
    public string CharacterId { get; set; } = string.Empty;
    
    [Description("Initiative roll value determining turn order in combat")]
    public int Initiative { get; set; }
    
    [Description("Current hit points of the combatant")]
    public int CurrentHP { get; set; }
    
    [Description("Maximum hit points of the combatant")]
    public int MaxHP { get; set; }
    
    [Description("Armor class determining difficulty to hit (0-18)")]
    public int ArmorClass { get; set; }
    
    [Description("Movement speed in feet per turn")]
    public int Speed { get; set; }
    
    [Description("List of active status effects affecting the combatant")]
    public List<StatusEffect> ActiveEffects { get; set; } = [];
    
    [Description("Challenge rating for monster combatants (0 for player characters)")]
    public int ChallengeRating { get; set; } = 0;
    
    [Description("Special abilities available to monster combatants")]
    public List<MonsterAbility> MonsterAbilities { get; set; } = [];
    
    [Description("Indicates whether the combatant has taken their turn in the current round")]
    public bool HasTakenTurn { get; set; } = false;
    public WeaponItem? EquippedWeapon { get; set; }
    
    
    /// <summary>
    /// Checks if combatant is alive
    /// </summary>
    [Description("Indicates whether the combatant is still alive (CurrentHP > 0)")]
    public bool IsAlive => CurrentHP > 0;
}

/// <summary>
/// Represents a status effect on a combatant
/// </summary>
public class StatusEffect
{
    [Description("Name of the status effect")]
    public string Name { get; set; } = string.Empty;
    
    [Description("Description of what the status effect does")]
    public string Description { get; set; } = string.Empty;
    
    [Description("Number of rounds remaining for this status effect")]
    public int Duration { get; set; } = 0;
    [Description("Type of damage dealt by this ability if applicable")]
    public string DamageType { get; set; } = string.Empty;

    [Description("Dictionary of stat modifiers applied by this effect (stat name to modifier value)")]
    public Dictionary<string, int> Modifiers { get; set; } = [];
}

/// <summary>
/// Represents a special ability for monsters
/// </summary>
public class MonsterAbility : Ability
{
    [Description("Name of the monster ability")]
    public string Name { get; set; } = string.Empty;
    
    [Description("Description of what the ability does")]
    public string Description { get; set; } = string.Empty;
    
    [Description("Type of ability (Attack, Defense, Utility)")]
    public string Type { get; set; } = string.Empty;
    
    [Description("Type of damage dealt by this ability if applicable")]
    public string DamageType { get; set; } = string.Empty;
    [Description("Damage dice used by this ability if applicable (e.g. 2d6)")]
    public string? DamageDice { get; set; }
    
    [Description("Effect of the ability when used")]
    public string Effect { get; set; }
}

/// <summary>
/// Represents an entry in the combat log
/// </summary>
public class CombatLogEntry
{
    [Description("Unique identifier for the log entry")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Description("UTC timestamp when the action occurred")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    [Description("Combat round number when the action occurred")]
    public int Round { get; set; }
    
    [Description("ID of the combatant performing the action")]
    public string CombatantId { get; set; } = string.Empty;
    
    [Description("Name of the combatant performing the action")]
    public string CombatantName { get; set; } = string.Empty;
    
    [Description("Type of action taken (Attack, Spell, Move, etc.)")]
    public string ActionType { get; set; } = string.Empty;
    
    [Description("Detailed description of the action")]
    public string Description { get; set; } = string.Empty;
    
    [Description("ID of the target combatant if applicable")]
    public string TargetId { get; set; } = string.Empty;
    
    [Description("Name of the target combatant if applicable")]
    public string TargetName { get; set; } = string.Empty;
    
    [Description("Result of any dice roll associated with the action")]
    public int? RollResult { get; set; }
    
    [Description("Amount of damage dealt by the action if applicable")]
    public int? DamageDealt { get; set; }
    
    [Description("Indicates whether the action was a critical hit")]
    public bool IsCritical { get; set; } = false;
}