using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
namespace AgenticRpg.Core.Rules;

public sealed record WeaponBreakdown(int Attribute, int Proficiency, int Skill, int Attack, int DamageBonus, string Dice)
{
    public string AttackExplanation => $"Attribute {WeaponMechanics.Signed(Attribute)}, proficiency {WeaponMechanics.Signed(Proficiency)}, combat skill {WeaponMechanics.Signed(Skill)}";
    public string DamageExplanation => $"{Dice} {WeaponMechanics.Signed(DamageBonus)}; critical doubles the rolled total";
    public int Damage(int rolled, bool critical) => (critical ? rolled * 2 : rolled) + DamageBonus;
}
public static class WeaponMechanics
{
    public static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();
    public static WeaponBreakdown ForMonster(int might, int agility, WeaponItem weapon)
    {
        var attribute = AttributeCalculator.CalculateModifier(weapon.WeaponType == WeaponType.Ranged ? agility : might);
        return new(attribute, 0, 0, attribute, attribute + weapon.GetDieRollBonus(), $"{weapon.GetDieRollCount()}{weapon.GetDieType()}");
    }
    public static WeaponBreakdown For(Character character, WeaponItem weapon)
    {
        var proficiency = StatsCalculator.CalculateProficiencyBonus(character, weapon);
        var basic = StatsCalculator.CalculateAttackBonus(character, weapon);
        var skill = 0;
        if (character.Skills.TryGetValue("Melee Combat", out var melee) && weapon.WeaponType is WeaponType.Melee or WeaponType.Both) skill = melee;
        else if (character.Skills.TryGetValue("Ranged Combat", out var ranged) && weapon.WeaponType is WeaponType.Ranged or WeaponType.Both) skill = ranged;
        return new(basic - proficiency, proficiency, skill, basic + skill, basic + skill + weapon.GetDieRollBonus(), $"{weapon.GetDieRollCount()}{weapon.GetDieType()}");
    }
}
