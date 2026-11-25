using System;
using System.Collections.Generic;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Agents.Tools;

public partial class CharacterManagerTools
{
    /// <summary>
    /// Determines whether a level grants new spells for the specified class.
    /// </summary>
    private static bool IsSpellGrantLevel(CharacterClass characterClass, int level) =>
        Rules.CharacterProgression.IsSpellGrantLevel(characterClass, level);

    private static int GetMaxSpellLevelForCharacterLevel(int level) =>
        Rules.CharacterProgression.GetMaxSpellLevelForCharacterLevel(level);

    private static List<Spell> GetSpellsForClass(CharacterClass characterClass, int maxSpellLevel) =>
        Rules.CharacterProgression.GetSpellsForClass(characterClass, maxSpellLevel);
}
