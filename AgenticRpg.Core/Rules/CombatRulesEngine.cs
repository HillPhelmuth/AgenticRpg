using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Rules;

/// <summary>
/// Handles combat mechanics and rule resolution
/// </summary>
public class CombatRulesEngine
{
    private readonly Random _random = new();
    
    /// <summary>
    /// Rolls initiative for a combatant
    /// </summary>
    /// <param name="initiativeModifier">The combatant's initiative modifier</param>
    /// <returns>Initiative roll result</returns>
    public int RollInitiative(int initiativeModifier)
    {
        var roll = RollD20();
        return roll + initiativeModifier;
    }

    /// <summary>
    /// Resolves an attack roll
    /// </summary>
    /// <param name="attackBonus">The attacker's attack bonus</param>
    /// <param name="targetAC">The target's armor class</param>
    /// <param name="rollVal"></param>
    /// <param name="hasAdvantage">Whether the attack has advantage</param>
    /// <param name="hasDisadvantage">Whether the attack has disadvantage</param>
    /// <returns>Attack result with hit status and critical info</returns>
    public AttackResult ResolveAttack(int attackBonus, int targetAC,
        int rollVal,
        bool hasAdvantage = false, bool hasDisadvantage = false)
    {
        var roll = rollVal;
        var total = roll + attackBonus;
        
        var isCritical = roll == 20;
        var isCriticalMiss = roll == 1;
        var isHit = (total >= targetAC && !isCriticalMiss) || isCritical;
        
        return new AttackResult
        {
            Roll = roll,
            Total = total,
            IsHit = isHit,
            IsCritical = isCritical,
            IsCriticalMiss = isCriticalMiss
        };
    }
    
    /// <summary>
    /// Rolls damage for an attack
    /// </summary>
    /// <param name="damageDice">Damage dice notation (e.g., "2d6")</param>
    /// <param name="damageModifier">Damage modifier to add</param>
    /// <param name="isCritical">Whether this is a critical hit (doubles dice)</param>
    /// <returns>Total damage dealt</returns>
    public int RollDamage(string damageDice, int damageModifier, bool isCritical = false)
    {
        var damage = 0;
        
        // Parse dice notation (e.g., "2d6" means 2 six-sided dice)
        var parts = damageDice.ToLower().Split('d');
        if (parts.Length == 2 && 
            int.TryParse(parts[0], out var count) && 
            int.TryParse(parts[1], out var sides))
        {
            // Roll dice
            var rolls = isCritical ? count * 2 : count;
            for (var i = 0; i < rolls; i++)
            {
                damage += _random.Next(1, sides + 1);
            }
        }
        
        // Add modifier (not doubled on crit)
        damage += damageModifier;
        
        return Math.Max(0, damage);
    }
    
    /// <summary>
    /// Resolves a saving throw
    /// </summary>
    /// <param name="savingThrowModifier">The character's saving throw modifier</param>
    /// <param name="difficultyClass">The DC to beat</param>
    /// <param name="hasAdvantage">Whether the save has advantage</param>
    /// <param name="hasDisadvantage">Whether the save has disadvantage</param>
    /// <returns>True if the save succeeds</returns>
    public bool ResolveSavingThrow(int savingThrowModifier, int difficultyClass,
        bool hasAdvantage = false, bool hasDisadvantage = false)
    {
        var roll = RollD20WithAdvantage(hasAdvantage, hasDisadvantage);
        var total = roll + savingThrowModifier;
        
        return total >= difficultyClass || roll == 20;
    }
    
    /// <summary>
    /// Calculates the difficulty class for a spell or ability
    /// Formula: 8 + proficiency bonus + spellcasting modifier
    /// </summary>
    public int CalculateSpellDC(int proficiencyBonus, int spellcastingModifier)
    {
        return 8 + proficiencyBonus + spellcastingModifier;
    }
    
    /// <summary>
    /// Applies damage to a combatant, accounting for temp HP
    /// </summary>
    /// <param name="combatant">The combatant taking damage</param>
    /// <param name="damage">Amount of damage</param>
    public void ApplyDamage(Combatant combatant, int damage)
    {
        if (damage <= 0)
            return;
        
        // Temp HP absorbs damage first
        if (combatant.CurrentHP > 0)
        {
            var tempHPDamage = Math.Min(combatant.CurrentHP, damage);
            combatant.CurrentHP -= tempHPDamage;
            damage -= tempHPDamage;
        }
        
        // Apply remaining damage to HP
        combatant.CurrentHP = Math.Max(0, combatant.CurrentHP - damage);
    }
    
    /// <summary>
    /// Applies healing to a combatant
    /// </summary>
    public void ApplyHealing(Combatant combatant, int healing)
    {
        if (healing <= 0)
            return;
        
        combatant.CurrentHP = Math.Min(combatant.MaxHP, combatant.CurrentHP + healing);
    }
    
    /// <summary>
    /// Rolls a d20 with advantage or disadvantage
    /// </summary>
    private int RollD20WithAdvantage(bool hasAdvantage, bool hasDisadvantage)
    {
        var roll1 = RollD20();
        
        // If both advantage and disadvantage, they cancel out
        if (hasAdvantage == hasDisadvantage)
            return roll1;
        
        var roll2 = RollD20();
        
        if (hasAdvantage)
            return Math.Max(roll1, roll2);
        else
            return Math.Min(roll1, roll2);
    }
    
    /// <summary>
    /// Rolls a single d20
    /// </summary>
    private int RollD20()
    {
        return _random.Next(1, 21);
    }
    
    /// <summary>
    /// Determines if a status effect should be removed (duration check)
    /// </summary>
    public void ProcessStatusEffects(Combatant combatant)
    {
        var expiredEffects = combatant.ActiveEffects
            .Where(e => e.Duration <= 0)
            .ToList();
        
        foreach (var effect in expiredEffects)
        {
            combatant.ActiveEffects.Remove(effect);
        }
        
        // Decrease duration on remaining effects
        foreach (var effect in combatant.ActiveEffects)
        {
            effect.Duration--;
        }
    }
}

/// <summary>
/// Represents the result of an attack roll
/// </summary>
public class AttackResult
{
    public int Roll { get; set; }
    public int Total { get; set; }
    public bool IsHit { get; set; }
    public bool IsCritical { get; set; }
    public bool IsCriticalMiss { get; set; }
}
