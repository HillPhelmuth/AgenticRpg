using AgenticRpg.Core.Agents.Llms;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Services;
using AgenticRpg.Core.State;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTelemetry;
using OpenTelemetry.Trace;
using System.ClientModel;
using System.ComponentModel;
using System.Text.Json;
#pragma warning disable OPENAI001

namespace AgenticRpg.Core.Agents.Tools;

/// <summary>
/// World building tools for the WorldBuilderAgent to create locations, NPCs, quests, and lore.
/// </summary>
public class WorldBuilderTools(
    IWorldRepository worldRepository,
    IGameStateManager stateManager, ISessionStateManager sessionStateManager,
    AgentConfiguration configuration)
{
    private const string QuickCreateInstructions = """
                                                   Using the provided world concept, create a complete campaign world.

                                                   ## World Requirements

                                                   #### World Theme and Setting
                                                   - Determine the fantasy genre (High Fantasy, Dark Fantasy, Sword & Sorcery, Urban Fantasy, etc.)
                                                   - Create a cohesive world name that fits the theme
                                                   - Establish the overall tone and atmosphere

                                                   #### Locations (Create 3-5 starting locations)
                                                   - At least one major settlement (Town or City) as a safe hub
                                                   - At least one adventure location (Dungeon, Cave, Ruins, Wilderness)
                                                   - Diverse location types that encourage exploration
                                                   - Each location should have:
                                                     * Vivid, sensory description
                                                     * Notable features and landmarks
                                                     * Clear purpose in the world

                                                   #### NPCs (Create 2-4 NPCs per location)
                                                   - Include variety of roles: Merchant, Guard, QuestGiver, Innkeeper, Blacksmith, Priest, Noble, Commoner
                                                   - Mix of dispositions: Friendly, Neutral, Hostile, Suspicious, Helpful
                                                   - Each NPC should have:
                                                     * Distinct personality and voice
                                                     * Clear role in the location
                                                     * Backstory that connects to world
                                                     * Motivations and goals

                                                   #### Quests (Create 1-2 starter quests per major location)
                                                   - Clear objectives and story hooks
                                                   - Appropriate difficulty for starting characters (levels 1-3)
                                                   - Connect quests to NPCs and locations
                                                   - Include varied types: investigation, rescue, treasure hunt, combat, social
                                                   - Specify rewards:
                                                     * XP appropriate to difficulty (100-500 XP for starter quests)
                                                     * Gold rewards (100-5000 gold, depending on difficulty)
                                                     * Possible item rewards

                                                   #### Lore Entries (Create 3-5 foundational pieces)
                                                   - Categories to cover: History, Legend, Culture, Religion, Geography
                                                   - Create depth and context for the world
                                                   - Connect lore to tangible locations and NPCs
                                                   - Leave some mysteries unexplained for discovery

                                                   #### Encounter Tables (For dangerous locations)
                                                   - Match encounters to location type and theme
                                                   - Set appropriate difficulty (1-5 for starter areas)
                                                   - Include variety: combat, social, environmental
                                                   - 2-4 encounter types per dangerous location
                                                   
                                                   #### Combat Frequency
                                                   - Define how often combat encounters occur in the location

                                                   ## Design Principles
                                                   - **Coherence**: All elements should feel like they belong together
                                                   - **Purpose**: Every element serves the adventure and story
                                                   - **Depth**: Layer in details that reward curiosity
                                                   - **Balance**: Mix of combat, exploration, and social encounters based on Combat Frequency
                                                   - **Theme consistency**: All content fits the chosen genre and tone

                                                   ---
                                                   """;

    [Description("Generates a new location in the world. Parameters: sessionId (world building session ID), locationName (name of location), locationType (Town/City/Dungeon/Wilderness/Castle/Village/Temple/Cave), description (detailed description of location). Returns JSON with location details and ID.")]
    public async Task<string> GenerateLocation(
        [Description("The unique session ID for this world building session. This tracks the world being created.")] string sessionId,
        [Description("The name of the location to create (e.g., 'Silverwood', 'The Dark Caverns', 'Port Meridian').")] string locationName,
        [Description("The type/category of this location. Valid types: Town, City, Dungeon, Wilderness, Castle, Village, Temple, Cave, Ruins, Forest, Mountain.")] LocationType locationType,
        [Description("A detailed narrative description of the location, including its appearance, atmosphere, inhabitants, and notable features.")] string description)
    {
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session == null)
        {
            return JsonSerializer.Serialize(new GenerateLocationResult
            {
                Success = false,
                Message = "",
                Error = "Session not found"
            });
        }

        if (session.Context.DraftWorld == null)
        {
            session.Context.DraftWorld = new World
            {
                Id = Guid.NewGuid().ToString(),
                Name = "New World",
                CreatedAt = DateTime.UtcNow
            };
        }

        var location = new Location
        {
            Id = Guid.NewGuid().ToString(),
            Name = locationName,
            Type = locationType.ToString(),
            Description = description
        };

        session.Context.DraftWorld.Locations.Add(location);
        session.LastUpdatedAt = DateTime.UtcNow;
        await sessionStateManager.UpdateSessionStateAsync(session);

        return JsonSerializer.Serialize(new GenerateLocationResult
        {
            Success = true,
            Location = location,
            Message = $"Location '{locationName}' created successfully as a {locationType}."
        });
    }

    [Description("Creates an NPC in the world. Parameters: sessionId (world building session ID), name (NPC name), role (Merchant/Guard/QuestGiver/Innkeeper/Blacksmith/Priest/Noble/Commoner), disposition (Friendly/Neutral/Hostile/Suspicious/Helpful), locationId (where NPC is located), backstory (NPC background and personality). Returns JSON with NPC details and ID.")]
    public async Task<string> CreateNPC(
        [Description("The unique session ID for this world building session.")] string sessionId,
        [Description("The name of the NPC to create (e.g., 'Elara the Wise', 'Grunk the Blacksmith').")] string name,
        [Description("The NPC's primary role or profession. Common roles: Merchant, Guard, QuestGiver, Innkeeper, Blacksmith, Priest, Noble, Commoner, Scholar.")] NPCRole role,
        [Description("The NPC's default attitude toward players. Values: Friendly, Neutral, Hostile, Suspicious, Helpful, Indifferent.")] NPCDisposition disposition,
        [Description("The unique ID of the location where this NPC currently resides.")] string locationId,
        [Description("A detailed backstory, personality description, motivations, and relevant history for this NPC. This enriches roleplay interactions.")] string backstory)
    {
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftWorld == null)
        {
            return JsonSerializer.Serialize(new CreateNPCResult
            {
                Success = false,
                Message = "",
                Error = "Session or draft world not found"
            });
        }

        var location = session.Context.DraftWorld.Locations.FirstOrDefault(l => l.Id == locationId);
        if (location == null)
        {
            return JsonSerializer.Serialize(new CreateNPCResult
            {
                Success = false,
                Message = "",
                Error = $"Location with ID '{locationId}' not found"
            });
        }

        var npc = new NPC
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Role = role.ToString(),
            Disposition = disposition,
            CurrentLocationId = locationId,
            Description = backstory  // Store backstory in Description field
        };

        session.Context.DraftWorld.NPCs.Add(npc);
        session.LastUpdatedAt = DateTime.UtcNow;
        await sessionStateManager.UpdateSessionStateAsync(session);

        return JsonSerializer.Serialize(new CreateNPCResult
        {
            Success = true,
            NPC = npc,
            Message = $"NPC '{name}' created as {role} with {disposition} disposition at {location.Name}."
        });
    }

    [Description("Designs a quest with objectives and rewards. Parameters: sessionId (world building session ID), questName (quest title), description (quest details and objectives), questGiverId (NPC who gives quest), locationId (where quest starts), rewards (XP and items awarded). Returns JSON with quest details and ID.")]
    public async Task<string> DesignQuest(
        [Description("The unique session ID for this world building session.")] string sessionId,
        [Description("The title of the quest (e.g., 'Rescue the Missing Villagers', 'The Tomb of Ancients').")] string questName,
        [Description("A detailed description of the quest including objectives, story context, and what the characters need to accomplish.")] string description,
        [Description("The unique ID of the NPC who offers this quest to the players.")] string questGiverId,
        [Description("The unique ID of the location where this quest begins or is offered.")] string locationId,
        [Description("A description of rewards for completing the quest, including XP amounts and item rewards (e.g., '500 XP, Magic Sword, 100 gold').")] string rewards)
    {
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftWorld == null)
        {
            return JsonSerializer.Serialize(new DesignQuestResult
            {
                Success = false,
                Message = "",
                Error = "Session or draft world not found"
            });
        }

        var questGiver = session.Context.DraftWorld.NPCs.FirstOrDefault(n => n.Id == questGiverId);
        if (questGiver == null)
        {
            return JsonSerializer.Serialize(new DesignQuestResult
            {
                Success = false,
                Message = "",
                Error = $"Quest giver NPC with ID '{questGiverId}' not found"
            });
        }

        var location = session.Context.DraftWorld.Locations.FirstOrDefault(l => l.Id == locationId);
        if (location == null)
        {
            return JsonSerializer.Serialize(new DesignQuestResult
            {
                Success = false,
                Message = "",
                Error = $"Location with ID '{locationId}' not found"
            });
        }

        var quest = new Quest
        {
            Id = Guid.NewGuid().ToString(),
            Name = questName,
            Description = description,
            QuestGiver = questGiverId,  // Store as string ID
            LocationId = locationId,
            Status = QuestStatus.Available,
            Reward = new QuestReward()  // Initialize empty reward, parse rewards string if needed
        };

        session.Context.DraftWorld.Quests.Add(quest);
        session.LastUpdatedAt = DateTime.UtcNow;
        await sessionStateManager.UpdateSessionStateAsync(session);

        return JsonSerializer.Serialize(new DesignQuestResult
        {
            Success = true,
            Quest = quest,
            Message = $"Quest '{questName}' created. Given by {questGiver.Name} at {location.Name}."
        });
    }

    [Description("Populates random encounter table for a location. Parameters: sessionId (world building session ID), locationId (location to add encounters to), encounterTypes (array of encounter descriptions like 'Bandits', 'Wild Animals', 'Undead'), difficultyRange (1-10 scale for encounter difficulty). Returns JSON with encounter table entries.")]
    public async Task<string> PopulateEncounterTable(
        [Description("The unique session ID for this world building session.")] string sessionId,
        [Description("The unique ID of the location to add random encounters to.")] string locationId,
        [Description("Array of encounter type names or descriptions (e.g., ['Bandits', 'Wild Wolves', 'Wandering Undead', 'Hostile Merchants']).")] string[] encounterTypes,
        [Description("The difficulty level of encounters on a scale of 1-10, where 1 is trivial and 10 is deadly. This helps scale encounters to party level.")] int difficultyRange)
    {
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftWorld == null)
        {
            return JsonSerializer.Serialize(new PopulateEncounterTableResult
            {
                Success = false,
                Encounters = [],
                Message = "",
                Error = "Session or draft world not found"
            });
        }

        var location = session.Context.DraftWorld.Locations.FirstOrDefault(l => l.Id == locationId);
        if (location == null)
        {
            return JsonSerializer.Serialize(new PopulateEncounterTableResult
            {
                Success = false,
                Encounters = [],
                Message = "",
                Error = $"Location with ID '{locationId}' not found"
            });
        }

        var totalProbability = 100;
        var probabilityPerEncounter = totalProbability / encounterTypes.Length;

        var encounters = encounterTypes.Select(encounterType => new EncounterEntry { EncounterType = encounterType, Description = $"{encounterType} encounter at {location.Name}", DifficultyLevel = difficultyRange, Probability = probabilityPerEncounter }).ToList();

        // Store encounter table in location Tags (simplified approach)
        location.Tags.Add($"Encounters:{JsonSerializer.Serialize(encounters)}");
        session.LastUpdatedAt = DateTime.UtcNow;
        await sessionStateManager.UpdateSessionStateAsync(session);

        return JsonSerializer.Serialize(new PopulateEncounterTableResult
        {
            Success = true,
            LocationId = locationId,
            Encounters = encounters,
            Message = $"Encounter table populated for {location.Name} with {encounters.Count} encounter types."
        });
    }

    [Description("Adds world lore and history entries. Parameters: sessionId (world building session ID), category (History/Legend/Culture/Religion/Geography), loreName (title of lore entry), description (detailed lore content), relatedEntities (array of related location/NPC IDs). Returns JSON with lore entry details and ID.")]
    public async Task<string> BuildWorldLore(
        [Description("The unique session ID for this world building session.")] string sessionId,
        [Description("The category of lore being added. Valid categories: History, Legend, Culture, Religion, Geography, Politics, Magic.")] LoreCategory category,
        [Description("The title or name of this lore entry (e.g., 'The Great War of the Ancients', 'The Legend of the First King').")] string loreName,
        [Description("The full detailed lore content, including historical events, cultural information, or mythological stories.")] string description,
        [Description("Array of related entity IDs (locations or NPCs) that connect to this lore entry.")] string[] relatedEntities)
    {
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftWorld == null)
        {
            return JsonSerializer.Serialize(new BuildWorldLoreResult
            {
                Success = false,
                Message = "",
                Error = "Session or draft world not found"
            });
        }

        // Use WorldEvent to store lore entries
        var loreEvent = new WorldEvent
        {
            Id = Guid.NewGuid().ToString(),
            Name = loreName,
            Description = $"[{category}] {description}",
            Status = "Lore",
            StartTime = DateTime.UtcNow,
            Tags = [category.ToString()],
            Notes = string.Join(", ", relatedEntities)
        };

        session.Context.DraftWorld.Events.Add(loreEvent);
        session.LastUpdatedAt = DateTime.UtcNow;
        await sessionStateManager.UpdateSessionStateAsync(session);

        return JsonSerializer.Serialize(new BuildWorldLoreResult
        {
            Success = true,
            LoreEntry = loreEvent,
            Message = $"Lore entry '{loreName}' added to {category} category."
        });
    }

    [Description("Creates a complete campaign world from a brief description or concept. Use this when the Game Master wants quick world creation without step-by-step building. Parameters: sessionId (world building session ID), worldConcept (brief description like 'dark fantasy kingdom in decay' or 'high seas adventure world'), worldName (optional), combatFrequency (optional). Returns fully created world saved to session's DraftWorld.")]
    public async Task<string> QuickCreateWorld(
        [Description("The unique session ID for this world building session.")] string sessionId,
        [Description("A description of the desired world concept, theme, and setting based on the Game Master's preferences.")] string worldConcept,
        [Description("Optional world name. If not provided, a default name will be generated based on the concept.")] string? worldName = null, 
        [Description("The frequency of combat encounters in the world.")] BattleFrequency combatFrequency = BattleFrequency.Medium)
    {
        try
        {
            var session = await sessionStateManager.GetSessionStateAsync(sessionId);
            if (session == null)
            {
                return JsonSerializer.Serialize(new QuickCreateWorldResult
                {
                    Success = false,
                    World = null,
                    Message = "Session not found"
                });
            }

            var client = new OpenAIClient(new ApiKeyCredential(configuration.OpenRouterApiKey),
                new OpenAIClientOptions() { Endpoint = new Uri(configuration.OpenRouterEndpoint) });
            var goalInstructions =
                $"""
                 ## Goal

                 Create a complete campaign world using the concept as guidance.
                 **World Concept:** 
                 {worldConcept}
                 **World Name:** {worldName ?? "(generate a fitting name)"}
                 **Combat Frequency:** {combatFrequency.ToString()} - {combatFrequency.GetDescription()}
                 """;
            
            var chatClient = client.GetChatClient("openai/gpt-oss-120b").AsIChatClient();
            var quickCreateAgent = chatClient.CreateAIAgent(
                options: new ChatClientAgentOptions()
                {
                    
                    Name = "Quick World Create Agent",
                    ChatOptions = new ChatOptions()
                    {
                        Instructions = $"{QuickCreateInstructions}\n{goalInstructions}",
                        ResponseFormat = ChatResponseFormat.ForJsonSchema<World>(),
                        RawRepresentationFactory = _ => new OpenRouterChatCompletionOptions()
                        {
                            ReasoningEffortLevel = "high",
                            Provider = new Provider() { Sort = "throughput" },
                        }
                    }
                });

            var response = await quickCreateAgent.RunAsync<World>();
            var world = response.Result;
            Console.WriteLine(
                $"World Created:\n\n{JsonSerializer.Serialize(world, new JsonSerializerOptions { WriteIndented = true })}");
            // Set world properties
            world.Id = Guid.NewGuid().ToString();
            world.CreatedAt = DateTime.UtcNow;
            world.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(worldName))
            {
                world.Name = worldName;
            }

            // Ensure all child entities have IDs
            foreach (var location in world.Locations.Where(location => string.IsNullOrEmpty(location.Id)))
            {
                location.Id = Guid.NewGuid().ToString();
            }

            foreach (var npc in world.NPCs.Where(npc => string.IsNullOrEmpty(npc.Id)))
            {
                npc.Id = Guid.NewGuid().ToString();
            }

            foreach (var quest in world.Quests.Where(quest => string.IsNullOrEmpty(quest.Id)))
            {
                quest.Id = Guid.NewGuid().ToString();
            }

            foreach (var worldEvent in world.Events.Where(worldEvent => string.IsNullOrEmpty(worldEvent.Id)))
            {
                worldEvent.Id = Guid.NewGuid().ToString();
            }

            // Save to session's DraftWorld
            session.Context.DraftWorld = world;
            session.LastUpdatedAt = DateTime.UtcNow;
            await sessionStateManager.UpdateSessionStateAsync(session);

            var loreCount = world.Events.Count(e => e.Status == "Lore");

            return JsonSerializer.Serialize(new QuickCreateWorldResult
            {
                Success = true,
                World = world,
                Message =
                    $"World '{world.Name}' created successfully with {world.Locations.Count} locations, {world.NPCs.Count} NPCs, {world.Quests.Count} quests, and {loreCount} lore entries."
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return JsonSerializer.Serialize(new QuickCreateWorldResult
            {
                Error = ex.ToString(),
                Success = false,
                Message = $"Error creating world: {ex.Message}"
            });
        }
    }
    [Description("Generates an image based on the world details and an optional set of instructions.")]
    public async Task<string> GenerateWorldImage([Description("The unique session ID for this world creation session.")] string sessionId, [Description("Additional instructions for image generation.")] string? additionalInstructions = null)
    {
        // Get session and draft world
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftWorld == null)
        {
            return JsonSerializer.Serialize(new
            {
                Success = false,
                Message = "World creation session not found"
            });
        }

        var world = session.Context.DraftWorld;
        var response = await ImageGenService.GenerateWorldImage(world, additionalInstructions);
        session.Context.DraftWorld.ImageUrl = response;
        await sessionStateManager.UpdateSessionStateAsync(session);
        return JsonSerializer.Serialize(new
        {
            Success = true,
            Image = $"Image generated successfully! Begins as {response.Split(';')[0]}"
        });
    }
    [Description("Saves the complete world from the session to the repository and campaign. This finalizes world building and makes the world available for gameplay. Parameters: sessionId (world building session ID), campaignId (campaign to attach world to), worldData (optional JSON string with world name and theme). Returns JSON confirming save and world statistics.")]
    public async Task<string> SaveWorld(
        [Description("The unique session ID for this world building session.")] string sessionId
        //[Description("The unique ID of the campaign this world belongs to.")] string campaignId,
        /*[Description("Optional JSON string containing world metadata including 'name' and 'theme' properties (e.g., '{\"name\":\"Aetheria\",\"theme\":\"High Fantasy\"}').")] string? worldData = null*/)
    {
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session?.Context.DraftWorld == null)
        {
            return JsonSerializer.Serialize(new SaveWorldResult
            {
                Success = false,
                Message = "",
                Error = "Session or draft world not found"
            });
        }

        var world = session.Context.DraftWorld;

        
        world.UpdatedAt = DateTime.UtcNow;
        
        // Save to repository
        await worldRepository.CreateAsync(world);
        
        // Update campaign state with the world
        
        // Mark session as completed
        await sessionStateManager.CompleteSessionAsync(sessionId, world.Id);

        // Count lore entries (WorldEvents with Status="Lore")
        var loreCount = world.Events.Count(e => e.Status == "Lore");

        return JsonSerializer.Serialize(new SaveWorldResult
        {
            Success = true,
            WorldId = world.Id,
            WorldName = world.Name,
            LocationCount = world.Locations.Count,
            NPCCount = world.NPCs.Count,
            QuestCount = world.Quests.Count,
            LoreEntryCount = loreCount,
            Message = $"World '{world.Name}' saved successfully with {world.Locations.Count} locations, {world.NPCs.Count} NPCs, {world.Quests.Count} quests, and {loreCount} lore entries."
        });
    }
}