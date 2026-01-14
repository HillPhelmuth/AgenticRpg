using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Agents.Tools;

public partial class GameMasterTools
{
    [Description("Updates selected character properties (vitals, gold, biography, attributes) and persists the campaign state.")]
    public async Task<string> UpdateCharacterDetails(
        [Description("The unique ID of the character to update.")] string characterId,
        [Description("The unique ID of the campaign where the character exists.")] string campaignId,
        [Description("Optional current HP override.")] int? currentHP = null,
        [Description("Optional current MP override.")] int? currentMP = null,
        [Description("Optional temporary HP override.")] int? tempHP = null,
        [Description("Optional total gold override.")] int? gold = null,
        [Description("Optional updated background or bio text.")] string? background = null,
        [Description("Optional updated portrait URL.")] string? portraitUrl = null,
        [Description("Optional map of attribute scores keyed by attribute name (e.g., Might, Agility).")] Dictionary<string, int>? attributeOverrides = null,
        [Description("Optional map of skill ranks keyed by skill name.")] Dictionary<string, int>? skillRanks = null)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState == null)
        {
            return JsonSerializer.Serialize(new CharacterUpdateResult
            {
                Success = false,
                Message = "Campaign not found",
                Error = "Campaign not found"
            });
        }

        var character = gameState.GetCharacter(characterId);
        if (character == null)
        {
            return JsonSerializer.Serialize(new CharacterUpdateResult
            {
                Success = false,
                Message = "Character not found",
                Error = "Character not found"
            });
        }

        var updates = new List<string>();

        if (currentHP.HasValue)
        {
            character.CurrentHP = Math.Clamp(currentHP.Value, 0, character.MaxHP > 0 ? character.MaxHP : int.MaxValue);
            updates.Add("CurrentHP");
        }
        
        if (currentMP.HasValue)
        {
            character.CurrentMP = Math.Clamp(currentMP.Value, 0, character.MaxMP > 0 ? character.MaxMP : int.MaxValue);
            updates.Add("CurrentMP");
        }
        
        if (tempHP.HasValue)
        {
            character.CurrentHP = Math.Max(0, tempHP.Value);
            updates.Add("TempHP");
        }

        if (gold.HasValue)
        {
            character.Gold = Math.Max(0, gold.Value);
            updates.Add("Gold");
        }

        if (!string.IsNullOrWhiteSpace(background))
        {
            character.Background = background;
            updates.Add("Background");
        }

        if (!string.IsNullOrWhiteSpace(portraitUrl))
        {
            character.PortraitUrl = portraitUrl;
            updates.Add("PortraitUrl");
        }

        if (attributeOverrides != null)
        {
            foreach (var entry in attributeOverrides)
            {
                if (Enum.TryParse<AttributeType>(entry.Key, true, out var attributeKey))
                {
                    character.Attributes[attributeKey] = entry.Value;
                    updates.Add($"Attribute:{attributeKey}");
                }
            }
        }

        if (skillRanks != null)
        {
            character.Skills ??= new Dictionary<string, int>();
            foreach (var entry in skillRanks)
            {
                character.Skills[entry.Key] = entry.Value;
                updates.Add($"Skill:{entry.Key}");
            }
        }

        if (updates.Count == 0)
        {
            return JsonSerializer.Serialize(new CharacterUpdateResult
            {
                Success = false,
                Message = "No changes were applied",
                Error = "No updates provided"
            });
        }
        
        await stateManager.UpdateCampaignStateAsync(gameState);

        return JsonSerializer.Serialize(new CharacterUpdateResult
        {
            Success = true,
            Character = BuildCharacterSnapshot(character, true, true),
            Message = $"Updated {updates.Count} field(s): {string.Join(", ", updates)}"
        });
    }

    [Description("Awards experience points to one or more characters. Parameters: characterIds (array of character IDs), experienceAmount (XP to award: Minor=50, Moderate=100, Major=250, Epic=500), reason (description of why XP was awarded), campaignId. Returns updated character XP and level-up notifications.")]
    public async Task<string> AwardExperience(
        [Description("Array of character IDs to receive experience points. Can be one character or multiple characters (for party-wide rewards).")] string[] characterIds,
        [Description("The amount of experience points to award. Guidelines: Minor achievement=50, Moderate=100, Major=250, Epic=500, Quest completion varies by difficulty.")] int experienceAmount,
        [Description("A text description explaining why this XP was awarded (e.g., 'Defeated goblin raiders', 'Completed main quest', 'Clever problem solving').")] string reason,
        [Description("The unique ID of the campaign where these characters exist.")] string campaignId)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState == null)
        {
            return JsonSerializer.Serialize(new AwardExperienceResult
            {
                Success = false,
                Results = new List<ExperienceAwardDetail>(),
                Message = "Campaign not found"
            });
        }

        var results = new List<ExperienceAwardDetail>();

        foreach (var characterId in characterIds)
        {
            var character = gameState.GetCharacter(characterId);
            if (character == null)
            {
                results.Add(new ExperienceAwardDetail
                {
                    CharacterId = characterId,
                    Success = false,
                    Reason = reason,
                    Error = "Character not found"
                });
                continue;
            }

            var oldXP = character.Experience;
            var oldLevel = character.Level;
            character.Experience += experienceAmount;

            var xpForNextLevel = character.Level * 1000;
            var leveledUp = character.Experience >= xpForNextLevel;

            if (leveledUp)
            {
                character.Level++;
            }

            results.Add(new ExperienceAwardDetail
            {
                CharacterName = character.Name,
                CharacterId = characterId,
                Success = true,
                OldXP = oldXP,
                NewXP = character.Experience,
                XPGained = experienceAmount,
                OldLevel = oldLevel,
                NewLevel = character.Level,
                LeveledUp = leveledUp,
                Reason = reason
            });
        }

        await stateManager.UpdateCampaignStateAsync(gameState);

        return JsonSerializer.Serialize(new AwardExperienceResult
        {
            Success = true,
            Results = results,
            Message = $"Awarded {experienceAmount} XP to {characterIds.Length} character(s)"
        });
    }
}
