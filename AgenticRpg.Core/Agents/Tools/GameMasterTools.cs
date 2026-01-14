using AgenticRpg.Core.Agents.Llms;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Rules;
using AgenticRpg.Core.Services;
using AgenticRpg.Core.State;
using AgenticRpg.DiceRoller.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
#pragma warning disable OPENAI001

namespace AgenticRpg.Core.Agents.Tools;

/// <summary>
/// Game Master tools for narrative control, skill checks, and agent coordination.
/// Each method is decorated with [Description] for AI function calling.
/// </summary>
public partial class GameMasterTools(
    IGameStateManager stateManager,
    INarrativeRepository narrativeRepository)
{
    [Description("Rolls a skill check for a character. Parameters: characterId, skillName (Athletics, Stealth, Perception, Persuasion, etc.), attribute (Might, Agility, Vitality, Wits, Presence), difficultyClass (DC: Easy=10, Moderate=15, Hard=20, Very Hard=25), hasAdvantage (true/false), hasDisadvantage (true/false), campaignId. Returns roll result, total, and success/failure.")]
    public async Task<string> RollSkillCheck(
        [Description("The unique ID of the character performing the skill check.")] string characterId,
        [Description("The name of the skill being checked. Valid skills include: Athletics, Stealth, Perception, Persuasion, Arcana, History, Investigation, etc.")] string skillName,
        [Description("The primary attribute used for this skill check. Must be one of: Might, Agility, Vitality, Wits, Presence.")] AttributeType attribute,
        [Description("The value the roll needs to meet to succeed.")] int successRollValue,
        [Description("The unique ID of the campaign where this action is occurring.")] string campaignId)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState == null)
        {
            return JsonSerializer.Serialize(new SkillCheckResult
            {
                Success = false,
                Description = string.Empty,
                Error = "Campaign not found"
            });
        }

        var character = gameState.GetCharacter(characterId);
        if (character == null)
        {
            return JsonSerializer.Serialize(new SkillCheckResult
            {
                Success = false,
                Description = string.Empty,
                Error = "Character not found"
            });
        }

        var roll1 = Random.Shared.Next(1, 21);

        var finalRoll = roll1;

        var attributeValue = character.Attributes[attribute];
        var attributeModifier = AttributeCalculator.CalculateModifier(attributeValue);
        var skillRank = character.Skills?.TryGetValue(skillName, out var rank) == true ? rank : 0;
        var total = finalRoll + attributeModifier + skillRank;
        var success = total >= successRollValue;

        var rollDescription = $"Rolled d20: {finalRoll}";

        var result = new SkillCheckResult
        {
            Success = success,
            Character = character.Name,
            Skill = skillName,
            Attribute = attribute.ToString(),
            Roll = finalRoll,
            AttributeModifier = attributeModifier,
            SkillRank = skillRank,
            Total = total,
            DC = successRollValue,
            Description = $"{rollDescription} + {attributeModifier} ({attribute}) + {skillRank} (skill) = {total} vs DC {successRollValue} - {(success ? "Success!" : "Failure")}"
        };

        return JsonSerializer.Serialize(result);
    }
    [Description("Gets a list of available monster names for a given difficulty level (0-15). Returns a comma-separated string of monster names. Must be called before `InitiateCombat`.")]
    public string GetAvailableMonsterNames(int difficultyLevel = 0)
    {
        if (difficultyLevel > 1)
            difficultyLevel--;
        var rpgMonsters = RpgMonster.GetAllRpgMonsters(difficultyLevel);
        var names = string.Join(", ", rpgMonsters.Select(m => m.Name).ToList());
        return names;
    }
    [Description("Initiates combat by creating combatants and transitioning to the Combat Agent. Returns confirmation that combat has started and Combat Agent is taking over.")]
    public async Task<string> InitiateCombat(
        [Description("A narrative description of the combat encounter, including the setting, enemies present, and the situation that triggered combat.")] string encounterDescription,
        [Description("Array of enemy monster names extracted from available names from `GetAvailableMonsterNames`. If a name is not found, a random monster will be generated instead.")] string[] enemyNames,
        [Description("The terrain of the combat encounter.")] string terrain,
        [Description("The overall difficulty of the combat encounter from 0 (standard early encounter) to 15 (high-level boss enemies). This is used as a fallback challenge rating when generating random monsters.")] int overallDifficulty,
        [Description("The unique ID of the campaign where this combat is occurring.")] string campaignId)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState == null)
        {
            return JsonSerializer.Serialize(new InitiateCombatResult
            {
                Success = false,
                Message = string.Empty,
                Error = "Campaign not found"
            });
        }

        var monsters = new List<RpgMonster>();
        var requestedNames = enemyNames ?? [];

        foreach (var requested in requestedNames.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var created = RpgMonster.GetRpgMonsterByName(requested);
            if (created != null)
            {
                monsters.Add(created);
            }
        }

        var desiredCount = Math.Max(1, requestedNames.Length);
        if (monsters.Count < desiredCount)
        {
            // # Reason: InitiateCombat must always have monsters. If any requested names are missing,
            // fill the remainder with random monsters at a difficulty approximated by overallDifficulty.
            var remaining = desiredCount - monsters.Count;
            var challengeRating = Math.Clamp(overallDifficulty, 0, 15).ToString();
            var encounter = await DndMonsterService.CreateRandomMonsterEncounter(remaining, challengeRating);
            monsters.AddRange(encounter.Monsters);
        }

        foreach (var monster in monsters.Where(monster => monster is { CurrentHP: <= 0, MaxHP: > 0 }))
        {
            monster.CurrentHP = monster.MaxHP;
        }

        var combat = new CombatEncounter
        {
            Id = Guid.NewGuid().ToString(),
            CampaignId = campaignId,
            Status = CombatStatus.Active,
            Round = 1,
            Terrain = terrain,
            PartyCharacters = gameState.Characters,
            EnemyMonsters = monsters,
        };

        combat.CombatLog.Add(new CombatLogEntry
        {
            Round = 1,
            ActionType = "CombatStart",
            Description = encounterDescription,
            Timestamp = DateTime.UtcNow
        });

        gameState.CurrentCombat = combat;
        gameState.ActiveAgent = "Combat";
        await stateManager.UpdateCampaignStateAsync(gameState);

        return JsonSerializer.Serialize(new InitiateCombatResult
        {
            Success = true,
            Message = "Combat initiated. Combat Agent is now active.",
            CombatId = combat.Id,
            Enemies = requestedNames,
            Description = encounterDescription
        });
    }

    [Description("Records a narrative event to the game log. Parameters: narrativeText (the story/description to add), narrativeType (Story, Combat, Dialog, System), visibility (Global, GMOnly, CharacterSpecific), campaignId, targetCharacterId (optional, only for CharacterSpecific visibility). Returns confirmation and narrative ID.")]
    public async Task<string> RecordNarrative(
        [Description("The narrative text content to record. This is the story description, dialogue, or event that occurred in the game.")] string narrativeText,
        [Description("The category of narrative being recorded. Common values: Story, Combat, Dialog, System, Quest, Discovery.")] string narrativeType,
        [Description("Who can see this narrative entry. Must be one of: Global (all players see), GMOnly (only game master sees), CharacterSpecific (only one character sees).")] NarrativeVisibility visibility,
        [Description("The unique ID of the campaign where this narrative belongs.")] string campaignId,
        [Description("Optional. Required only if visibility is CharacterSpecific. The character ID who should see this narrative.")] string? targetCharacterId = null)
    {
        try
        {
            if (visibility == NarrativeVisibility.CharacterSpecific && string.IsNullOrEmpty(targetCharacterId))
            {
                return JsonSerializer.Serialize(new RecordNarrativeResult
                {
                    Success = false,
                    NarrativeId = string.Empty,
                    Message = "targetCharacterId is required for CharacterSpecific visibility"
                });
            }
            var campaignState = await stateManager.GetCampaignStateAsync(campaignId);

            var narrative = new Narrative
            {
                CampaignId = campaignId,
                Content = narrativeText,
                Type = narrativeType,
                Visibility = visibility,
                TargetCharacterId = visibility == NarrativeVisibility.CharacterSpecific ? targetCharacterId : null,
                AgentType = "GameMaster",
                Timestamp = DateTime.UtcNow
            };

            var created = await narrativeRepository.CreateAsync(narrative);
            campaignState.RecentNarratives.Add(created);
            await stateManager.UpdateCampaignStateAsync(campaignState);
            return JsonSerializer.Serialize(new RecordNarrativeResult
            {
                Success = true,
                NarrativeId = created.Id,
                Message = "Narrative recorded successfully"
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new RecordNarrativeResult
            {
                Success = false,
                NarrativeId = string.Empty,
                Message = $"Error recording narrative: {ex.Message}"
            });
        }
    }

    private const string ChallengeRatingDescription = """
                                                      (Optional)
                                                      The difficulty level of the monster(s) (0-15). Can be a single value like "10" or a comma seprated list string like: "4,10,0" to get multiple difficulty levels.
                                                      """;

    [Description("Generate an image of a major campaign event. Outputs markdown to display image to players.")]
    public async Task<string> GenerateCampaignEventImage([Description("The unique ID of the campaign where this narrative belongs.")] string campaignId, [Description("The style of the image to generate.")] string style, [Description("Detailed description of scene to display. Will be used as prompt for image generator.")] string eventDescription)
    {
        // Implementation for generating an image based on the campaign event
        // This could involve calling an external image generation API or service
        var instructions = $"Generate a {style} image for the following RPG event: {eventDescription}";
        var campaignImage = await ImageGenService.GenerateCampaignImage(instructions, campaignId);
        return $"![Campaign Event]({campaignImage})";
    }
}
