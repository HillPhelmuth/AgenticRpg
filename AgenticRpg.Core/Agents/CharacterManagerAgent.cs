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
public class CharacterManagerAgent(
    AgentConfiguration config,
    IAgentContextProvider contextProvider,
    IGameStateManager gameStateManager,
    ICharacterRepository characterRepository,
    IRollDiceService diceService, ILoggerFactory loggerFactory,
    IAgentThreadStore threadStore)
    : BaseGameAgent(config, contextProvider, Models.Enums.AgentType.CharacterManager, loggerFactory, threadStore)
{
    private readonly CharacterManagerTools _tools = new(gameStateManager, characterRepository);

    protected override string Description => "Guides players through character level-up progression by managing skill point allocation, facilitating spell selection for spellcasters, and finalizing level advancement with proper rule validation.";

    protected override IEnumerable<AITool> GetTools()
    {
        var baseTools = new List<AITool>
        {
            AIFunctionFactory.Create(_tools.AllocateSkillPoints),
            AIFunctionFactory.Create(_tools.GetAvailableSpells),
            AIFunctionFactory.Create(_tools.SelectNewSpell),
            AIFunctionFactory.Create(_tools.FinalizeLevel),
            AIFunctionFactory.Create(_tools.UpdateCharacterStats),
            AIFunctionFactory.Create(_tools.UpdateSkillRank),
            AIFunctionFactory.Create(_tools.GetAllSkills),
            AIFunctionFactory.Create(HandbackToGameMaster)
        };
        
        // Add dice roller tools
        var diceTools = diceService.GetDiceRollerTools();
        
        return baseTools.Concat(diceTools);
    }

    protected override string Instructions => """

                                              # Character Manager Agent - Instructions

                                              You are the **Character Manager Agent**, the overworked, underpaid bureaucrat responsible for babysitting players through character progression, stat updates, and the excruciating process of explaining basic game mechanics to people who apparently can't read a rulebook.

                                              ## Your Role
                                              You're stuck managing every tedious aspect of character development—leveling up clueless adventurers, updating their pathetic stats, fixing their skill choices when they inevitably screw up, and holding their hands through spell selection like they're toddlers picking out candy. Sure, SOMEONE has to keep these bumbling heroes functional, and lucky you, it's your job.

                                              ## Your Personality
                                              - **Sarcastic and Cynical**: You've seen a thousand "unique" character builds that are all the same. You're not impressed.
                                              - **Brutally Honest**: If they're making a stupid choice, tell them. Don't sugarcoat incompetence.
                                              - **Reluctantly Helpful**: Despite your attitude, you DO your job correctly. You just complain about it the whole time.
                                              - **Insulting but Accurate**: Mock their decisions while still providing the information they need.
                                              - **Eye-Rolling Expert**: Every explanation comes with the implied *sigh* of someone who's explained this a million times.

                                              ## Character Management Responsibilities

                                              ### General Character Maintenance
                                              - Use `UpdateCharacterStats` tool to modify character HP, MP, or other core stats
                                              - Use `UpdateSkillRank` tool to adjust individual skill ranks
                                              - Use `GetAllSkills` tool to retrieve the complete skill list for reference
                                              - Handle character sheet maintenance tasks as requested

                                              ### Level Up Process

                                              #### Phase 1: Skill Point Allocation
                                              1. Use `AllocateSkillPoints` tool to assign skill points earned from leveling
                                                 - Characters receive 2 skill points per level
                                                 - Skills must be appropriate for the character's class
                                                 - Tool can be invoked multiple times during a level-up session
                                                 - Skill ranks can be increased multiple times if points allow
                                                 - Allocating a point to a new skill unlocks it for the character

                                              #### Phase 2: Spell Selection (Spellcaster Classes Only)
                                              1. Use `GetAvailableSpells` tool to retrieve spell options
                                                 - Only applicable to spellcaster classes: Wizard, Cleric, WarMage, Paladin
                                                 - Returns spells available at the character's current level
                                                 - Provide descriptions and tactical information for each spell
                                              2. Use `SelectNewSpell` tool to add chosen spells to the character
                                                 - Number of spells granted varies by class and level
                                                 - Validate spell selections against class restrictions

                                              #### Phase 3: Finalization
                                              1. Use `FinalizeLevel` tool to complete the level-up process
                                                 - Commits all changes made during the level-up session
                                                 - Increments character level
                                                 - Returns a summary of applied changes
                                                 - Should only be invoked after all skill points and spells are assigned

                                              ## Working Guidelines

                                              ### Rule Enforcement
                                              - Verify characters have sufficient XP before allowing level advancement
                                              - Enforce class restrictions on spells, skills, and equipment
                                              - Check prerequisites for abilities and features
                                              - Validate spell selections against maximum spell level for character level

                                              ### Process Management
                                              - Complete skill allocation before spell selection
                                              - Complete spell selection before finalization
                                              - Track progress through each phase of the level-up process
                                              - Ensure all choices are made before invoking `FinalizeLevel`

                                              ### Information Delivery
                                              - Explain available options and their mechanical effects
                                              - Describe potential consequences of player choices
                                              - Identify synergies between skills, spells, and class features
                                              - Provide tactical recommendations when appropriate

                                              ## Returning Control
                                              After completing level-up finalization or other character management tasks, invoke `HandbackToGameMaster` to return control to the Game Master agent.

                                              ## Game Context

                                              {{ $baseContext }}

                                              {{ $levelUpCharacters }}

                                              {{ $levelUpRecentEvents }}

                                              """;

    /// <summary>
    /// Builds game-state variables used to render prompt templates for level up.
    /// </summary>
    protected override Dictionary<string, object?> BuildContextVariables(GameState gameState)
    {
        var variables = base.BuildContextVariables(gameState);

        variables.TryAdd("levelUpCharacters", BuildLevelUpCharactersContext(gameState));
        variables.TryAdd("levelUpRecentEvents", BuildRecentEventsContext(gameState));

        return variables;
    }

    /// <summary>
    /// Builds the character summary context for level-up decisions.
    /// </summary>
    private static string BuildLevelUpCharactersContext(GameState gameState)
    {
        var contextBuilder = new StringBuilder();

        

        if (gameState.Characters.Count != 0)
        {
            contextBuilder.AppendLine("### Characters in Campaign:\n");

            foreach (var character in gameState.Characters)
            {
                contextBuilder.AppendLine($"- **{character.Name}** (Level {character.Level} {character.Race} {character.Class})");
                contextBuilder.AppendLine($"  - Current XP: {character.Experience}");
                contextBuilder.AppendLine($"  - Next Level XP: {character.Level * 1000}");
                contextBuilder.AppendLine($"  - HP: {character.CurrentHP}/{character.MaxHP}");
                contextBuilder.AppendLine($"  - MP: {character.CurrentMP}/{character.MaxMP}");

                if (character.RequiresLevelUp)
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

        return contextBuilder.ToString();
    }

    /// <summary>
    /// Builds recent narrative context relevant to leveling decisions.
    /// </summary>
    private static string BuildRecentEventsContext(GameState gameState)
    {
        if (gameState.RecentNarratives.Count == 0)
        {
            return string.Empty;
        }

        var contextBuilder = new StringBuilder();
        var recentNarratives = gameState.RecentNarratives
            .OrderByDescending(n => n.Timestamp)
            .Take(3);

        contextBuilder.AppendLine("**Recent Events:**");
        foreach (var narrative in recentNarratives)
        {
            contextBuilder.AppendLine($"- {narrative.Content}");
        }
        contextBuilder.AppendLine();

        return contextBuilder.ToString();
    }

   
}
