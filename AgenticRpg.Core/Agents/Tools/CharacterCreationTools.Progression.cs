using System.ComponentModel;
using System.Text.Json;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Rules;

namespace AgenticRpg.Core.Agents.Tools;

public partial class CharacterCreationTools
{
    private static string CreationError(string error) => JsonSerializer.Serialize(new CharacterSheetResult { Valid = false, Error = error });

    [Description("Save the starting level for a new character, from 1 to 7. Changing it resets earned skill and spell selections. Existing characters cannot use this tool.")]
    public async Task<string> SaveStartingLevel(
        [Description("The character creation session ID.")] string sessionId,
        [Description("The starting level, an integer from 1 through 7.")] int startingLevel)
    {
        if (!CharacterCreationRules.IsValidLevel(startingLevel)) return CreationError("Starting level must be between 1 and 7.");
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session is null || session.SessionType != SessionType.CharacterCreation) return CreationError("Character creation session not found.");
        if (session.IsCompleted || session.Context.IsCharacterModification) return CreationError("Starting level can only be changed for an unfinished new character.");
        var character = session.Context.DraftCharacter ??= new Character();
        if (character.Level != startingLevel)
        {
            if (session.Context.InitialCharacterSkills.Count == 4)
                CharacterCreationRules.ApplySkills(character, session.Context.InitialCharacterSkills.ToDictionary(s => s, _ => 1));
            else
            {
                character.Skills.Clear();
                character.CharacterSkills = [];
            }
            character.KnownSpells.Clear();
            character.MaxSpellSlots.Clear();
            character.CurrentSpellSlots.Clear();
            session.Context.CompletedSteps.RemoveAll(s => s is "skillProgression" or "spells");
        }
        character.Level = startingLevel;
        CharacterCreationRules.InitializeStats(character);
        character.UpdatedAt = session.LastUpdatedAt = DateTime.UtcNow;
        await sessionStateManager.UpdateSessionStateAsync(session);
        return JsonSerializer.Serialize(new CharacterSheetResult { Valid = true, Character = character });
    }

    [Description("Retrieve starting level, total skill-rank budget, initial skills, spell acquisition limits and eligible spells. Each spell limit describes one choice in acquisition order.")]
    public async Task<string> GetStartingProgression([Description("The character creation session ID.")] string sessionId)
    {
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter is not { } character) return CreationError("Character creation draft not found.");
        var limits = CharacterCreationRules.SpellLimits(character.Class, character.Level);
        return JsonSerializer.Serialize(new
        {
            Valid = true,
            StartingLevel = character.Level,
            InitialSkills = session.Context.InitialCharacterSkills,
            AdditionalSkillRanks = CharacterCreationRules.AdditionalSkillRanks(character.Level),
            TotalSkillRanks = 4 + CharacterCreationRules.AdditionalSkillRanks(character.Level),
            SpellChoiceLimits = limits,
            AvailableSpells = CharacterProgression.GetSpellsForClass(character.Class, limits.Count == 0 ? 0 : limits.Max()),
            RemainingRequirement = CharacterCreationRules.ValidateDraft(character, session.Context)
        });
    }

    [Description("Replace the full starting skill-rank allocation. Preserve the four initial skills at rank 1 or higher and spend exactly two additional ranks per level after level 1. Additional skills are allowed; no rank cap.")]
    public async Task<string> SaveStartingSkillRanks(
        [Description("The character creation session ID.")] string sessionId,
        [Description("Complete map of valid skill names to positive ranks, including all four initial skills.")] Dictionary<string, int> skillRanks)
    {
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftCharacter is not { } character) return CreationError("Character creation draft not found.");
        if (session.IsCompleted || session.Context.IsCharacterModification) return CreationError("Starting progression requires an unfinished new character.");
        var error = CharacterCreationRules.ValidateSkills(skillRanks, session.Context.InitialCharacterSkills, character.Level);
        if (error is not null) return CreationError(error);
        CharacterCreationRules.ApplySkills(character, skillRanks);
        if (!session.Context.CompletedSteps.Contains("skillProgression")) session.Context.CompletedSteps.Add("skillProgression");
        character.UpdatedAt = session.LastUpdatedAt = DateTime.UtcNow;
        await sessionStateManager.UpdateSessionStateAsync(session);
        return JsonSerializer.Serialize(new CharacterSheetResult { Valid = true, Character = character });
    }
}
