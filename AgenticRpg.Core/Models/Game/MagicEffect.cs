using System.ComponentModel;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Models.Game;

public class MagicEffect
{
    [Description("Type of magical effect")]
    public MagicEffectType EffectType { get; set; } = MagicEffectType.Utility;
    [Description("Attribute affected by the magical buff or debuff effect (if applicable). This is required if `EffectType` is 'Buff', 'Debuff', or 'Utility'.")]
    public AttributeType? AffectedAttribute { get; set; }
    [Description("Magnitude of the magical effect")]
    public int EffectMagnitude { get; set; } = 0;
    [Description("Duration of the magical effect in turns")]
    public int EffectDuration { get; set; } = 0;
    [Description("Dice expression for variable effects (e.g., 2d6). Applies to damage and healing effects. Required of `EffectType` is 'Damage' or 'Healing'.")]
    public string? EffectDice { get; set; }
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
