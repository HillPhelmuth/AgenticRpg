using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.State;
using AgenticRpg.DiceRoller.Models;

namespace AgenticRpg.Core.Agents.Tools;

/// <summary>
/// Tools for guiding players through character level-up process
/// </summary>
public class CharacterLevelUpTools(
    IGameStateManager gameStateManager,
    ICharacterRepository characterRepository)
{
    /// <summary>
    /// Calculates new HP, MP, and proficiency bonus when character levels up
    /// </summary>
    [Description("Calculates the HP increase, MP increase, and new proficiency bonus for a character leveling up.")]
    public async Task<string> CalculateNewStats(
        [Description("The character's unique ID")] string characterId,
        [Description("The new level the character is advancing to")] int newLevel,
        [Description("The campaign ID")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new CalculateNewStatsResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null)
            {
                return JsonSerializer.Serialize(new CalculateNewStatsResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found in campaign."
                });
            }

            if (newLevel <= character.Level)
            {
                return JsonSerializer.Serialize(new CalculateNewStatsResult
                {
                    Success = false,
                    Error = $"New level {newLevel} must be greater than current level {character.Level}."
                });
            }

            var previousHP = character.MaxHP;
            var previousMP = character.MaxMP;

            var hpIncrease = character.Class switch
            {
                CharacterClass.Warrior => 10,
                CharacterClass.Wizard => 6,
                CharacterClass.Cleric => 8,
                CharacterClass.Rogue => 8,
                CharacterClass.Paladin => 10,
                CharacterClass.WarMage => 8,
                _ => 8
            };

            hpIncrease += character.GetAttributeModifier(AttributeType.Vitality);
            var newHP = previousHP + hpIncrease;

            var mpIncrease = 0;
            var newMP = previousMP;

            var spellcasterClasses = new[] { CharacterClass.Wizard, CharacterClass.Cleric, CharacterClass.WarMage, CharacterClass.Paladin };
            if (spellcasterClasses.Contains(character.Class))
            {
                mpIncrease = 5 + character.GetAttributeModifier(AttributeType.Wits);
                newMP = previousMP + mpIncrease;
            }

            var proficiencyBonus = ((newLevel - 1) / 4) + 2;

            var result = new CalculateNewStatsResult
            {
                Success = true,
                NewLevel = newLevel,
                PreviousHP = previousHP,
                NewHP = newHP,
                HPIncrease = hpIncrease,
                PreviousMP = previousMP,
                NewMP = newMP,
                MPIncrease = mpIncrease,
                NewProficiencyBonus = proficiencyBonus,
                Message = $"{character.Name} advances to level {newLevel}! HP: +{hpIncrease} ({newHP}), MP: +{mpIncrease} ({newMP}), Prof: +{proficiencyBonus}."
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new CalculateNewStatsResult
            {
                Success = false,
                Error = $"Error calculating new stats: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Gets list of class abilities available at a specific level
    /// </summary>
    [Description("Returns a list of class-specific abilities that become available at the specified level.")]
    public async Task<string> GetAvailableAbilities(
        [Description("The character's unique ID")] string characterId,
        [Description("The character's class")] CharacterClass characterClass,
        [Description("The level to get abilities for")] int newLevel,
        [Description("The campaign ID")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new GetAvailableAbilitiesResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null)
            {
                return JsonSerializer.Serialize(new GetAvailableAbilitiesResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            var abilities = GetClassAbilitiesForLevel(characterClass, newLevel);

            var result = new GetAvailableAbilitiesResult
            {
                Success = true,
                Level = newLevel,
                ClassName = characterClass.ToString(),
                AvailableAbilities = abilities,
                Message = $"Found {abilities.Count} abilities available for {characterClass} at level {newLevel}."
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new GetAvailableAbilitiesResult
            {
                Success = false,
                Error = $"Error getting available abilities: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Adds a selected ability to the character's ability list
    /// </summary>
    [Description("Adds the chosen class ability to the character's ability list.")]
    public async Task<string> SelectNewAbility(
        [Description("The character's unique ID")] string characterId,
        [Description("The name of the ability to add")] string abilityName,
        [Description("The level at which this ability is gained")] int newLevel,
        [Description("The campaign ID")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new SelectNewAbilityResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null)
            {
                return JsonSerializer.Serialize(new SelectNewAbilityResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            var abilitiesItem = character.Inventory.FirstOrDefault(i => i.ItemType == ItemType.Ability);
            if (abilitiesItem == null)
            {
                abilitiesItem = new InventoryItem
                {
                    Name = "Class Abilities",
                    ItemType = ItemType.Ability,
                    Properties = []
                };
                character.Inventory.Add(abilitiesItem);
            }

            abilitiesItem.Properties[$"Level{newLevel}_{abilityName}"] = JsonSerializer.Serialize(new
            {
                Name = abilityName,
                Level = newLevel,
                AcquiredAt = DateTime.UtcNow
            });

            character.UpdatedAt = DateTime.UtcNow;

            var result = new SelectNewAbilityResult
            {
                Success = true,
                AbilityName = abilityName,
                Level = newLevel,
                Description = $"Successfully added {abilityName} to {character.Name}'s abilities.",
                Message = $"{character.Name} learned {abilityName} at level {newLevel}!"
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new SelectNewAbilityResult
            {
                Success = false,
                Error = $"Error selecting ability: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Allocates skill points to a character's skills during level up
    /// </summary>
    [Description("Increases a character's skill ranks by allocating available skill points.")]
    public async Task<string> AllocateSkillPoints(
        [Description("The unique ID of the character allocating skill points.")] string characterId,
        [Description("The name of the skill to increase (e.g., Athletics, Stealth, Perception, Arcana).")] string skillName,
        [Description("The number of skill points to add to this skill. Typically 1-4 points per level.")] int pointsToAdd,
        [Description("The new level at which these skill points are being allocated.")] int newLevel,
        [Description("The unique ID of the campaign this character belongs to.")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new AllocateSkillPointsResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null)
            {
                return JsonSerializer.Serialize(new AllocateSkillPointsResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            var previousRank = character.Skills.TryGetValue(skillName, out var value) ? value : 0;
            var newRank = previousRank + pointsToAdd;
            character.Skills[skillName] = newRank;
            character.UpdatedAt = DateTime.UtcNow;

            var skillPointsPerLevel = 4 + character.GetAttributeModifier(AttributeType.Wits);
            var remainingPoints = Math.Max(0, skillPointsPerLevel - pointsToAdd);

            var result = new AllocateSkillPointsResult
            {
                Success = true,
                SkillName = skillName,
                PreviousRank = previousRank,
                NewRank = newRank,
                PointsAllocated = pointsToAdd,
                RemainingSkillPoints = remainingPoints,
                Message = $"{character.Name}'s {skillName} increased from {previousRank} to {newRank}."
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new AllocateSkillPointsResult
            {
                Success = false,
                Error = $"Error allocating skill points: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Gets list of spells available for spellcaster classes at a specific level
    /// </summary>
    [Description("Returns spells that can be learned at the specified level for spellcaster classes.")]
    public async Task<string> GetAvailableSpells(
        [Description("The unique ID of the character learning spells.")] string characterId,
        [Description("The spellcasting class. Valid classes: Wizard, Cleric, Paladin, WarMage, Druid, Sorcerer, Warlock.")] CharacterClass spellcasterClass,
        [Description("The character level to determine available spell levels and choices.")] int newLevel,
        [Description("The unique ID of the campaign this character belongs to.")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new GetAvailableSpellsResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null)
            {
                return JsonSerializer.Serialize(new GetAvailableSpellsResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            var maxSpellLevel = GetMaxSpellLevelForCharacterLevel(newLevel);
            var spells = GetSpellsForClass(spellcasterClass, maxSpellLevel);

            var result = new GetAvailableSpellsResult
            {
                Success = true,
                Level = newLevel,
                SpellcasterClass = spellcasterClass.ToString(),
                MaxSpellLevel = maxSpellLevel,
                AvailableSpells = spells,
                Message = $"Found {spells.Count} spells available for {spellcasterClass} at level {newLevel}."
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new GetAvailableSpellsResult
            {
                Success = false,
                Error = $"Error getting available spells: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Adds a selected spell to the character's known spells
    /// </summary>
    [Description("Adds the chosen spell to the character's known spells list.")]
    public async Task<string> SelectNewSpell(
        [Description("The unique ID of the character learning the spell.")] string characterId,
        [Description("The name of the spell to add to the character's spell list (e.g., 'Fireball', 'Healing Word', 'Shield').")] string spellName,
        [Description("The level of the spell (0 for cantrips, 1-9 for leveled spells). Must be accessible at the character's level.")] int spellLevel,
        [Description("The unique ID of the campaign this character belongs to.")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new SelectNewSpellResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null)
            {
                return JsonSerializer.Serialize(new SelectNewSpellResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            if (character.KnownSpells.Any(s => s.Name == spellName))
            {
                return JsonSerializer.Serialize(new SelectNewSpellResult
                {
                    Success = false,
                    Error = $"{character.Name} already knows the spell {spellName}."
                });
            }

            var spell = new Spell
            {
                Name = spellName,
                Level = spellLevel,
                Description = $"A level {spellLevel} spell",
                School = "Evocation",
                CastingTime = "1 action",
                Range = "30 feet",
                Duration = "Instantaneous",
                Components = ["V", "S"]
            };

            character.KnownSpells.Add(spell);
            character.UpdatedAt = DateTime.UtcNow;

            var result = new SelectNewSpellResult
            {
                Success = true,
                SpellName = spellName,
                SpellLevel = spellLevel,
                Description = spell.Description,
                Message = $"{character.Name} learned the spell {spellName} (level {spellLevel})!"
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new SelectNewSpellResult
            {
                Success = false,
                Error = $"Error selecting spell: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Finalizes the level up process, applying all changes and saving the character
    /// </summary>
    [Description("Completes the level up process by applying HP/MP increases, updating the character level, and saving all changes.")]
    public async Task<string> FinalizeLevel(
        [Description("The unique ID of the character completing the level up.")] string characterId,
        [Description("The new level to set for the character (must be higher than current level).")] int newLevel,
        [Description("The unique ID of the campaign this character belongs to.")] string campaignId)
    {
        try
        {
            var gameState = await gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                return JsonSerializer.Serialize(new FinalizeLevelResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null)
            {
                return JsonSerializer.Serialize(new FinalizeLevelResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            var previousLevel = character.Level;
            character.Level = newLevel;
            character.UpdatedAt = DateTime.UtcNow;

            var nextLevelXP = newLevel * 1000;

            var newAbilities = new List<string>();
            var newSpells = new List<string>();
            var skillIncreases = new Dictionary<string, int>();

            var abilitiesItem = character.Inventory.FirstOrDefault(i => i.ItemType == ItemType.Ability);
            if (abilitiesItem != null)
            {
                foreach (var prop in abilitiesItem.Properties)
                {
                    if (prop.Key.StartsWith($"Level{newLevel}_"))
                    {
                        newAbilities.Add(prop.Key.Substring($"Level{newLevel}_".Length));
                    }
                }
            }

            foreach (var spell in character.KnownSpells)
            {
                newSpells.Add($"{spell.Name} (level {spell.Level})");
            }

            foreach (var skill in character.Skills)
            {
                skillIncreases[skill.Key] = skill.Value;
            }

            await characterRepository.UpdateAsync(character);

            var result = new FinalizeLevelResult
            {
                Success = true,
                CharacterId = character.Id,
                CharacterName = character.Name,
                PreviousLevel = previousLevel,
                NewLevel = newLevel,
                CurrentXP = character.Experience,
                NextLevelXP = nextLevelXP,
                NewAbilities = newAbilities,
                NewSpells = newSpells,
                SkillIncreases = skillIncreases,
                Message = $"Congratulations! {character.Name} is now level {newLevel}!"
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new FinalizeLevelResult
            {
                Success = false,
                Error = $"Error finalizing level up: {ex.Message}"
            });
        }
    }

    #region Helper Methods

    private List<AbilityOption> GetClassAbilitiesForLevel(CharacterClass characterClass, int level)
    {
        var abilities = new List<AbilityOption>();

        switch (characterClass)
        {
            case CharacterClass.Warrior:
                if (level == 2) abilities.Add(new AbilityOption { Name = "Action Surge", Description = "Take an additional action on your turn", Type = "Active" });
                if (level == 3) abilities.Add(new AbilityOption { Name = "Martial Archetype", Description = "Choose your fighter specialization", Type = "Passive" });
                if (level == 5) abilities.Add(new AbilityOption { Name = "Extra Attack", Description = "Attack twice when taking the Attack action", Type = "Passive" });
                break;

            case CharacterClass.Wizard:
                if (level == 2) abilities.Add(new AbilityOption { Name = "Arcane Recovery", Description = "Recover spell slots during short rest", Type = "Active" });
                if (level == 3) abilities.Add(new AbilityOption { Name = "Arcane Tradition", Description = "Choose your wizard school", Type = "Passive" });
                if (level == 5) abilities.Add(new AbilityOption { Name = "Spell Mastery", Description = "Cast certain low-level spells at will", Type = "Passive" });
                break;

            case CharacterClass.Rogue:
                if (level == 2) abilities.Add(new AbilityOption { Name = "Cunning Action", Description = "Dash, Disengage, or Hide as bonus action", Type = "Active" });
                if (level == 3) abilities.Add(new AbilityOption { Name = "Roguish Archetype", Description = "Choose your rogue specialization", Type = "Passive" });
                if (level == 5) abilities.Add(new AbilityOption { Name = "Uncanny Dodge", Description = "Use reaction to halve damage", Type = "Reaction" });
                break;

            default:
                abilities.Add(new AbilityOption { Name = "Class Feature", Description = $"A feature gained at level {level}", Type = "Passive" });
                break;
        }

        return abilities;
    }

    private int GetMaxSpellLevelForCharacterLevel(int characterLevel)
    {
        return characterLevel switch
        {
            1 => 1,
            2 => 1,
            3 => 2,
            4 => 2,
            5 => 3,
            6 => 3,
            7 => 4,
            8 => 4,
            9 => 5,
            10 => 5,
            11 => 6,
            12 => 6,
            13 => 7,
            14 => 7,
            15 => 8,
            16 => 8,
            17 => 9,
            _ => 9
        };
    }

    private List<SpellOption> GetSpellsForClass(CharacterClass characterClass, int maxSpellLevel)
    {
        var spells = new List<SpellOption>
        {
            new SpellOption
            {
                Name = "Fire Bolt",
                Level = 0,
                School = "Evocation",
                Description = "Hurl a mote of fire at a creature or object",
                CastingTime = "1 action",
                Range = "120 feet",
                RequiresConcentration = false
            }
        };

        if (maxSpellLevel >= 1)
        {
            spells.Add(new SpellOption
            {
                Name = "Magic Missile",
                Level = 1,
                School = "Evocation",
                Description = "Create darts of magical force that automatically hit",
                CastingTime = "1 action",
                Range = "120 feet",
                RequiresConcentration = false
            });

            spells.Add(new SpellOption
            {
                Name = "Shield",
                Level = 1,
                School = "Abjuration",
                Description = "Invisible barrier protects you, adding +5 to AC",
                CastingTime = "1 reaction",
                Range = "Self",
                RequiresConcentration = false
            });
        }

        if (maxSpellLevel >= 2)
        {
            spells.Add(new SpellOption
            {
                Name = "Scorching Ray",
                Level = 2,
                School = "Evocation",
                Description = "Create rays of fire to hurl at targets",
                CastingTime = "1 action",
                Range = "120 feet",
                RequiresConcentration = false
            });
        }

        if (maxSpellLevel >= 3)
        {
            spells.Add(new SpellOption
            {
                Name = "Fireball",
                Level = 3,
                School = "Evocation",
                Description = "A bright streak flashes to a point you choose and explodes",
                CastingTime = "1 action",
                Range = "150 feet",
                RequiresConcentration = false
            });
        }

        return spells;
    }

    #endregion
}
