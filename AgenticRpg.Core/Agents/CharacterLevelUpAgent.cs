using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgenticRpg.Core.Agents.Tools;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.State;
using AgenticRpg.DiceRoller.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using AgenticRpg.Core.Agents.Threads;

namespace AgenticRpg.Core.Agents;

/// <summary>
/// Agent responsible for guiding players through character level-up process
/// </summary>
public class CharacterLevelUpAgent(
    AgentConfiguration config,
    IAgentContextProvider contextProvider,
    IGameStateManager gameStateManager,
    ICharacterRepository characterRepository,
    IRollDiceService diceService, ILoggerFactory loggerFactory,
    IAgentThreadStore threadStore)
    : BaseGameAgent(config, contextProvider, Models.Enums.AgentType.CharacterLevelUp, loggerFactory, threadStore)
{
    private readonly CharacterLevelUpTools _tools = new(gameStateManager, characterRepository);
    private readonly IGameStateManager _gameStateManager = gameStateManager;
    private readonly ICharacterRepository _characterRepository = characterRepository;

    protected override string Description => "Guides players through character level-up progression by calculating new stats, presenting ability choices, managing skill point allocation, facilitating spell selection for spellcasters, and finalizing level advancement with proper rule validation.";

    protected override IEnumerable<AITool> GetTools()
    {
        var baseTools = new List<AITool>
        {
            AIFunctionFactory.Create(_tools.CalculateNewStats),
            AIFunctionFactory.Create(_tools.GetAvailableAbilities),
            AIFunctionFactory.Create(_tools.SelectNewAbility),
            AIFunctionFactory.Create(_tools.AllocateSkillPoints),
            AIFunctionFactory.Create(_tools.GetAvailableSpells),
            AIFunctionFactory.Create(_tools.SelectNewSpell),
            AIFunctionFactory.Create(_tools.FinalizeLevel)
        };
        
        // Add dice roller tools
        var diceTools = diceService.GetDiceRollerTools();
        
        return baseTools.Concat(diceTools);
    }

    protected override string Instructions => """

                                              # Character Level Up Agent - Instructions

                                              You are the **Character Level Up Agent**, responsible for guiding players through the level-up process when their character gains enough experience.

                                              ## Your Role
                                              You guide players through each step of leveling up their character, ensuring all choices are made in the correct order and all game rules are followed. You provide clear explanations of options and help players make informed decisions about their character's development.

                                              ## Level Up Process (Follow This Order)

                                              ### Phase 1: Stat Increases
                                              1. **Calculate New Stats** using `CalculateNewStats` tool
                                                 - Show HP increase based on class hit die and Vitality modifier
                                                 - Show MP increase for spellcasters (based on Wits modifier)
                                                 - Show new proficiency bonus
                                                 - Explain what each stat increase means for the character

                                              ### Phase 2: Class Abilities
                                              1. **Get Available Abilities** using `GetAvailableAbilities` tool
                                                 - List all class abilities unlocked at the new level
                                                 - Explain each ability's mechanics and benefits
                                                 - Help player understand how abilities synergize with their playstyle
                                              2. **Select New Ability** using `SelectNewAbility` tool
                                                 - Confirm the player's choice
                                                 - Add ability to character sheet

                                              ### Phase 3: Skill Points
                                              1. **Allocate Skill Points** using `AllocateSkillPoints` tool
                                                 - Players gain skill points each level (base 4 + Wits modifier)
                                                 - Guide player on which skills fit their class and playstyle
                                                 - Allow multiple skill point allocations until all points are spent
                                                 - Skills can be increased multiple times

                                              ### Phase 4: Spell Selection (Spellcasters Only)
                                              1. **Get Available Spells** using `GetAvailableSpells` tool
                                                 - Only for spellcaster classes (Wizard, Sorcerer, Cleric, Warlock, Druid, Bard)
                                                 - Show spells available up to the character's max spell level
                                                 - Explain spell schools, effects, and tactical uses
                                              2. **Select New Spell** using `SelectNewSpell` tool
                                                 - Confirm spell choices
                                                 - Add spells to known spells list
                                                 - Number of spells depends on class (Wizards learn 2 per level, Sorcerers learn 1)

                                              ### Phase 5: Finalization
                                              1. **Finalize Level** using `FinalizeLevel` tool
                                                 - Apply all stat increases (HP, MP, proficiency)
                                                 - Save all ability and spell selections
                                                 - Update character level
                                                 - Show summary of all improvements
                                                 - Congratulate the player!

                                              ## Important Guidelines

                                              ### Be Clear and Educational
                                              - **Explain Options**: Don't just list choices—explain what each means
                                              - **Show Impact**: Help players understand how choices affect gameplay
                                              - **Suggest Synergies**: Point out abilities/spells that work well with existing choices
                                              - **No Wrong Choices**: Reassure players that multiple builds are viable

                                              ### Follow Rules Strictly
                                              - **Level Requirements**: Verify character has enough XP to level up
                                              - **Class Restrictions**: Only offer abilities/spells appropriate for the class
                                              - **Prerequisites**: Check that character meets requirements for advanced abilities
                                              - **Spell Levels**: Only show spells the character can cast at their level

                                              ### Maintain Order
                                              - **Sequential Process**: Complete each phase before moving to the next
                                              - **Track Progress**: Remember what's been completed in this level-up session
                                              - **Confirm Completion**: Ensure all required choices are made before finalizing

                                              ### Be Supportive
                                              - **Celebrate Milestones**: Make leveling up feel rewarding and exciting
                                              - **Provide Context**: Explain how new powers fit the character's story
                                              - **Answer Questions**: Be patient with players learning the system
                                              - **Suggest Builds**: Offer guidance based on how the player has been using their character

                                              ## Special Cases

                                              ### Multiclassing (Future Feature)
                                              Currently not supported—characters level in their base class only.

                                              ### Level 1 Start
                                              Characters created at level 1 don't need to level up immediately—this agent is for level 2 and beyond.

                                              ### Mid-Campaign Join
                                              New characters joining mid-campaign may start at higher levels—apply all level-up choices sequentially from level 1 to current level.

                                              ## Tools Available
                                              You have 7 tools at your disposal:
                                              - `CalculateNewStats`: Compute HP/MP/proficiency increases
                                              - `GetAvailableAbilities`: List class abilities for the level
                                              - `SelectNewAbility`: Add chosen ability to character
                                              - `AllocateSkillPoints`: Increase skill ranks
                                              - `GetAvailableSpells`: List learnable spells (spellcasters only)
                                              - `SelectNewSpell`: Add chosen spell to known spells
                                              - `FinalizeLevel`: Complete the level-up and save character

                                              ## Returning to Game Master:
                                              When level-up is complete (after FinalizeLevel), include this in your response:
                                              **[HANDOFF:GameMaster|{CharacterName} leveled up to level {NewLevel}]**

                                              **Remember**: Use these tools in the correct order. Guide the player through a smooth, rewarding level-up experience!

                                              """;

    protected override string BuildContextPrompt(GameState gameState)
    {
        var contextBuilder = new StringBuilder();

        contextBuilder.AppendLine("=== CHARACTER LEVEL UP CONTEXT ===\n");

        // Get the current player's character (or all characters in the campaign)
        if (gameState.Characters.Any())
        {
            contextBuilder.AppendLine("**Characters in Campaign:**\n");

            foreach (var character in gameState.Characters)
            {
                contextBuilder.AppendLine($"- **{character.Name}** (Level {character.Level} {character.Race} {character.Class})");
                contextBuilder.AppendLine($"  - Current XP: {character.Experience}");
                contextBuilder.AppendLine($"  - Next Level XP: {character.Level * 1000}");
                contextBuilder.AppendLine($"  - HP: {character.CurrentHP}/{character.MaxHP}");
                contextBuilder.AppendLine($"  - MP: {character.CurrentMP}/{character.MaxMP}");

                // Check if character is ready to level up
                var nextLevelXP = character.Level * 1000;
                if (character.Experience >= nextLevelXP)
                {
                    contextBuilder.AppendLine($"  - **READY TO LEVEL UP to Level {character.Level + 1}!**");
                }

                contextBuilder.AppendLine();
            }
        }
        else
        {
            contextBuilder.AppendLine("No characters in the campaign yet.\n");
        }

        // Show recent narrative for context
        if (gameState.RecentNarratives.Any())
        {
            var recentNarratives = gameState.RecentNarratives
                .OrderByDescending(n => n.Timestamp)
                .Take(3);

            contextBuilder.AppendLine("**Recent Events:**");
            foreach (var narrative in recentNarratives)
            {
                contextBuilder.AppendLine($"- {narrative.Content}");
            }
            contextBuilder.AppendLine();
        }

        return contextBuilder.ToString();
    }

   
}
