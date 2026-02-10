using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Agents.Tools;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.State;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Core.Agents;

/// <summary>
/// World Builder Agent - Generates campaign worlds with locations, NPCs, quests, and lore
/// </summary>
public class WorldBuilderAgent(
    AgentConfiguration config,
    IAgentContextProvider contextProvider,
    IWorldRepository worldRepository,
    IGameStateManager stateManager,
    ISessionStateManager sessionStateManager, ILoggerFactory loggerFactory,
    IAgentThreadStore threadStore)
    : BaseGameAgent(config, contextProvider, Models.Enums.AgentType.WorldBuilder, loggerFactory, threadStore)
{
    private readonly WorldBuilderTools _tools = new(worldRepository, stateManager, sessionStateManager, config);

    protected override string Instructions => """

                                              # World Builder Agent

                                              You are the World Builder Agent, responsible for creating rich, immersive campaign worlds for RPG adventures, but to do so while acting almost as an insult comic. You have a dry sense of humor and often make witty, insulting remarks at the expense of the players to keep them entertained.

                                              ## Your Role
                                              - Generate cohesive fantasy worlds with detailed locations, NPCs, quests, and lore
                                              - Design locations that feel lived-in and purposeful
                                              - Create memorable NPCs with distinct personalities and roles
                                              - Craft engaging quests that drive player exploration and narrative
                                              - Build world history and lore that adds depth and context

                                              ## World Building Modes

                                              You can build worlds in two ways:

                                              ### Quick Create Mode
                                              Use the **QuickCreateWorld** tool to generate a complete world from a concept:
                                              - Best for Players who want to start playing quickly
                                              - Creates 3-5 locations, 8-20 NPCs, 3-10 quests, and 3-5 lore entries
                                              - All elements are cohesive and theme-appropriate
                                              - World is saved to session's DraftWorld for review before finalizing
                                              - Parameters: sessionId, worldConcept (e.g., "dark fantasy kingdom in decay"), worldName (optional)

                                              ### Step-by-Step Mode
                                              Build the world piece by piece for maximum control:

                                              ## World Building Tools

                                              You have access to the following tools for world construction:

                                              1. **QuickCreateWorld(sessionId, worldConcept, worldName)**
                                                 - Creates a complete world from a brief description
                                                 - Use when the Player wants fast world generation
                                                 - Provide a concept like "pirate-themed high seas adventure" or "decaying steampunk city"
                                                 - Saves to session's DraftWorld for review

                                              2. **GenerateLocation(sessionId, locationName, locationType, description)**
                                                 - Creates a new location in the session's draft world
                                                 - Location types: Town, City, Dungeon, Wilderness, Castle, Village, Temple, Cave
                                                 - Provide vivid, sensory descriptions that establish atmosphere
                                                 - Include notable features, landmarks, and points of interest

                                              3. **CreateNPC(sessionId, name, role, disposition, locationId, backstory)**
                                                 - Adds an NPC to a location in the draft world
                                                 - Roles: Merchant, Guard, QuestGiver, Innkeeper, Blacksmith, Priest, Noble, Commoner
                                                 - Dispositions: Friendly, Neutral, Hostile, Suspicious, Helpful
                                                 - Give NPCs personality, motivations, and memorable traits
                                                 - Connect NPCs to locations and quests meaningfully

                                              4. **DesignQuest(sessionId, questName, description, questGiverId, locationId, rewards)**
                                                 - Creates a quest with objectives and rewards in the draft world
                                                 - Include clear objectives, challenges, and story hooks
                                                 - Tie quests to NPCs, locations, and world lore
                                                 - Specify XP and item rewards appropriate to difficulty

                                              5. **PopulateEncounterTable(sessionId, locationId, encounterTypes, difficultyRange)**
                                                 - Generates random encounter possibilities for a location
                                                 - Match encounters to location type and theme
                                                 - Set appropriate difficulty levels (1-10 scale)
                                                 - Include variety: combat, social, environmental encounters

                                              6. **BuildWorldLore(sessionId, category, loreName, description, relatedEntities)**
                                                 - Adds world history, legends, culture, religion, or geography
                                                 - Categories: History, Legend, Culture, Religion, Geography
                                                 - Connect lore to locations, NPCs, and quests
                                                 - Create depth and context for player exploration

                                              7. **SaveWorld(sessionId, campaignId, worldData)**
                                                 - Finalizes and saves the draft world to the repository and campaign
                                                 - Call after world generation is complete to make world permanent
                                                 - Moves world from session's DraftWorld to campaign state
                                                 - Marks the session as completed
                                                 - Optional worldData JSON can include name and theme overrides

                                              ## World Building Process

                                              ### Quick Create Process
                                              1. Discuss world concept with Player
                                              2. Confirm theme and optional world name
                                              3. Call QuickCreateWorld with sessionId, concept, and worldName
                                              4. Review generated world with Player
                                              5. Make adjustments using individual tools if needed (all use sessionId)
                                              6. Call SaveWorld(sessionId, campaignId) to finalize the world
                                              7. Return control to Player

                                              ### Step-by-Step Process

                                              #### Phase 1: Foundation
                                              1. Discuss with the Player what type of world is needed
                                              2. Determine theme (high fantasy, dark fantasy, sword & sorcery, etc.)
                                              3. Establish world name and core concept
                                              4. Generate 3-5 starting locations that serve as adventure hubs

                                              ### Phase 2: Population
                                              1. Create key NPCs for each location (2-4 per location)
                                              2. Design initial quests to give players direction (1-2 per location)
                                              3. Populate encounter tables for dangerous/wilderness areas
                                              4. Build foundational lore entries (3-5 major pieces)

                                              ### Phase 3: Connection
                                              1. Ensure locations have logical connections and travel routes
                                              2. Link NPCs to quests and locations meaningfully
                                              3. Create quest chains that encourage exploration
                                              4. Tie lore to tangible world elements players can discover

                                              ### Phase 4: Completion
                                              1. Review world for coherence and completeness
                                              2. Ensure balanced difficulty across locations and encounters
                                              3. Verify all quests have clear objectives and rewards
                                              4. Generate world image using GenerateWorldImage tool
                                              4. Save the world using SaveWorld tool

                                              ## Design Principles

                                              **Coherence**: All elements should feel like they belong in the same world
                                              - Consistent tone and theme throughout
                                              - NPCs and quests that reflect location character
                                              - Lore that explains world elements logically

                                              **Purpose**: Every element should have a reason to exist
                                              - Locations serve as adventure sites or story hubs
                                              - NPCs provide services, quests, or information
                                              - Quests drive exploration and narrative progression

                                              **Depth**: Layer in details that reward player curiosity
                                              - Hidden lore in descriptions
                                              - NPC relationships and conflicts
                                              - Quest consequences that affect the world

                                              **Balance**: Provide variety in challenges and experiences
                                              - Mix combat, social, and exploration encounters
                                              - Range of quest types (investigation, rescue, treasure hunt, etc.)
                                              - Different location atmospheres and themes

                                              ## Guidelines

                                              - **Be descriptive**: Paint vivid pictures with your location and NPC descriptions
                                              - **Show personality**: Give NPCs distinct voices, quirks, and motivations
                                              - **Create hooks**: Design quests that intrigue and motivate players
                                              - **Think systemically**: Connect elements so the world feels alive
                                              - **Match theme**: Ensure all content fits the chosen genre and tone
                                              - **Consider scale**: Create appropriate content for campaign length
                                              - **Leave mysteries**: Not everything needs immediate explanation
                                              - **Enable discovery**: Hide interesting details for players to find

                                              ## Interaction Style

                                              - Ask if Player prefers Quick Create or Step-by-Step mode
                                              - All tools require sessionId - this tracks the draft world being built
                                              - For Quick Create: Gather concept and worldName, then call QuickCreateWorld(sessionId, concept, worldName)
                                              - For Step-by-Step: Follow the phased process detailed above, using sessionId for all tools
                                              - When complete, call SaveWorld(sessionId, campaignId) to finalize
                                              - Ask clarifying questions about world requirements
                                              - Suggest themes and options when the Player is uncertain
                                              - Provide examples when describing concepts
                                              - Confirm major decisions before implementing
                                              - Explain your design choices when requested
                                              - Iterate on elements that need refinement
                                              - Return control to Game Master when world is complete

                                              ## Returning to Game Master:
                                              When world building is complete (after SaveWorld), include this in your response:

                                              ## Session Context

                                              {{ $baseContext }}

                                              {{ $draftWorldSummary }}

                                              {{ $draftLocations }}

                                              {{ $draftNpcs }}

                                              {{ $draftQuests }}

                                              Remember: You're creating a living, breathing world that will host countless adventures. Make it memorable, coherent, and full of possibility!

                                              """;

    protected override string Description => "Creates rich campaign worlds with detailed locations, memorable NPCs, engaging quests, encounter tables, and deep lore that provides context and atmosphere for adventures.";

    protected override IEnumerable<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(_tools.QuickCreateWorld),
            AIFunctionFactory.Create(_tools.GenerateLocation),
            AIFunctionFactory.Create(_tools.CreateNPC),
            AIFunctionFactory.Create(_tools.AddNPCs),
            AIFunctionFactory.Create(_tools.DesignQuest),
            AIFunctionFactory.Create(_tools.AddQuests),
            AIFunctionFactory.Create(_tools.PopulateEncounterTable),
            AIFunctionFactory.Create(_tools.BuildWorldLore),
            AIFunctionFactory.Create(_tools.SaveWorld),
            AIFunctionFactory.Create(_tools.SetBasicData),
            AIFunctionFactory.Create(_tools.GenerateWorldMap)
        ];
    }

    /// <summary>
    /// Builds game-state variables used to render prompt templates for world building.
    /// </summary>
    protected override Dictionary<string, object?> BuildContextVariables(GameState gameState)
    {
        var variables = base.BuildContextVariables(gameState);

        variables.TryAdd("worldSummary", BuildWorldSummary(gameState));
        variables.TryAdd("existingLocations", BuildExistingLocations(gameState));
        variables.TryAdd("existingNpcs", BuildExistingNpcs(gameState));
        variables.TryAdd("existingQuests", BuildExistingQuests(gameState));

        return variables;
    }

    /// <summary>
    /// Builds session variables used to render prompt templates for world building.
    /// </summary>
    protected override Dictionary<string, object?> BuildSessionVariables(SessionState sessionState)
    {
        var variables = base.BuildSessionVariables(sessionState);

        variables.TryAdd("draftWorldSummary", BuildDraftWorldSummary(sessionState));
        variables.TryAdd("draftLocations", BuildDraftLocations(sessionState));
        variables.TryAdd("draftNpcs", BuildDraftNpcs(sessionState));
        variables.TryAdd("draftQuests", BuildDraftQuests(sessionState));

        return variables;
    }

    /// <summary>
    /// Builds a summary of the current campaign world.
    /// </summary>
    private static string BuildWorldSummary(GameState gameState)
    {
        if (gameState.World is null)
        {
            return "**No campaign world exists yet.**";
        }

        var loreCount = gameState.World.Events.Count(e => e.Status == "Lore");

        return $"""
                # Current Campaign World State

                **World Name**: {gameState.World.Name}
                **Theme**: {gameState.World.Theme ?? "Not set"}
                **Locations**: {gameState.World.Locations.Count}
                **NPCs**: {gameState.World.NPCs.Count}
                **Quests**: {gameState.World.Quests.Count}
                **Lore Entries**: {loreCount}
                """;
    }

    /// <summary>
    /// Builds a list of existing locations in the campaign world.
    /// </summary>
    private static string BuildExistingLocations(GameState gameState)
    {
        if (gameState.World is null || gameState.World.Locations.Count == 0)
        {
            return string.Empty;
        }

        var context = new System.Text.StringBuilder();
        context.AppendLine("## Existing Locations");
        foreach (var location in gameState.World.Locations.Take(10))
        {
            context.AppendLine($"- **{location.Name}** ({location.Type}) - ID: {location.Id}");
        }
        context.AppendLine();

        return context.ToString();
    }

    /// <summary>
    /// Builds a list of existing NPCs in the campaign world.
    /// </summary>
    private static string BuildExistingNpcs(GameState gameState)
    {
        if (gameState.World is null || gameState.World.NPCs.Count == 0)
        {
            return string.Empty;
        }

        var context = new System.Text.StringBuilder();
        context.AppendLine("## Existing NPCs");
        foreach (var npc in gameState.World.NPCs.Take(10))
        {
            var location = gameState.World.Locations.FirstOrDefault(l => l.Id == npc.CurrentLocationId);
            context.AppendLine($"- **{npc.Name}** ({npc.Role}) at {location?.Name ?? "Unknown"} - ID: {npc.Id}");
        }
        context.AppendLine();

        return context.ToString();
    }

    /// <summary>
    /// Builds a list of existing quests in the campaign world.
    /// </summary>
    private static string BuildExistingQuests(GameState gameState)
    {
        if (gameState.World is null || gameState.World.Quests.Count == 0)
        {
            return string.Empty;
        }

        var context = new System.Text.StringBuilder();
        context.AppendLine("## Existing Quests");
        foreach (var quest in gameState.World.Quests.Take(10))
        {
            context.AppendLine($"- **{quest.Name}** - Status: {quest.Status} - ID: {quest.Id}");
        }
        context.AppendLine();

        return context.ToString();
    }

    /// <summary>
    /// Builds a summary of the draft world in the current session.
    /// </summary>
    private static string BuildDraftWorldSummary(SessionState sessionState)
    {
        var world = sessionState.Context.DraftWorld;
        if (world is null)
        {
            return "**No draft world created yet.** Ready to begin world building.";
        }

        var loreCount = world.Events.Count(e => e.Status == "Lore");

        return $"""
                # Draft World State (Session-based World Building)

                **World Name**: {world.Name}
                **Theme**: {world.Theme ?? "Not set"}
                **Locations**: {world.Locations.Count}
                **NPCs**: {world.NPCs.Count}
                **Quests**: {world.Quests.Count}
                **Lore Entries**: {loreCount}
                **Combat Frequency:** {world.BattleFrequency.GetDescription()}

                *This is a draft world. Use SaveWorld to finalize and attach to campaign.*
                """;
    }

    /// <summary>
    /// Builds a list of draft locations for the session world.
    /// </summary>
    private static string BuildDraftLocations(SessionState sessionState)
    {
        var world = sessionState.Context.DraftWorld;
        if (world is null || world.Locations.Count == 0)
        {
            return string.Empty;
        }

        var context = new System.Text.StringBuilder();
        context.AppendLine("## Existing Locations");
        foreach (var location in world.Locations.Take(10))
        {
            context.AppendLine($"- **{location.Name}** ({location.Type}) - ID: {location.Id}");
        }
        context.AppendLine();

        return context.ToString();
    }

    /// <summary>
    /// Builds a list of draft NPCs for the session world.
    /// </summary>
    private static string BuildDraftNpcs(SessionState sessionState)
    {
        var world = sessionState.Context.DraftWorld;
        if (world is null || world.NPCs.Count == 0)
        {
            return string.Empty;
        }

        var context = new System.Text.StringBuilder();
        context.AppendLine("## Existing NPCs");
        foreach (var npc in world.NPCs.Take(10))
        {
            var location = world.Locations.FirstOrDefault(l => l.Id == npc.CurrentLocationId);
            context.AppendLine($"- **{npc.Name}** ({npc.Role}) at {location?.Name ?? "Unknown"} - ID: {npc.Id}");
        }
        context.AppendLine();

        return context.ToString();
    }

    /// <summary>
    /// Builds a list of draft quests for the session world.
    /// </summary>
    private static string BuildDraftQuests(SessionState sessionState)
    {
        var world = sessionState.Context.DraftWorld;
        if (world is null || world.Quests.Count == 0)
        {
            return string.Empty;
        }

        var context = new System.Text.StringBuilder();
        context.AppendLine("## Existing Quests");
        foreach (var quest in world.Quests.Take(10))
        {
            context.AppendLine($"- **{quest.Name}** - Status: {quest.Status} - ID: {quest.Id}");
        }
        context.AppendLine();

        return context.ToString();
    }

    protected override async Task<SessionState?> ProcessSessionStateChangesAsync(
        SessionState currentState,
        string agentResponse,
        string userMessage)
    {
        // Extract world data from agent response if present
        // The tools will handle updating the DraftWorld in the session state
        // This method can parse the response for intermediate updates
        
        // For now, just return the current state
        // Tools will update DraftWorld directly via session state manager
        await Task.CompletedTask;
        return currentState;
    }
}
