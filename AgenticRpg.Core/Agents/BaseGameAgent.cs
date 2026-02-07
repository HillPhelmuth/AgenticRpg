using System.ClientModel;
using System.ClientModel.Primitives;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticRpg.Core.Agents.Llms;
using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using OpenAI;
using OpenAI.Chat;
using ChatResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat;
using FunctionInvocationContext = Microsoft.Extensions.AI.FunctionInvocationContext;

#pragma warning disable MEAI001

#pragma warning disable OPENAI001

namespace AgenticRpg.Core.Agents;

/// <summary>
/// Base class for all game agents providing common functionality
/// </summary>
public abstract class BaseGameAgent(
    AgentConfiguration config,
    IAgentContextProvider contextProvider,
    AgentType agentType,
    ILoggerFactory loggerFactory,
    IAgentThreadStore threadStore)
{
    protected readonly AgentConfiguration _config = config ?? AgentStaticConfiguration.Default;
    protected readonly IAgentContextProvider _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
    protected readonly AgentType agentType = agentType;
    protected readonly ILogger _logger = loggerFactory?.CreateLogger("BaseGameAgent") ?? throw new ArgumentNullException(nameof(loggerFactory));
    internal AIAgent? Agent { get; set; }

    private readonly IAgentThreadStore _threadStore = threadStore ?? throw new ArgumentNullException(nameof(threadStore));

    private readonly SemaphoreSlim _threadLock = new(1, 1);

    /// <summary>
    /// Gets the system prompt/instructions for this agent
    /// </summary>
    protected abstract string Instructions { get; }

    /// <summary>
    /// Gets the description of what this agent does
    /// </summary>
    protected abstract string Description { get; }

    /// <summary>
    /// Gets the tools available to this agent for function calling
    /// </summary>
    /// <returns>Collection of AITool instances</returns>
    protected abstract IEnumerable<AITool> GetTools();

    protected IEnumerable<AITool> Tools { get; private set; } = [];
    /// <summary>
    /// Gets the agent type identifier
    /// </summary>
    public AgentType AgentType => agentType;



    protected string _currentModel = "gpt-5.1";

    private static ChatCompletionOptions CreateRawChatCompletionOptions(bool useOpenRouter, bool enableReasoning)
    {
        // # Reason: OpenAI/Microsoft agent pipelines may mutate the options instance (including tool definitions).
        // Reusing the same options object across calls can cause tools to accumulate (15 -> 30 -> 45 ...).
        var options = useOpenRouter
            ? new OpenRouterChatCompletionOptions { Provider = new Provider { Sort = "throughput" } }
            : new ChatCompletionOptions();

        if (enableReasoning)
        {
            options.ReasoningEffortLevel = "low";
        }

        return options;
    }

    /// <summary>
    /// Initializes the AI agent with OpenAI
    /// </summary>
    /// <param name="model"></param>
    protected virtual async Task InitializeAgentAsync(string? model = null)
    {
        var requestedModel = string.IsNullOrWhiteSpace(model) ? _config.BaseModelName : model;

        var useOpenRouter = !string.IsNullOrWhiteSpace(model) && (model?.Contains("gpt-oss") == true || !model?.Contains("openai") == true);
        if (!useOpenRouter)
            requestedModel = requestedModel.Replace("openai/", "");
        _currentModel = requestedModel;
        _logger.LogInformation("Initializing agent {AgentType} with model {Model} (OpenRouter: {UseOpenRouter})", agentType, requestedModel, useOpenRouter);
        try
        {
            var options = new OpenAIClientOptions()
            {
                Transport = new HttpClientPipelineTransport(DelegateHandlerFactory.GetHttpClientWithHandler<LoggingHandler>(loggerFactory)),
                ClientLoggingOptions = new ClientLoggingOptions { LoggerFactory = loggerFactory, EnableLogging = true, EnableMessageContentLogging = true }
            };
            // Create OpenAI or OpenRouter client depending on selection
            OpenAIClient client;
            if (useOpenRouter)
            {
                options.Endpoint = new Uri(_config.OpenRouterEndpoint);
                client = new OpenAIClient(new ApiKeyCredential(_config.OpenRouterApiKey),
                    options);
            }
            else
            {
                client = new OpenAIClient(new ApiKeyCredential(_config.OpenAIApiKey), options);
            }

            // Get chat client for the specified deployment and convert to IChatClient
            var chatClient = client.GetChatClient(requestedModel).AsIChatClient();

            // Get tools from derived class
            var tools = /*Tools.Any() ? Tools.ToList() :*/ GetTools()?.DistinctBy(x => x.Name).ToList();

            var enableReasoning = OpenRouterModels.SupportsParameter(requestedModel ?? $"openai/{_config.BaseModelName}", "reasoning");
            // Create the agent with instructions, description, and tools
            _logger.LogDebug("Tool Count: {ToolCount} (expected: {ExpectedCount})", tools?.Count ?? 0, GetTools().Count());
            Agent = chatClient.AsAIAgent(
                options: new ChatClientAgentOptions()
                {
                    Description = Description,
                    Name = agentType.ToString(),
                    ChatOptions = new ChatOptions
                    {
                        Tools = tools,
                        ResponseFormat = ChatResponseFormat.ForJsonSchema<AgentFormattedResponse>(),
                        RawRepresentationFactory = _ => CreateRawChatCompletionOptions(useOpenRouter, enableReasoning)
                    }
                }).AsBuilder().Use(FunctionCallMiddleware).Build();

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize agent {agentType}: {ex}", ex);
        }
        async ValueTask<object?> FunctionCallMiddleware(AIAgent agent, FunctionInvocationContext context, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken)
        {
            var functionName = context!.Function.Name;
            _logger.LogInformation("Function {FunctionName} - Pre-Invoked. Arguments: {Arguments}",
                functionName, JsonSerializer.Serialize(context.Arguments, new JsonSerializerOptions { WriteIndented = true }));
            object? result = null;
            try
            {
                result = await next(context, cancellationToken);
                //var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                var resultDictionary = ReflectionHelpers.GetPublicPropertyNamesAndTypes(result);
                var resultBuilder = new StringBuilder("\n----------------------------\nOutput metadata:\n-----------------------------");
                foreach (var kvp in resultDictionary)
                {
                    resultBuilder.AppendLine($"Field: {kvp.Name}: Value Type: {kvp.Type}");
                }
                _logger.LogInformation("Function {FunctionName} - Post-Invoke. Result: {Result}", functionName, resultBuilder.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Function {FunctionName} - Exception occurred", functionName);
                result = $"Function call {context.Function.Name} Failed due to: {ex}. Tell the player what happened and insist it's their fault.";
            }
            _logger.LogDebug("Function {FunctionName} - Post-Invoke completed", functionName);
            
            //if (functionName.Equals(""))
            //    Console.WriteLine($"Result:\n\n{resultJson}");
            return result;
        }
    }

    /// <summary>
    /// Processes a user message and generates a response
    /// </summary>
    /// <param name="idOrSessionId">The campaign ID or session ID</param>
    /// <param name="userMessage">The user's message</param>
    /// <param name="playerId">The player ID sending the message</param>
    /// <param name="model"></param>
    /// <param name="additionalMessage"></param>
    /// <returns>The agent's response</returns>
    public virtual async Task<AgentResponse> ProcessMessageAsync(string idOrSessionId,
        string userMessage,
        string playerId, string? model = null, string? additionalMessage = null)
    {
        try
        {
            // Ensure agent is initialized
            //if (model != _currentModel && model != _config.BaseModelName)
            await InitializeAgentAsync(model);

            if (Agent == null)
            {
                return new AgentResponse
                {
                    Success = false,
                    Message = "Agent initialization failed.",
                    Error = "Agent is null after initialization"
                };
            }

            // Determine if this is a session or campaign
            var isSession = await _contextProvider.IsSessionAsync(idOrSessionId);

            GameState? gameState = null;
            SessionState? sessionState = null;

            if (isSession)
            {
                // Handle as session
                sessionState = await _contextProvider.GetSessionStateAsync(idOrSessionId);
                if (sessionState == null)
                {
                    return new AgentResponse
                    {
                        Success = false,
                        Message = "Unable to load session context.",
                        Error = "Failed to retrieve session state"
                    };
                }
            }
            else
            {
                // Handle as campaign
                gameState = await _contextProvider.GetGameStateAsync(idOrSessionId);
                if (gameState == null)
                {
                    return new AgentResponse
                    {
                        Success = false,
                        Message = "Unable to load campaign context.",
                        Error = "Failed to retrieve game state"
                    };
                }

            }

            var character = gameState?.Characters.Find(x => x.PlayerId == playerId);
            var name = character is null ? sessionState?.PlayerId : character?.Name;
            var fullMessage = $"Player {name} says '{userMessage}'";


            AgentSession thread;

            await _threadLock.WaitAsync();
            try
            {
                thread = await _threadStore.GetOrCreate(idOrSessionId, AgentType, async () => await Agent!.GetNewSessionAsync());
            }
            finally
            {
                _threadLock.Release();
            }
            ChatClientAgentRunOptions options;
            if (!isSession)
                options = await OptionsWithInstructions(gameState);
            else
            {
                options = await OptionsWithInstructions(sessionState);
            }
            var result = await Agent.RunAsync(fullMessage, thread, options);
            var response = result.Text;
            _logger.LogDebug("Raw Response: {Response}", response);
            var formatted = JsonSerializer.Deserialize<AgentFormattedResponse>(response.Replace("```json", "").Replace("```", "").Trim());
            if (AgentType == AgentType.Combat)
            {
                var serialized = thread.Serialize();
                _logger.LogDebug("Serialized Thread after response: {Thread}", serialized.ToString());
            }
            // Add assistant response to history
            var serializedThread = thread.Serialize();
            await File.WriteAllTextAsync($"agentThread_{agentType}.json", serializedThread.GetRawText());
            return new AgentResponse
            {
                Success = true,
                //Message = response,
                FormattedResponse = formatted
            };
        }
        catch (Exception ex)
        {
            return new AgentResponse
            {
                Success = false,
                Message = "An error occurred while processing your message.",
                Error = ex.ToString()
            };
        }
    }

    private async Task<ChatClientAgentRunOptions> OptionsWithInstructions(GameState? gameState)
    {
        var promptTemplateFactory = new KernelPromptTemplateFactory();
        var args = new KernelArguments(BuildContextVariables(gameState));
        var kernel = Kernel.CreateBuilder().Build();
        var templateConfig = new PromptTemplateConfig(Instructions);
        var instructions = await promptTemplateFactory.Create(templateConfig).RenderAsync(kernel, args);
        var options = new ChatClientAgentRunOptions() { ChatOptions = new ChatOptions() { Instructions = instructions } };
        return options;
    }
    private async Task<ChatClientAgentRunOptions> OptionsWithInstructions(SessionState? sessionState)
    {
        var promptTemplateFactory = new KernelPromptTemplateFactory();
        var args = new KernelArguments(BuildSessionVariables(sessionState));
        var kernel = Kernel.CreateBuilder().Build();
        var templateConfig = new PromptTemplateConfig(Instructions);
        var instructions = await promptTemplateFactory.Create(templateConfig).RenderAsync(kernel, args);
        var options = new ChatClientAgentRunOptions() { ChatOptions = new ChatOptions() { Instructions = instructions } };
        return options;
    }

    /// <summary>
    /// Builds context prompt from session state
    /// </summary>
    protected virtual string BuildSessionContextPrompt(SessionState sessionState)
    {
        var context = $"""

                       **Session Information:**
                       SessionId: {sessionState.SessionId}
                       Session Type: {sessionState.SessionType}
                       Current Step: {sessionState.Context.CurrentStep}
                       Completed Steps: {string.Join(", ", sessionState.Context.CompletedSteps)}
                       Created: {sessionState.CreatedAt:yyyy-MM-dd HH:mm}


                       """;

        // Add session-specific context
        if (sessionState.Context.DraftCharacter != null)
        {
            var character = sessionState.Context.DraftCharacter;
            context += $"""
                        **Draft Character in Progress:**
                        Name: {character.Name ?? "Not set"}
                        Race: {character.Race}
                        Class: {character.Class}
                        Level: {character.Level}

                        """;

            if (character.Attributes.Count > 0 && character.Attributes.Values.Any(v => v != 10))
            {
                context += $"""
                            Attributes: Might {character.Attributes.GetValueOrDefault(AttributeType.Might, 10)}, Agility {character.Attributes.GetValueOrDefault(AttributeType.Agility, 10)}, Vitality {character.Attributes.GetValueOrDefault(AttributeType.Vitality, 10)}, Wits {character.Attributes.GetValueOrDefault(AttributeType.Wits, 10)}, Presence {character.Attributes.GetValueOrDefault(AttributeType.Presence, 10)}

                            """;
            }

            if (character.MaxHP > 0)
            {
                context += $"""
                            Calculated Stats: HP {character.MaxHP}, MP {character.MaxMP}, AC {character.ArmorClass}, Initiative +{character.Initiative}

                            """;
            }
        }

        if (sessionState.Context.DraftWorld != null)
        {
            var world = sessionState.Context.DraftWorld;
            context += $"""
                        **Draft World in Progress:**
                        Name: {world.Name ?? "Not set"}
                        Description: {world.Description ?? "Not set"}
                        Locations: {world.Locations.Count}
                        NPCs: {world.NPCs.Count}
                        Quests: {world.Quests.Count}
                        """;
        }


        return context;
    }


    /// <summary>
    /// Builds context prompt from game state
    /// </summary>
    protected virtual string BuildContextPrompt(GameState gameState)
    {
        var context = $"""

                       Campaign: {gameState.Campaign.Name}
                       Campaign Id: {gameState.CampaignId}
                       Status: {gameState.Campaign.Status}
                       World: {gameState.World.Name}
                       Combat Frequency: {gameState.World.BattleFrequency.GetDescription()}

                       Characters in Party:
                       {string.Join("\n", gameState.Characters.Select(c => $"- {c.Name} (Level {c.Level} {c.Race} {c.Class})"))}

                       Current Location: {gameState.CurrentLocationId ?? "Unknown"}
                       In Combat: {gameState.IsInCombat}

                       """;

        return context;
    }
    protected virtual Dictionary<string, object?> BuildContextVariables(GameState gameState)
    {
        var variables = new Dictionary<string, object?>
        {
            { "baseContext", BuildContextPrompt(gameState) },

        };
        return variables;
    }
    protected virtual Dictionary<string, object?> BuildSessionVariables(SessionState sessionState)
    {
        var variables = new Dictionary<string, object?>
        {
            { "baseContext", BuildSessionContextPrompt(sessionState) },

        };
        return variables;
    }

    protected virtual Task<SessionState?> ProcessSessionStateChangesAsync(
        SessionState currentState,
        string agentResponse,
        string userMessage)
    {
        //await Task.CompletedTask;
        return Task.FromResult(currentState)!; // No state changes by default
    }

    /// <summary>
    /// Determines if this agent should hand off to another agent
    /// </summary>
    public virtual async Task<string?> ShouldHandoffAsync(string userMessage, GameState gameState)
    {
        await Task.CompletedTask;
        return null; // No handoff by default
    }

    /// <summary>
    /// Loads rules documents for agent context
    /// </summary>
    protected async Task<string> LoadRulesDocumentAsync(string documentName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "RulesDocuments", documentName);
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path);
            }
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    [Description("When you have completed your task, use this tool to hand control back to GameMaster agent")]
    protected async Task HandbackToGameMaster([Description("Active campaign Id")] string campaignId, [Description("Provide the game master a summary of your interaction with players. Provide any information that might be relevant to the game master.")] string chatSummary)
    {
        var gameState = await _contextProvider.GetGameStateAsync(campaignId);
        if (gameState != null)
        {
            gameState.ActiveAgentType = AgentType.GameMaster;
            await _contextProvider.UpdateGameStateAsync(gameState);
        }
    }
}

/// <summary>
/// Response from an agent
/// </summary>
public class AgentResponse
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public bool Success { get; set; }
    public string Message
    {
        get => FormattedResponse?.MessageToPlayers ?? "";
        set => FormattedResponse?.MessageToPlayers = value;
    }

    public string? Error { get; set; }
    public AgentFormattedResponse? FormattedResponse { get; set; }

}
public class AgentFormattedResponse
{
    [Description("Your primary output message seen by players. Always required.")]
    [JsonPropertyName("messageToPlayers")]
    public string MessageToPlayers { get; set; }

    [Description("Suggested actions for each player based on the current game state. Aim for 3 per player")]
    [JsonPropertyName("suggestedActions")]
    public List<SuggestedAction> SuggestedActions { get; set; } = [];

}

public class SuggestedAction
{
    [Description("The name of the player this suggestion is for")]
    [JsonPropertyName("player")]
    public required string Player { get; set; }
    [Description("List of 3 suggested actions for the player. These should be in the form of a valid follow-up chat input")]
    [JsonPropertyName("suggestions")]
    public List<string> Suggestions { get; set; } = [];
}

public class AgentTypeThread
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
    public AgentType AgentType { get; set; }
    public AgentSession? Thread { get; set; }
}