using System;
using System.Collections.Generic;
using System.Text;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Helpers;

public static class GameExtensions
{
    extension(Character character)
    {
        public bool IsSpellcaster()
        {
            return character.Class is CharacterClass.Wizard or CharacterClass.Cleric or CharacterClass.Paladin or CharacterClass.WarMage;
        }
    }
}