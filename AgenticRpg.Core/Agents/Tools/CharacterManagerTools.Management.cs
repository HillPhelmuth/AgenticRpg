using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Agents.Tools;

public partial class CharacterManagerTools
{
    /// <summary>
    /// Updates core character stats and attributes without touching derived MaxHP/MaxMP values.
    /// </summary>
    [Description("Updates core character stats (level, experience, current HP/MP, gold, speed, initiative, background, portrait, attributes).")]
    public async Task<string> UpdateCharacterStats(
        [Description("The character's unique ID")] string characterId,
        [Description("The campaign ID")] string campaignId,
        [Description("Optional level override (must be >= 1).")]
        int? level = null,
        [Description("Optional experience override (>= 0).")]
        int? experience = null,
        [Description("Optional current HP override (clamped to MaxHP).")]
        int? currentHP = null,
        [Description("Optional current MP override (clamped to MaxMP).")]
        int? currentMP = null,
        [Description("Optional gold override (>= 0).")]
        int? gold = null,
        [Description("Optional movement speed override (>= 0).")]
        int? speed = null,
        [Description("Optional initiative override.")]
        int? initiative = null,
        [Description("Optional background override.")]
        string? background = null,
        [Description("Optional portrait URL override.")]
        string? portraitUrl = null,
        [Description("Optional attribute overrides (keys: Might, Agility, Vitality, Wits, Presence).")]
        Dictionary<string, int>? attributeOverrides = null)
    {
        try
        {
            var gameState = await _gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState is null)
            {
                return JsonSerializer.Serialize(new UpdateCharacterStatsResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character is null)
            {
                return JsonSerializer.Serialize(new UpdateCharacterStatsResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            var updates = new List<string>();

            var requiresDerivedClamp = false;

            if (level.HasValue)
            {
                if (level.Value < 1)
                {
                    return JsonSerializer.Serialize(new UpdateCharacterStatsResult
                    {
                        Success = false,
                        Error = "Level must be at least 1."
                    });
                }

                character.Level = level.Value;
                updates.Add(nameof(Character.Level));
                requiresDerivedClamp = true;
            }

            if (experience.HasValue)
            {
                character.Experience = Math.Max(0, experience.Value);
                updates.Add(nameof(Character.Experience));
            }

            if (attributeOverrides is not null)
            {
                var parsedAttributes = new Dictionary<AttributeType, int>();
                foreach (var entry in attributeOverrides)
                {
                    if (!Enum.TryParse<AttributeType>(entry.Key, true, out var attributeKey))
                    {
                        return JsonSerializer.Serialize(new UpdateCharacterStatsResult
                        {
                            Success = false,
                            Error = $"Unknown attribute '{entry.Key}'."
                        });
                    }

                    parsedAttributes[attributeKey] = entry.Value;
                }

                foreach (var entry in parsedAttributes)
                {
                    character.Attributes[entry.Key] = entry.Value;
                }

                updates.Add(nameof(Character.Attributes));
                requiresDerivedClamp = true;
            }

            if (gold.HasValue)
            {
                character.Gold = Math.Max(0, gold.Value);
                updates.Add(nameof(Character.Gold));
            }

            if (speed.HasValue)
            {
                character.Speed = Math.Max(0, speed.Value);
                updates.Add(nameof(Character.Speed));
            }

            if (initiative.HasValue)
            {
                character.Initiative = initiative.Value;
                updates.Add(nameof(Character.Initiative));
            }

            if (!string.IsNullOrWhiteSpace(background))
            {
                character.Background = background;
                updates.Add(nameof(Character.Background));
            }

            if (!string.IsNullOrWhiteSpace(portraitUrl))
            {
                character.PortraitUrl = portraitUrl;
                updates.Add(nameof(Character.PortraitUrl));
            }

            if (currentHP.HasValue)
            {
                // Reason: CurrentHP must remain within the derived MaxHP after stat changes.
                character.CurrentHP = Math.Clamp(currentHP.Value, 0, character.MaxHP);
                updates.Add(nameof(Character.CurrentHP));
            }

            if (currentMP.HasValue)
            {
                // Reason: CurrentMP must remain within the derived MaxMP after stat changes.
                character.CurrentMP = Math.Clamp(currentMP.Value, 0, character.MaxMP);
                updates.Add(nameof(Character.CurrentMP));
            }

            if (requiresDerivedClamp && !currentHP.HasValue)
            {
                // Reason: Derived MaxHP changes should not leave CurrentHP above the new maximum.
                character.CurrentHP = Math.Clamp(character.CurrentHP, 0, character.MaxHP);
            }

            if (requiresDerivedClamp && !currentMP.HasValue)
            {
                // Reason: Derived MaxMP changes should not leave CurrentMP above the new maximum.
                character.CurrentMP = Math.Clamp(character.CurrentMP, 0, character.MaxMP);
            }

            character.UpdatedAt = DateTime.UtcNow;
            await _characterRepository.UpdateAsync(character);

            var result = new UpdateCharacterStatsResult
            {
                Success = true,
                CharacterId = character.Id,
                CharacterName = character.Name,
                Level = character.Level,
                Experience = character.Experience,
                CurrentHP = character.CurrentHP,
                MaxHP = character.MaxHP,
                CurrentMP = character.CurrentMP,
                MaxMP = character.MaxMP,
                Gold = character.Gold,
                Speed = character.Speed,
                Initiative = character.Initiative,
                Updates = updates,
                Message = updates.Count == 0
                    ? $"No changes applied for {character.Name}."
                    : $"Updated {character.Name}: {string.Join(", ", updates)}."
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new UpdateCharacterStatsResult
            {
                Success = false,
                Error = $"Error updating character stats: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Updates or removes a skill rank for a character.
    /// </summary>
    [Description("Sets a skill rank (0 removes the skill).")]
    public async Task<string> UpdateSkillRank(
        [Description("The character's unique ID")] string characterId,
        [Description("The skill name to update.")] string skillName,
        [Description("The desired rank (0 removes the skill).")]
        int newRank,
        [Description("The campaign ID")] string campaignId)
    {
        try
        {
            var gameState = await _gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState is null)
            {
                return JsonSerializer.Serialize(new UpdateSkillResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character is null)
            {
                return JsonSerializer.Serialize(new UpdateSkillResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            if (newRank < 0)
            {
                return JsonSerializer.Serialize(new UpdateSkillResult
                {
                    Success = false,
                    Error = "Skill rank cannot be negative."
                });
            }

            var previousRank = character.Skills.TryGetValue(skillName, out var currentRank)
                ? currentRank
                : 0;

            if (newRank == 0)
            {
                character.Skills.Remove(skillName);
            }
            else
            {
                character.Skills[skillName] = newRank;
            }

            character.UpdatedAt = DateTime.UtcNow;
            await _characterRepository.UpdateAsync(character);

            var result = new UpdateSkillResult
            {
                Success = true,
                CharacterId = character.Id,
                CharacterName = character.Name,
                SkillName = skillName,
                PreviousRank = previousRank,
                NewRank = newRank,
                Message = newRank == 0
                    ? $"Removed {skillName} from {character.Name}."
                    : $"Updated {character.Name}'s {skillName} from {previousRank} to {newRank}."
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new UpdateSkillResult
            {
                Success = false,
                Error = $"Error updating skill: {ex.Message}"
            });
        }
    }

   
}
