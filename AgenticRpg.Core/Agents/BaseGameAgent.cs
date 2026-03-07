using AgenticRpg.Core.Agents.Llms;
using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using Azure.Identity;
using Google.GenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat;
using FunctionInvocationContext = Microsoft.Extensions.AI.FunctionInvocationContext;

#pragma warning disable MEAI001

#pragma warning disable OPENAI001

namespace AgenticRpg.Core.Agents;

/// <summary>
/// Base class for all game agents providing common functionality
/// </summary>
public abstract class BaseGameAgent(
    IAgentContextProvider contextProvider,
    AgentType agentType,
    ILoggerFactory loggerFactory,
    IAgentSessionStore agentSessionStore)
{
    private const string SuggestAgentInstructions = """
                                                     Generate suggested next actions for each player based on the provided current game state and agent response. For each player, provide 3 high-quality, relevant suggestions in the format of valid follow-up chat inputs, tailored to the current context and player roles. 

                                                     For each player, first give detailed reasoning behind why each suggestion is appropriate, considering game state, player objectives, and previous interactions. Ensure that Reasoning sections precede the listed suggestions for every player. 

                                                     Persist in reasoning through each player's situation individually and only finalize your answer when all players' actions have been addressed with clear rationale and complete sets of suggestions.
                                                     
                                                     **Output Format:**
                                                     - The entire output is a single JSON object conforming to the above schema.
                                                     - The Reasoning field must contain a separate, clearly labeled section (e.g., "Player1:", "Player2:") for each player, explaining the logic behind the three suggestions for that player, placed before their corresponding suggestions.
                                                     - Each object in suggestedActions must include the player name and exactly three actionable, relevant suggestions (as chat input strings) tailored to the current state.
                                                     - Do not include markdown or code blocks.
                                                     
                                                     Example:
                                                     
                                                     {
                                                       "Reasoning": "Player1: Player1 is low on resources and should focus on recovery or negotiation. Suggestion 1 allows resource trading; Suggestion 2 proposes an alliance; Suggestion 3 explores scavenging. Player2: Player2 controls a pivotal position and can either defend, expand influence, or negotiate. Suggestions focus on each option, taking into account recent alliance offers.",
                                                       "suggestedActions": [
                                                         {
                                                           "player": "Player1",
                                                           "suggestions": [
                                                             "Offer to trade resources with Player2.",
                                                             "Propose an alliance with another player.",
                                                             "Search the area for additional supplies."
                                                           ]
                                                         },
                                                         {
                                                           "player": "Player2",
                                                           "suggestions": [
                                                             "Strengthen defenses at your current location.",
                                                             "Negotiate terms with Player1 about their alliance proposal.",
                                                             "Expand territory into an adjacent area."
                                                           ]
                                                         }
                                                       ]
                                                     }
                                                     
                                                     (If there are more players or game complexity, extend the Reasoning and suggestedActions arrays accordingly. In actual use, Reasoning per player should be more detailed than this example.)
                                                     
                                                     Important:  
                                                     - Reasoning for each player MUST appear BEFORE their respective suggestions.  
                                                     - ALL output must be in the specified JSON schema.  
                                                     - Provide exactly three suggestions per player, as valid chat input strings.
                                                     - Keep suggestions simple. They should be actionable steps a player can take in the game, not complex strategies or vague ideas.
                                                     - NEVER suggest the player to request behavior changes from the agent (e.g., "Tell the Game Master to be nicer") as a suggested action. The suggestions should always be actionable steps the player can take within the game, not requests for the agent to alter its behavior.
                                                     
                                                     REMEMBER: The suggestions will be turned directly into chat inputs for the players, and thus cannot contain variables or further direction to the player as your suggestion will end up going straight to the agent. 
                                                     """;
    protected readonly AgentConfiguration _config = AgentStaticConfiguration.Default;
    protected readonly IAgentContextProvider _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
    protected readonly AgentType agentType = agentType;
    protected readonly ILogger _logger = loggerFactory?.CreateLogger("BaseGameAgent") ?? throw new ArgumentNullException(nameof(loggerFactory));
    internal AIAgent? Agent { get; set; }

    private readonly IAgentSessionStore _agentSessionStore = agentSessionStore ?? throw new ArgumentNullException(nameof(agentSessionStore));

    private readonly SemaphoreSlim _threadLock = new(1, 1);

    /// <summary>
    /// Gets the system prompt/instructions for this agent
    /// </summary>
    public abstract string Instructions { get; }

    /// <summary>
    /// Gets the description of what this agent does
    /// </summary>
    protected abstract string Description { get; }

    /// <summary>
    /// Gets the tools available to this agent for function calling
    /// </summary>
    /// <returns>Collection of AITool instances</returns>
    protected abstract IEnumerable<AITool> GetTools();

    /// <summary>
    /// Gets the agent type identifier
    /// </summary>
    public AgentType AgentType => agentType;
    public event Action<AgentSuggestedActions>? OnSuggestedActions;
    public event Action<string>? OnTokenGenerated;



    protected string _currentModel = "gpt-5.4";

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

        var useOpenRouter = !requestedModel.StartsWith("gpt-");
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
            var chatClient = client.GetChatClient(_currentModel).AsIChatClient();

            // Get tools from derived class
            var tools = GetTools()?.DistinctBy(x => x.Name).ToList();

            var enableReasoning = OpenRouterModels.SupportsParameter(_currentModel, "reasoning");
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
    public virtual async Task<AgentResponse> ProcessMessageAsync(string idOrSessionId, string userMessage,
        string playerId, string? model = null, string? additionalMessage = null, AgentSession? agentSession = null)
    {
        try
        {
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


            //AgentSession thread;

            await _threadLock.WaitAsync();
            try
            {
                agentSession ??= await _agentSessionStore.GetOrCreate(idOrSessionId, AgentType, async () => await Agent!.CreateSessionAsync());
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
            var result = await Agent.RunAsync(fullMessage, agentSession, options);
            //var formattedResponse = await AgentFormattedResponse(agentSession, fullMessage, options, gameState, sessionState);
            var response = result.Text;
            var items = result.Messages.Where(x => x.Role == ChatRole.Assistant).ToList();
            if (items.Count > 1)
            {
                _logger.LogInformation("Multiple assistant messages in response. Sending final response to client. Total responses: {count}", items.Count);
                response = items.Last().Text;
            }
            _logger.LogDebug("Raw Response: {Response}", response);
            var output = NormalizeLegacyStructuredOutput(response);
            var suggestedActions = await GenerateSuggestedActionsAsync(gameState, sessionState, output);
            OnSuggestedActions?.Invoke(suggestedActions);

            // Add assistant response to history
            var serializedThread = await Agent.SerializeSessionAsync(agentSession);
            await File.WriteAllTextAsync($"agentThread_{agentType}.json", serializedThread.GetRawText());
            return new AgentResponse
            {
                Success = true,
                FormattedResponse = new AgentFormattedResponse
                {
                    MessageToPlayers = output,
                    SuggestedActions = suggestedActions.SuggestedActions
                }
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

    public async IAsyncEnumerable<string> ProcessMessageStreamingAsync(string idOrSessionId, string userMessage,
        string playerId, string? model = null, string? additionalMessage = null, AgentSession? agentSession = null)
    {


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
                yield return "Unable to load session context.";
                yield break;

            }
            model ??= sessionState.SelectedModel ?? _config.BaseModelName;
            _logger.LogDebug("Base model name: {BaseModelName}", _config.BaseModelName);
        }
        else
        {
            // Handle as campaign
            gameState = await _contextProvider.GetGameStateAsync(idOrSessionId);
            if (gameState == null)
            {
                yield return "Unable to load campaign context.";
                yield break;

            }
            model ??= gameState.SelectedModel;

        }

        await InitializeAgentAsync(model);

        if (Agent == null)
        {
            yield return "Agent initialization failed.";
            yield break;
        }
        var character = gameState?.Characters.Find(x => x.PlayerId == playerId);
        var name = character is null ? sessionState?.PlayerId : character?.Name;
        var fullMessage = $"Player {name} says '{userMessage}'";


        //AgentSession thread;

        await _threadLock.WaitAsync();
        try
        {
            agentSession ??= await _agentSessionStore.GetOrCreate(idOrSessionId, AgentType, async () => await Agent!.CreateSessionAsync());
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
        var outputBuilder = new StringBuilder();
        bool? isLegacyStructuredOutput = null;

        await foreach (var token in Agent.RunStreamingAsync(fullMessage, agentSession, options))
        {
            if (string.IsNullOrEmpty(token.Text)) continue;

            outputBuilder.Append(token.Text);

            if (!isLegacyStructuredOutput.HasValue)
            {
                var firstContentCharacter = outputBuilder.ToString().FirstOrDefault(c => !char.IsWhiteSpace(c));
                if (firstContentCharacter != default)
                {
                    isLegacyStructuredOutput = firstContentCharacter == '{';
                }
            }

            if (isLegacyStructuredOutput == false)
            {
                OnTokenGenerated?.Invoke(token.Text);
                yield return token.Text;
            }
        }
        var output = NormalizeLegacyStructuredOutput(outputBuilder.ToString());

        if (isLegacyStructuredOutput == true && !string.IsNullOrWhiteSpace(output))
        {
            OnTokenGenerated?.Invoke(output);
            yield return output;
        }

        var suggestResult = await GenerateSuggestedActionsAsync(gameState, sessionState, output);
        OnSuggestedActions?.Invoke(suggestResult);

    }

    private static string NormalizeLegacyStructuredOutput(string? response)
    {
        var trimmed = response?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (!trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return trimmed;
            }

            if (TryGetStringPropertyIgnoreCase(document.RootElement, "MessageToPlayers", out var messageToPlayers) && !string.IsNullOrWhiteSpace(messageToPlayers))
            {
                return messageToPlayers.Trim();
            }

            if (TryGetStringPropertyIgnoreCase(document.RootElement, "MessageToPlayer", out var messageToPlayer) && !string.IsNullOrWhiteSpace(messageToPlayer))
            {
                return messageToPlayer.Trim();
            }
        }
        catch (JsonException)
        {
            return trimmed;
        }

        return trimmed;
    }

    private static bool TryGetStringPropertyIgnoreCase(JsonElement element, string propertyName, out string? value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString();
                return true;
            }

            break;
        }

        value = null;
        return false;
    }

    private async Task<AgentSuggestedActions> GenerateSuggestedActionsAsync(GameState? gameState, SessionState? sessionState, string output)
    {
        var optionsOptions = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(DelegateHandlerFactory.GetHttpClientWithHandler<LoggingHandler>(loggerFactory)),
            ClientLoggingOptions = new ClientLoggingOptions { LoggerFactory = loggerFactory, EnableLogging = true, EnableMessageContentLogging = true },
            Endpoint = new Uri(_config.OpenRouterEndpoint)
        };
        var client = new OpenAIClient(new ApiKeyCredential(_config.OpenRouterApiKey), optionsOptions).GetChatClient("gpt-oss-120b:nitro");
        var suggestionAgent = client.AsAIAgent(new ChatClientAgentOptions()
        {
            Name = "SuggestionAgent",
            Description =
                "Agent that generates suggested actions for players based on the current game state and agent response. Aim for 3 suggestions per player.",
            ChatOptions = new ChatOptions()
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema<AgentSuggestedActions>(),
                Instructions = SuggestAgentInstructions
            }
        });

        var context = gameState is not null
            ? BuildContextPrompt(gameState)
            : BuildSessionContextPrompt(sessionState!);

        return (await suggestionAgent.RunAsync<AgentSuggestedActions>($"""
             **Game Context:**
             {context}
             **Agent Response:** 
             {output}
             """)).Result;
    }

    internal async Task<ChatClientAgentRunOptions> OptionsWithInstructions(GameState? gameState)
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
                       PlayerId: {sessionState.PlayerId}
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
                        Quests: {world.SideQuests.Count}
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

public class AgentSuggestedActions
{
    [Description("Reasons for suggested actions. Should contain a section for each player explain why the actions for that player were provided")]
    public string? Reasoning { get; set; }
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