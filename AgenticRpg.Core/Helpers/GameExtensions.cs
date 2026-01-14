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

        public Combatant AsCombatant()
        {
            return new Combatant
            {
                Id = character.Id,
                Name = character.Name,
                ArmorClass = character.ArmorClass,
                MaxHP = character.MaxHP,
                CurrentHP = character.MaxHP,
                Initiative = character.Initiative,
                CharacterId = character.Id,
                IsEnemy = false,
                Speed = character.Speed,
                EquippedWeapon = character.Equipment.MainHand,
                
            };
        }
    }
}