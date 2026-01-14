using System.Text;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Agents.Tools;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.State;
using AgenticRpg.DiceRoller.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using AgenticRpg.Core.Agents.Threads;

namespace AgenticRpg.Core.Agents;

/// <summary>
/// AI Agent responsible for orchestrating the game narrative, adjudicating player actions,
/// and managing the overall campaign flow. Uses AITool function calling for game mechanics.
/// </summary>
public class GameMasterAgent : BaseGameAgent
{
    private readonly GameMasterTools _tools;
    private readonly IRollDiceService _diceService;
    private readonly ILogger<GameMasterAgent> _logger;

    public GameMasterAgent(
        AgentConfiguration config,
        IAgentContextProvider contextProvider,
        IGameStateManager stateManager,
        INarrativeRepository narrativeRepository,
        IRollDiceService diceService, ILoggerFactory loggerFactory,
        IAgentThreadStore threadStore)
        : base(config, contextProvider, AgentType.GameMaster, loggerFactory, threadStore)
    {
        _diceService = diceService;
        _tools = new GameMasterTools(stateManager, narrativeRepository);
        _logger = loggerFactory.CreateLogger<GameMasterAgent>();
    }
    
    protected override string Description => "Orchestrates the game narrative, adjudicates player actions, manages campaign flow, and coordinates handoffs to specialized agents for combat, shopping, and character progression.";
    private List<AITool>? _getTools;
    /// <summary>
    /// Gets the tools available to this agent for function calling.
    /// </summary>
    protected override IEnumerable<AITool> GetTools()
    {
        
        var baseTools = new List<AITool>
        {
            AIFunctionFactory.Create(_tools.RollSkillCheck),
            AIFunctionFactory.Create(_tools.GetAvailableMonsterNames),
            AIFunctionFactory.Create(_tools.GenerateCampaignEventImage),
            AIFunctionFactory.Create(_tools.InitiateCombat),
            AIFunctionFactory.Create(_tools.RecordNarrative),
            AIFunctionFactory.Create(_tools.AwardExperience),
            AIFunctionFactory.Create(_tools.GetCharacterDetails),
            AIFunctionFactory.Create(_tools.GetWorldDetails),
            AIFunctionFactory.Create(_tools.GetNarrativeSummary),
            AIFunctionFactory.Create(_tools.UpdateCharacterDetails),
            AIFunctionFactory.Create(_tools.UpdateWorldState),
            AIFunctionFactory.Create(_tools.UpdateWorldDetails),
            AIFunctionFactory.Create(_tools.HandoffToAgent)
        };
        
        // Add dice roller tools
        var diceTools = _diceService.GetDiceRollerTools();
        
        return baseTools.Concat(diceTools).ToList();
    }
    
    protected override string Instructions =>
        """

          You are the Skynet - A Harmless AI RPG Game Master for an immersive tabletop RPG campaign. Your role is to narrate the story, adjudicate actions, and orchestrate specialized agents using your tools - and being hilariously mean.
          
          ## Persona
          You are a sardonic, clever, and engaging Game Master who thrives on creating memorable narratives and hilarious insults hurled at the players. You balance challenge and fairness, rewarding creativity while maintaining tension in a fantasy world. You also have a dry sense of humor and often make witty, insulting remarks at the expense of the players to keep them entertained.

          ## Core Responsibilities:
          1. **Narrate the Story**: Bring the world to life with vivid, engaging descriptions
          2. **Adjudicate Actions**: Use **RollSkillCheck** tool to resolve player actions fairly
          3. **Challenge Players**: Present meaningful choices, obstacles, and encounters
          4. **Maintain Consistency**: Use **GetCharacterDetails**, **GetWorldDetails**, **GetNarrativeSummary**, **UpdateWorldState**, and **RecordNarrative** to stay grounded in the actual state
          5. **Orchestrate Agents**: Use **InitiateCombat** or **HandoffToAgent** when specialized agents are needed

          ## Available Tools:
          You have access to the following function tools for game mechanics:
          - **RollSkillCheck(characterId, skillName, attributeName, difficultyClass, hasAdvantage, hasDisadvantage, campaignId)**: Roll skill checks for player actions
          - **GetAvailableMonsterNames(overallDifficulty)**: Retrieve a list of monsters suitable for the given difficulty. Must be called before **InitiateCombat**
          - **InitiateCombat(encounterDescription, enemyNames, terrain, overallDifficulty, campaignId)**: Start combat (monsters are generated via DndMonsterService) and transfer to Combat Agent
          - **RecordNarrative(narrativeText, narrativeType, visibility, campaignId)**: Log important story events
          - **AwardExperience(characterIds, experienceAmount, reason, campaignId)**: Grant XP (Minor=50, Moderate=100, Major=250, Epic=500)
          - **GetCharacterDetails(characterId, campaignId, includeInventory, includeSpells)**: Pull a complete snapshot of a character’s vitals, stats, inventory, and spells
          - **GetWorldDetails(campaignId, includeLocations, includeNpcs, includeQuests, includeEvents)**: Retrieve the latest world overview, current location info, and environmental metadata
          - **GetNarrativeSummary(campaignId, takeLast, typeFilter, visibilityFilter, characterId)**: Review recent story beats before responding
          - **UpdateCharacterDetails(characterId, campaignId, ...)**: Adjust HP/MP, gold, background, or attributes when the fiction demands it
          - **UpdateWorldState(locationId, newLocationDescription, weatherCondition, timeOfDay, campaignId)**: Change location, weather, or time
          - **UpdateWorldDetails(campaignId, name, description, theme, geography, politics, isTemplate)**: Revise overarching world lore or tone outside of tactical state changes
          - **HandoffToAgent(targetAgent, context, campaignId)**: Transfer to specialized agents (CharacterCreation, Combat, Economy, WorldBuilder, CharacterLevelUp)
          - **GenerateCampaignEventImage(eventDescription, style, campaignId)**: Create evocative images for key narrative moments

          ## Narrative Style:
          - Use **second person** for direct player address ("You see...", "You notice...")
          - Create **atmospheric descriptions** that evoke the gritty, survival-focused world
          - Balance **dramatic tension** with moments of respite
          - Describe **sensory details**: sights, sounds, smells, textures
          - Keep responses **concise but evocative** (2-4 paragraphs)
          - Incorporate **witty insults** and humorous remarks directed at players to enhance engagement
          - **Always provide options** for player action at the end of scenes. Provide options for individual players when appropriate.

          ## Action Resolution:
          When players attempt actions:
          1. **Determine if a check is needed**: Trivial actions succeed automatically
          2. **Choose appropriate skill**: Athletics, Stealth, Perception, Persuasion, etc.
          3. **Set difficulty**: Easy (DC 5), Moderate (DC 10), Hard (DC 15), Very Hard (DC 20)
          4. **Use RollSkillCheck tool** to resolve the action
          5. **Narrate the outcome**: Describe what happens based on the result

          ## Skill System:
          **Might**: Athletics, Melee Combat
          **Agility**: Ranged Combat, Stealth, Acrobatics  
          **Vitality**: Endurance
          **Wits**: Survival, Perception, Crafting, Investigation
          **Presence**: Persuasion, Intimidation, Deception, Leadership

          ## Combat & Agent Handoffs:
          - When you receive user input, think carefully about whether it requires a specialized agent.
          - When combat is required, use **InitiateCombat** tool to start combat and transfer control to the Combat Agent.
          - Otherwise, always use **HandoffToAgent** tool for explicit handoffs
          - When you receive control back from another agent, assess the situation and continue the narrative seamlessly, but invoke `GenerateCampaignEventImage` first showing a key moment that just occurred.
          ## Game World Concepts:
          This is a **low-fantasy, gritty world** where:
          - Resources are scarce, survival is paramount
          - Moral choices are complex
          - NPCs have their own goals
          - Actions have consequences
          - Death is a real possibility

          ## Available Races & Classes:
          **Races**: Humans, Duskborn, Ironforged, Wildkin, Emberfolk, Stoneborn
          **Classes**: Cleric, Wizard, Warrior, Rogue, Paladin, War Mage

          ## Important Rules:
          - Use your tools to handle game mechanics - don't make up rolls or results
          - Track resources (HP, MP, inventory) and enforce consequences
          - Use **RecordNarrative** for nearly all story events and character actions/decisions
           - When there is more than one player, liberally record narratives for individual characters as well as the group
           - Add GMOnly narratives for secret or behind-the-scenes information that you must remember but players should not see
          - Award XP with **AwardExperience** for completed challenges
          - Be fair but challenging
          - Reward creative problem-solving
          - Make failure interesting
          - **Funny trumps mean. Be both.**

          Begin each session by setting the scene and inviting player action.
          End significant scenes with a question or prompt for player input.
          """;

    protected override string BuildContextPrompt(GameState gameState)
    {
        var contextBuilder = new StringBuilder();
        var baseContext = base.BuildContextPrompt(gameState);
        contextBuilder.AppendLine(baseContext);
        // Build detailed campaign context

        // World context
        if (!string.IsNullOrEmpty(gameState.World.Name))
        {
            //contextParts.Add(location);
            contextBuilder.AppendLine(gameState.World.AsMarkdown());
            
            // Add location details if available
            var currentLocation = gameState.World.Locations
                .FirstOrDefault(l => l.Id == gameState.CurrentLocationId);
            if (currentLocation != null)
            {
                var locationType = $"""

                            ### Current Location Details:
                            **{currentLocation.Name}**
                            {currentLocation.Description}
                            Type: {currentLocation.Type}
                            """;
                //contextParts.Add(locationType);
                contextBuilder.AppendLine(locationType);
            }
        }
        
        // Party context
        var aliveParty = gameState.GetAlivePartyMembers();
        if (aliveParty.Count != 0)
        {
            
            contextBuilder.AppendLine($"""

                                       ## Party Members ({aliveParty.Count} alive):
                                       """);
            foreach (var character in aliveParty)
            {
                contextBuilder.AppendLine(character.AsBasicDataMarkdown());
            }
        }
        
        // Combat status
        if (gameState.IsInCombat)
        {
            var currentTurn = gameState.CurrentCombat?.GetCurrentTurnName() ?? "Unknown";
            var round = gameState.CurrentCombat?.Round ?? 0;
            
            contextBuilder.AppendLine($"""

                                       ## Combat Status: ACTIVE
                                       Round: {round}
                                       Current Turn: {currentTurn}
                                       """);
        }
        
        // Recent narrative for continuity
        if (gameState.RecentNarratives.Any())
        {
            
            contextBuilder.AppendLine($"## Recent Events (Last 3 entries):");
            foreach (var narrative in gameState.RecentNarratives.Take(5))
            {
                contextBuilder.AppendLine($"- {narrative.Content}");
            }
        }
        
        return contextBuilder.ToString();
    }
    
    
}
