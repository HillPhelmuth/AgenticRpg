using System.ComponentModel;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Models.Game;

public class MagicEffect
{
    [Description("Type of magical effect")]
    public MagicEffectType EffectType { get; set; } = MagicEffectType.Utility;
    [Description("Attribute affected by the magical buff or debuff effect (if applicable). This is required if `EffectType` is 'Buff', 'Debuff', or 'Utility'.")]
    public AttributeType? AffectedAttribute { get; set; }
    [Description("Area of effect for the magical effect")]
    public MagicEffectAreaOfEffect AreaOfEffect { get; set; } = MagicEffectAreaOfEffect.SingleTarget;
    [Description("Magnitude of the magical effect")]
    public int EffectMagnitude { get; set; } = 0;
    [Description("Duration of the magical effect in turns")]
    public int EffectDuration { get; set; } = 0;
    [Description("Dice expression for variable effects (e.g., 2d6). Applies to damage and healing effects. Required of `EffectType` is 'Damage' or 'Healing'.")]
    public string? EffectDice { get; set; }
    [Description("Level-based effect dice slots (e.g., for spells that scale with level). If spells scale by slot, assume a 'slot' = 2 levels.")]
    public Dictionary<int, string> LevelBasedEffectDiceSlots { get; set; } = [];

    public string? GetLevelEffectDice(int characterLevel)
    {
        if (LevelBasedEffectDiceSlots.Count == 0) return EffectDice;
        // Get the highest level slot that is less than or equal to characterLevel
        var applicableLevels = LevelBasedEffectDiceSlots.Keys.Where(level => level <= characterLevel).ToList();
        if (applicableLevels.Count == 0) return EffectDice;
        var highestApplicableLevel = applicableLevels.Max();
        return LevelBasedEffectDiceSlots[highestApplicableLevel];
    }
    [Description("Damage type for saving throw")]
    public DamageType? SavingThrowDamageType { get; set; }

    public override string ToString()
    {
        return EffectType switch
        {
            MagicEffectType.Damage or MagicEffectType.Healing => $"{EffectType} - ({EffectDice})",
            MagicEffectType.Buff or MagicEffectType.Debuff =>
                $"{EffectType} - {EffectMagnitude} to {AffectedAttribute} ({EffectDuration} turns)",
            _ => $"{EffectType} - {EffectMagnitude} ({EffectDuration} turns)"
        };
    }
}
