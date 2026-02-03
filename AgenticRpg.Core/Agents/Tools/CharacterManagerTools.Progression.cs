using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Agents.Tools;

public partial class CharacterManagerTools
{
    
    [Description("Increases a character's skill ranks by allocating available skill points (2 per level).")]
    public async Task<string> AllocateSkillPoints(
        [Description("The unique ID of the character allocating skill points.")] string characterId,
        [Description("The name of the skill to increase (e.g., Athletics, Stealth, Perception, Arcana).")]
        string skillName,
        [Description("The number of skill points to add to this skill (1-2 points per level).")]
        int pointsToAdd,
        [Description("The new level at which these skill points are being allocated.")] int newLevel,
        [Description("The unique ID of the campaign this character belongs to.")] string campaignId)
    {
        try
        {
            var gameState = await _gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState is null)
            {
                return JsonSerializer.Serialize(new AllocateSkillPointsResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId || c.Name.Equals(characterId, StringComparison.OrdinalIgnoreCase));
            if (character is null)
            {
                return JsonSerializer.Serialize(new AllocateSkillPointsResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            if (newLevel <= character.Level)
            {
                return JsonSerializer.Serialize(new AllocateSkillPointsResult
                {
                    Success = false,
                    Error = $"New level {newLevel} must be greater than current level {character.Level}."
                });
            }

            if (pointsToAdd is < 1 or > 2)
            {
                return JsonSerializer.Serialize(new AllocateSkillPointsResult
                {
                    Success = false,
                    Error = "Skill points per level are limited to 2."
                });
            }

            var skillDefinition = Skill.GetAllSkillsFromFile()
                .FirstOrDefault(skill => skill.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase));

            if (skillDefinition is null)
            {
                return JsonSerializer.Serialize(new AllocateSkillPointsResult
                {
                    Success = false,
                    Error = $"Skill '{skillName}' was not found in the skill list."
                });
            }

            var canonicalName = skillDefinition.Name;
            var previousRank = character.Skills.TryGetValue(canonicalName, out var value) ? value : 0;
            var newRank = previousRank + pointsToAdd;
            // Reason: Adding points to an unknown skill unlocks it at rank 1+, aligning skills with abilities.
            character.Skills[canonicalName] = newRank;
            character.UpdatedAt = DateTime.UtcNow;

            await _characterRepository.UpdateAsync(character);

            var remainingPoints = Math.Max(0, 2 - pointsToAdd);

            var result = new AllocateSkillPointsResult
            {
                Success = true,
                SkillName = canonicalName,
                PreviousRank = previousRank,
                NewRank = newRank,
                PointsAllocated = pointsToAdd,
                RemainingSkillPoints = remainingPoints,
                Message = $"{character.Name}'s {canonicalName} increased from {previousRank} to {newRank}."
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

    [Description("Get all the available skills in the game")]
    public Task<string> GetAllSkills()
    {
        var skills = Skill.GetAllSkillsFromFile();
        return Task.FromResult(JsonSerializer.Serialize(skills));
    }
   
    [Description("Returns spells that can be learned at the specified level for spellcaster classes.")]
    public async Task<string> GetAvailableSpells(
        [Description("The unique ID of the character learning spells.")] string characterId,
        [Description("The character level to determine available spell levels and choices.")] int newLevel,
        [Description("The unique ID of the campaign this character belongs to.")] string campaignId)
    {
        try
        {
            var gameState = await _gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState is null)
            {
                return JsonSerializer.Serialize(new GetAvailableSpellsResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character is null)
            {
                return JsonSerializer.Serialize(new GetAvailableSpellsResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }
            

            if (!character.IsSpellcaster())
            {
                return JsonSerializer.Serialize(new GetAvailableSpellsResult
                {
                    Success = false,
                    Error = $"{character.Class} is not a spellcasting class."
                });
            }

            if (!IsSpellGrantLevel(character.Class, newLevel))
            {
                return JsonSerializer.Serialize(new GetAvailableSpellsResult
                {
                    Success = false,
                    Error = $"No new spells are granted to {character.Class} at level {newLevel}."
                });
            }

            var maxSpellLevel = GetMaxSpellLevelForCharacterLevel(newLevel);
            var spells = GetSpellsForClass(character.Class, maxSpellLevel);

            var result = new GetAvailableSpellsResult
            {
                Success = true,
                Level = newLevel,
                SpellcasterClass = character.Class.ToString(),
                MaxSpellLevel = maxSpellLevel,
                AvailableSpells = spells,
                Message = $"Found {spells.Count} spells available for {character.Class} at level {newLevel}."
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

  
    [Description("Adds the chosen spell to the character's known spells list.")]
    public async Task<string> SelectNewSpell(
        [Description("The unique ID of the character learning the spell.")] string characterId,
        [Description("The name of the spell to add to the character's spell list (e.g., 'Fireball', 'Healing Word', 'Shield').")] string spellName,
        [Description("The character level for which the spell is being granted.")] int newLevel,
        [Description("The unique ID of the campaign this character belongs to.")] string campaignId)
    {
        try
        {
            var gameState = await _gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState is null)
            {
                return JsonSerializer.Serialize(new SelectNewSpellResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.GetCharacter(characterId);
            if (character is null)
            {
                return JsonSerializer.Serialize(new SelectNewSpellResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }
            var spellList = GetSpellsForClass(character.Class, GetMaxSpellLevelForCharacterLevel(newLevel));
            var selectedSpell = spellList.FirstOrDefault(s => s.Name.Equals(spellName, StringComparison.OrdinalIgnoreCase));
            if (selectedSpell is null)
            {
                return JsonSerializer.Serialize(new SelectNewSpellResult
                {
                    Success = false,
                    Error = $"Spell '{spellName}' not found for class {character.Class} at level {newLevel}."
                });
            }
            if (!character.IsSpellcaster())
            {
                return JsonSerializer.Serialize(new SelectNewSpellResult
                {
                    Success = false,
                    Error = $"{character.Class} is not a spellcasting class."
                });
            }

            if (newLevel <= character.Level)
            {
                return JsonSerializer.Serialize(new SelectNewSpellResult
                {
                    Success = false,
                    Error = $"New level {newLevel} must be greater than current level {character.Level}."
                });
            }

            if (!IsSpellGrantLevel(character.Class, newLevel))
            {
                return JsonSerializer.Serialize(new SelectNewSpellResult
                {
                    Success = false,
                    Error = $"No new spells are granted to {character.Class} at level {newLevel}."
                });
            }

            var maxSpellLevel = GetMaxSpellLevelForCharacterLevel(newLevel);
            if (selectedSpell.Level is < 0 or > 9 || selectedSpell.Level > maxSpellLevel)
            {
                return JsonSerializer.Serialize(new SelectNewSpellResult
                {
                    Success = false,
                    Error = $"Spell level {selectedSpell.Level} is not available at character level {newLevel}."
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

            var spell = selectedSpell;

            character.KnownSpells.Add(spell);
            character.UpdatedAt = DateTime.UtcNow;
            await _characterRepository.UpdateAsync(character);

            var result = new SelectNewSpellResult
            {
                Success = true,
                SpellName = spellName,
                SpellLevel = spell.Level,
                Description = spell.Description,
                Message = $"{character.Name} learned the spell {spellName} (level {spell.Level})!"
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
}
