using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Rules;

public static class CharacterProgression
{
    public static bool IsSpellGrantLevel(CharacterClass characterClass, int level) => characterClass switch
    {
        CharacterClass.Wizard or CharacterClass.Cleric => level % 2 == 0,
        CharacterClass.WarMage or CharacterClass.Paladin => level % 3 == 0,
        _ => false
    };

    public static int GetMaxSpellLevelForCharacterLevel(int level) => Math.Clamp((level + 1) / 2, 1, 9);

    public static List<Spell> GetSpellsForClass(CharacterClass characterClass, int maxSpellLevel) =>
        (characterClass switch
        {
            CharacterClass.Wizard or CharacterClass.WarMage => Spell.GetAllWizardSpellsFromFile(),
            CharacterClass.Cleric or CharacterClass.Paladin => Spell.GetAllClericSpellsFromFile(),
            _ => new List<Spell>()
        }).Where(s => s.Level >= 0 && s.Level <= maxSpellLevel)
        .OrderBy(s => s.Level).ThenBy(s => s.Name).ToList();
}
