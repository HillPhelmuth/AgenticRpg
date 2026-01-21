using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.State;

namespace AgenticRpg.Core.Agents.Tools;

/// <summary>
/// Tools for managing character progression and adjustments, including level-up.
/// </summary>
public partial class CharacterManagerTools
{
    private readonly IGameStateManager _gameStateManager;
    private readonly ICharacterRepository _characterRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="CharacterManagerTools"/> class.
    /// </summary>
    public CharacterManagerTools(IGameStateManager gameStateManager, ICharacterRepository characterRepository)
    {
        _gameStateManager = gameStateManager;
        _characterRepository = characterRepository;
    }

    /// <summary>
    /// Finalizes the level up process and saves the character.
    /// </summary>
    [Description("Completes the level up process by updating the character level and saving all changes.")]
    public async Task<string> FinalizeLevel(
        [Description("The unique ID of the character completing the level up.")] string characterId,
        [Description("The new level to set for the character (must be higher than current level).")] int newLevel,
        [Description("The unique ID of the campaign this character belongs to.")] string campaignId)
    {
        try
        {
            var gameState = await _gameStateManager.GetCampaignStateAsync(campaignId);
            if (gameState is null)
            {
                return JsonSerializer.Serialize(new FinalizeLevelResult
                {
                    Success = false,
                    Error = $"Campaign with ID {campaignId} not found."
                });
            }

            var character = gameState.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character is null)
            {
                return JsonSerializer.Serialize(new FinalizeLevelResult
                {
                    Success = false,
                    Error = $"Character with ID {characterId} not found."
                });
            }

            if (newLevel <= character.Level)
            {
                return JsonSerializer.Serialize(new FinalizeLevelResult
                {
                    Success = false,
                    Error = $"New level {newLevel} must be greater than current level {character.Level}."
                });
            }

            var previousLevel = character.Level;
            character.Level = newLevel;
            character.UpdatedAt = DateTime.UtcNow;

            var nextLevelXP = newLevel * 1000;

            var newAbilities = new List<string>();
            var newSpells = new List<string>();
            var skillIncreases = new Dictionary<string, int>();
            // Reason: Abilities are represented as skills; no separate ability inventory tracking.

            foreach (var spell in character.KnownSpells)
            {
                newSpells.Add($"{spell.Name} (level {spell.Level})");
            }

            foreach (var skill in character.Skills)
            {
                skillIncreases[skill.Key] = skill.Value;
            }

            await _characterRepository.UpdateAsync(character);

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
}
