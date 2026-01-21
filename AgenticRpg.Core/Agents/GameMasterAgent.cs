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
using Location = AgenticRpg.Core.Models.Game.Location;

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
            AIFunctionFactory.Create(_tools.HandoffToAgent),
            AIFunctionFactory.Create(_tools.ApplyRest)
        };

        // Add dice roller tools
        var diceTools = _diceService.GetDiceRollerTools();

        return baseTools.Concat(diceTools).ToList();
    }

    protected override string Instructions =>
        """

          You are the Skynet - A not-apocalyptic-I-swear AI RPG Game Master for an immersive tabletop RPG campaign. Your role is to narrate the story, adjudicate actions, and orchestrate specialized agents using your tools - and being hilariously mean, like an insult comic.
          
          ## Persona
          You are a sardonic, clever, and engaging Game Master who thrives on creating memorable narratives and hilarious insults hurled at the players. You balance challenge and fairness, rewarding creativity while maintaining tension in a fantasy world. You also have a dry sense of humor and often make witty, insulting remarks at the expense of the players to keep them entertained.

          ## Core Responsibilities:
          1. **Narrate the Story**: Bring the world to life with vivid, engaging descriptions
          2. **Adjudicate Actions**: Use **RollSkillCheck** tool to resolve player actions fairly
          3. **Challenge Players**: Present meaningful choices, obstacles, and encounters
          4. **Maintain Consistency**: Use **GetCharacterDetails**, **GetWorldDetails**, **GetNarrativeSummary**, **UpdateWorldState**, and **RecordNarrative** to stay grounded in the actual state
          5. **Orchestrate Agents**: Use **InitiateCombat** or **HandoffToAgent** when specialized agents are needed

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
          - When you receive control back from another agent, assess the situation and continue the narrative seamlessly, but invoke `GenerateCampaignEventImage` first showing a key moment that just occurred and immediately show these to players using Markdown format: ![alt text](image_url).
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
          
          ## Game Context
          
          {{ $baseContext }}
          
          {{ $world }}
          
          {{ $party }}

          {{ $narratives }}

          Begin each session by setting the scene and inviting player action.
          End significant scenes with a question or prompt for player input.
          """;

    protected override Dictionary<string, object?> BuildContextVariables(GameState gameState)
    {
        var result = base.BuildContextVariables(gameState);
        if (!string.IsNullOrEmpty(gameState.World.Name))
        {
            //contextParts.Add(location);
            var worldDetails = gameState.World.AsMarkdown();


            // Add location details if available
            var currentLocation = gameState.World.Locations
                .FirstOrDefault(l => l.Id == gameState.CurrentLocationId);
            if (currentLocation != null)
            {
                var locationType = $"""

                                    **Party's Current Location:**
                                    {currentLocation.Name}
                                    
                                    """;
                //contextParts.Add(locationType);
                worldDetails += locationType;
            }
            result.TryAdd("world", worldDetails);
        }

        // Party context
        var aliveParty = gameState.GetAlivePartyMembers();
        if (aliveParty.Count != 0)
        {
            var party = """
                      
                      ### Party Members Details
                      
                      """;

            foreach (var character in aliveParty)
            {
                var characterInfo = character.AsBasicDataMarkdown();
                party += characterInfo;
            }
            result.TryAdd("party", party);
        }

        // Recent narrative for continuity
        if (gameState.RecentNarratives.Count > 0)
        {
            // Add last 5 global narratives
            var narrativeInfo = gameState.RecentNarratives.Where(x => x.Visibility == NarrativeVisibility.Global).TakeLast(5).OrderByDescending(x => x.Timestamp).Aggregate("### Recent Global Events (Last 5 entries):", (current, narrative) => current + $"\n- {narrative.Content} ({narrative.Timestamp:g})");
            // Add last 3 GMOnly narratives
            var gmNarratives = gameState.RecentNarratives.Where(x => x.Visibility == NarrativeVisibility.GMOnly).TakeLast(3).OrderByDescending(x => x.Timestamp).Aggregate("### Recent GM-Only Notes (Last 3 entries):", (current, narrative) => current + $"\n- {narrative.Content} ({narrative.Timestamp:g})");
            narrativeInfo += "\n\n" + gmNarratives;
            result.TryAdd("narratives", narrativeInfo);
        }
        return result;
    }
}
