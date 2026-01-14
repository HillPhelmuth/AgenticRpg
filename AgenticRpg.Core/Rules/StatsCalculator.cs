using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Rules;

/// <summary>
/// Calculates derived character statistics based on attributes, class, and level
/// </summary>
public class StatsCalculator
{
    /// <summary>
    /// Calculates maximum hit points for a character
    /// Formula: Base HP + (Vitality modifier * level)
    /// </summary>
    /// <param name="character">The character</param>
    /// <returns>Maximum HP value</returns>
    public static int CalculateMaxHP(Character character)
    {
        var baseHP = GetClassBaseHP(character.Class);
        var vitalityModifier = character.GetAttributeModifier(AttributeType.Vitality);
        
        var hp = (baseHP + vitalityModifier) * character.Level;
        
        return hp;
    }
    
    /// <summary>
    /// Calculates maximum magic/mana points for a character
    /// Only applies to spellcasting classes
    /// </summary>
    public static int CalculateMaxMP(Character character)
    {
        if (!IsSpellcaster(character.Class))
            return 0;
        
        var baseMP = GetClassBaseMP(character.Class);
        var witsModifier = character.GetAttributeModifier(AttributeType.Wits);
        
        // MP scales with level and Wits modifier
        var mp = (baseMP + witsModifier) * character.Level;
        
        return Math.Max(0, mp);
    }
    
    /// <summary>
    /// Calculates armor class for a character
    /// Formula: 10 + Agility modifier + armor bonus + shield bonus
    /// </summary>
    public static int CalculateArmorClass(Character character)
    {
        var baseAC = 10;
        var agilityModifier = character.GetAttributeModifier(AttributeType.Agility);
        
        var armorBonus = 0;
        var wornArmor = character.Equipment.Armor;
        if (wornArmor != null)
        {
            if (!wornArmor.AllowAgilityBonus)
            {
                agilityModifier = 0;
            }

            armorBonus = GetArmorBonus(wornArmor);
        }
        
        var shieldBonus = 0;
        var offHand = character.Equipment.OffHand;
        if (offHand is { ItemType: ItemType.Shield })
        {
            shieldBonus = offHand is ArmorItem shieldArmor
                ? GetArmorBonus(shieldArmor)
                : GetArmorBonus(offHand);
        }
        
        return baseAC + agilityModifier + armorBonus + shieldBonus + CalculateMagicACBonuses(character);
    }

    private static int CalculateMagicACBonuses(Character character)
    {
        var magicBonus = 0;

        // Add any magic item bonuses
        foreach (var item in character.Equipment.Items.Values)
        {
            if (item is MagicItem magicItem && magicItem.Effects.Any(x => x is { AffectedAttribute: AttributeType.AC, EffectDuration: 0 }))
            {
                magicBonus += magicItem.Effects
                    .Where(x => x is { AffectedAttribute: AttributeType.AC, EffectDuration: 0 })
                    .Sum(x => x.EffectMagnitude);
            }
        }
        if (character.TemporaryAttributeBuffs.Any(x => x is { AffectedAttribute: AttributeType.AC, EffectDuration: 0 }))
        {
            magicBonus += character.TemporaryAttributeBuffs
                .Where(x => x is { AffectedAttribute: AttributeType.AC, EffectDuration: 0 })
                .Sum(x => x.EffectMagnitude);
        }

        return magicBonus;
    }
    /// <summary>
    /// Calculates initiative modifier
    /// Formula: Agility modifier + class bonus
    /// </summary>
    public static int CalculateInitiative(Character character)
    {
        var agilityModifier = character.GetAttributeModifier(AttributeType.Agility);
        var classBonus = GetClassInitiativeBonus(character.Class);
        
        return agilityModifier + classBonus;
    }

    public static int CalculateAttackBonus(Character character, WeaponItem weapon)
    {
        var proficiencyBonus = CalculateProficiencyBonus(character, weapon);

        var isFinesse = weapon.WeaponProperties.Any(p => string.Equals(p, "Finesse", StringComparison.OrdinalIgnoreCase))
                        || weapon.Properties.ContainsKey("Finesse");

        var isRanged = weapon.WeaponType == WeaponType.Ranged
                       || weapon.WeaponType == WeaponType.Both
                       || weapon.WeaponProperties.Any(p => string.Equals(p, "Ranged", StringComparison.OrdinalIgnoreCase))
                       || weapon.Properties.ContainsKey("Ranged");

        int attributeModifier;
        if (isFinesse)
        {
            attributeModifier = Math.Max(
                character.GetAttributeModifier(AttributeType.Might),
                character.GetAttributeModifier(AttributeType.Agility)
            );
        }
        else if (isRanged)
        {
            attributeModifier = character.GetAttributeModifier(AttributeType.Agility);
        }
        else
        {
            attributeModifier = character.GetAttributeModifier(AttributeType.Might);
        }

        return proficiencyBonus + attributeModifier;
    }
    
    /// <summary>
    /// Calculates proficiency bonus based on level
    /// </summary>
    public static int CalculateProficiencyBonus(Character character, WeaponItem weapon)
    {
        var skills = character.Skills;
        
        if (skills.TryGetValue("Melee Combat", out var meleeSkill))
        {
            if (weapon.WeaponType == WeaponType.Melee)
            {
                return  meleeSkill;
            }
        }
        else if (skills.TryGetValue("Ranged Combat", out var rangedSkill))
        {
            if (weapon.WeaponType == WeaponType.Ranged)
            {
                return rangedSkill;
            }
        }

        return 0;
    }
    
    /// <summary>
    /// Gets base HP for a class at level 1
    /// </summary>
    private static int GetClassBaseHP(CharacterClass characterClass)
    {
        return characterClass switch
        {
            CharacterClass.Warrior => 10,
            CharacterClass.Paladin => 10,
            CharacterClass.Rogue => 8,
            CharacterClass.Cleric => 8,
            CharacterClass.Wizard => 6,
            CharacterClass.WarMage => 8,
            _ => 8
        };
    }

    /// <summary>
    /// Gets base MP for spellcasting classes
    /// </summary>
    private static int GetClassBaseMP(CharacterClass characterClass)
    {
        return characterClass switch
        {
            CharacterClass.Wizard => 10,
            CharacterClass.Cleric => 8,
            CharacterClass.WarMage => 8,
            CharacterClass.Paladin => 4,
            _ => 0
        };
    }
    
    /// <summary>
    /// Checks if a class is a spellcaster
    /// </summary>
    private static bool IsSpellcaster(CharacterClass characterClass)
    {
        return characterClass switch
        {
            CharacterClass.Wizard => true,
            CharacterClass.Cleric => true,
            CharacterClass.WarMage => true,
            CharacterClass.Paladin => true,
            _ => false
        };
    }
    
    /// <summary>
    /// Gets initiative bonus for a class
    /// </summary>
    private static int GetClassInitiativeBonus(CharacterClass characterClass)
    {
        return characterClass switch
        {
            CharacterClass.Rogue => 2,
            _ => 0
        };
    }
    
    /// <summary>
    /// Gets armor bonus from armor item
    /// </summary>
    private static int GetArmorBonus(ArmorItem armor)
    {
        if (armor.ArmorClassBonus != 0)
        {
            return armor.ArmorClassBonus;
        }

        if (armor.Properties.TryGetValue("ArmorBonus", out var bonus))
        {
            if (bonus is int intBonus)
                return intBonus;
        }

        // Back-compat: some items stored a full AC value under "ArmorClass".
        if (armor.Properties.TryGetValue("ArmorClass", out var armorClass) && armorClass is int ac)
        {
            return ac > 10 ? ac - 10 : ac;
        }
        
        // Default armor bonuses by type
        return armor.ItemType switch
        {
            ItemType.Armor => 4,
            ItemType.Shield => 2,
            _ => 0
        };
    }

    private static int GetArmorBonus(InventoryItem armor)
    {
        if (armor is ArmorItem armorItem)
        {
            return GetArmorBonus(armorItem);
        }

        if (armor.Properties.TryGetValue("ArmorBonus", out var bonus))
        {
            if (bonus is int intBonus)
                return intBonus;
        }

        return armor.ItemType switch
        {
            ItemType.Armor => 4,
            ItemType.Shield => 2,
            _ => 0
        };
    }
}
