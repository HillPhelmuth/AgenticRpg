using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Text;
using ChatResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat;


namespace AgenticRpg.Core.Services;

public class QuestGenService
{
    private const string Instructions = """
                                         ## Instructions
                                         
                                         Design a primary quest for a new RPG campaign using the provided world information, quest description, and difficulty level. 
                                         
                                         Carefully analyze the setting, lore, factions, and notable elements from the world information to ensure the quest fits the environment and narrative. Consider the tone, challenges, and any constraints established by the difficulty level when devising obstacles, antagonists, or milestones. 
                                         
                                         First, review all provided context and consider:  
                                         - Examine key world features or conflicts the quest could revolve around.
                                         - Identify NPCs, artifacts, monsters, or themes to be included.
                                         - Determine how the specified difficulty should be reflected in the quest’s scope, required skills, dangers, and rewards.
                                         - Use the available monsters list to select appropriate adversaries that align with the quest’s theme and challenge level.
                                         - Remember, this is a primary quest that should have at least one powerful monster, several weaker monsters of varying difficulty, and generous rewards.
                                         
                                         ## Additional Context
                                         
                                         ### World Information
                                         
                                         {{$world}}
                                         
                                         ### Quest Difficulty Level
                                         
                                         {{$difficulty}}
                                         
                                         ### Available Monsters
                                         
                                         {{$availableMonsters}}
                                         
                                         ## Output Format
                                         Output the quest in valid JSON format matching the Quest schema. Ensure all fields are populated accurately, including a unique identifier, name, detailed description, quest giver, starting location, objectives, status, rewards, and involved monsters.
                                         """;
    private static readonly AgentConfiguration Configuration = AgentStaticConfiguration.Default;

    public static async Task<Quest> GeneratePrimaryQuest(World world, string description, int difficulty)
    {
        var availableMonsters =
            RpgMonster.GetAllRpgMonsters(difficulty - 2, difficulty - 1, difficulty, difficulty + 1, difficulty + 2);
        var promptTemplateFactory = new KernelPromptTemplateFactory();
        var monsters = string.Join("\n", availableMonsters.Select(m => m.BasicInfoMarkdown()));
        var args = new KernelArguments()
        {
            ["world"] = world,
            ["difficulty"] = difficulty,
            ["availableMonsters"] = monsters
        };
        var kernel = Kernel.CreateBuilder().Build();
        var templateConfig = new PromptTemplateConfig(Instructions);
        var instructions = await promptTemplateFactory.Create(templateConfig).RenderAsync(kernel, args);
        var client = new OpenAIClient(new ApiKeyCredential(Configuration.OpenRouterApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(Configuration.OpenRouterEndpoint) }).GetChatClient("x-ai/grok-4.1-fast");
        ChatClientAgent agent = client.AsAIAgent(new ChatClientAgentOptions()
        {
            Description = "Generates a primary quest for a new campaign",
            Name = "Quest Generation Agent",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<Quest>()
            }
        });
        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync<Quest>($"Generate a primary quest based on this description: '{description}'");
        return response.Result;
    }
}