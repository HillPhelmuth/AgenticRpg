using System;
using System.Collections.Generic;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Agents.Tools;

public partial class CharacterManagerTools
{
    /// <summary>
    /// Determines if a class is considered a spellcaster.
    /// </summary>
    private static bool IsSpellcasterClass(CharacterClass characterClass)
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
    /// Determines whether a level grants new spells for the specified class.
    /// </summary>
    private static bool IsSpellGrantLevel(CharacterClass characterClass, int level)
    {
        return characterClass switch
        {
            CharacterClass.Wizard => level % 2 == 0,
            CharacterClass.Cleric => level % 2 == 0,
            CharacterClass.WarMage => level % 3 == 0,
            CharacterClass.Paladin => level % 3 == 0,
            _ => false
        };
    }

    /// <summary>
    /// Gets the maximum spell level available to a character at the specified level.
    /// </summary>
    private static int GetMaxSpellLevelForCharacterLevel(int characterLevel)
    {
        var maxSpellLevel = (characterLevel + 1) / 2;
        return Math.Clamp(maxSpellLevel, 1, 9);
    }

    /// <summary>
    /// Returns a list of spells available for the class and spell level.
    /// </summary>
    private static List<Spell> GetSpellsForClass(CharacterClass characterClass, int maxSpellLevel)
    {
        var allSpells = characterClass switch
        {
            CharacterClass.Wizard or CharacterClass.WarMage => Spell.GetAllWizardSpellsFromFile(),
            CharacterClass.Cleric or CharacterClass.Paladin => Spell.GetAllClericSpellsFromFile(),
            _ => []
        };

        var filteredSpells = allSpells
            .Where(spell => spell.Level <= maxSpellLevel)
            .OrderBy(spell => spell.Level)
            .ThenBy(spell => spell.Name)
            .ToList();

        return filteredSpells;
    }
}
