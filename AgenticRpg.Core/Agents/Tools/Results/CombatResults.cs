namespace AgenticRpg.Core.Agents.Tools.Results;

// CalculateAttackRoll result type
public class AttackRollResult
{
    public bool Success { get; set; }
    public string? AttackerName { get; set; }
    public string? TargetName { get; set; }
    public string? WeaponName { get; set; }
    public int AttackRoll { get; set; }
    public int AttackModifier { get; set; }
    public int TotalAttack { get; set; }
    public int TargetAC { get; set; }
    public bool IsHit { get; set; }
    public bool IsCritical { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

// ApplyDamage result type
public class ApplyDamageResult
{
    public bool Success { get; set; }
    public string? TargetName { get; set; }
    public int DamageAmount { get; set; }
    public string? DamageType { get; set; }
    public int TempHPLost { get; set; }
    public int HPLost { get; set; }
    public int CurrentHP { get; set; }
    public int MaxHP { get; set; }
    public int RemainingTempHP { get; set; }
    public bool IsDead { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

// ProcessSavingThrow result type
public class SavingThrowResult
{
    public bool Success { get; set; }
    public string? CharacterName { get; set; }
    public string? SaveType { get; set; }
    public int SaveRoll { get; set; }
    public int SaveModifier { get; set; }
    public int TotalSave { get; set; }
    public int DifficultyClass { get; set; }
    public bool IsSuccess { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

// DetermineInitiative result types
public class InitiativeResult
{
    public bool Success { get; set; }
    public required List<InitiativeEntry> InitiativeOrder { get; set; }
    public string? CurrentTurn { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

public class InitiativeEntry
{
    public required string CombatantId { get; set; }
    public required string Name { get; set; }
    public int Initiative { get; set; }
}

// ResolveSpecialAbility result type
public class SpecialAbilityResult
{
    public bool Success { get; set; }
    public string? UserName { get; set; }
    public string? AbilityName { get; set; }
    public string[]? Targets { get; set; }
    public required string Message { get; set; }
    public string? Error { get; set; }
}

// EndCombat result type
public class EndCombatResult
{
    public bool Success { get; set; }
    public string? Victor { get; set; }
    public CombatRewards? Rewards { get; set; }
    public required string Message { get; set; }
    public string? ActiveAgent { get; set; }
    public string? Error { get; set; }
}

// Unified combat result for attacks (weapons or spells)
public class CombatAttackResult
{
    public bool Success { get; set; }
    public string? AttackerName { get; set; }
    public string? TargetName { get; set; }

    public string? SourceName { get; set; }
    public string? SourceType { get; set; }

    public int AttackRoll { get; set; }
    public int AttackModifier { get; set; }
    public int TotalAttack { get; set; }
    public int TargetAC { get; set; }

    public bool IsHit { get; set; }
    public bool IsCritical { get; set; }

    public int Damage { get; set; }
    public int TargetHP { get; set; }
    public int TargetMaxHP { get; set; }
    public bool TargetDefeated { get; set; }

    public required string Message { get; set; }
    public string? Error { get; set; }
}
