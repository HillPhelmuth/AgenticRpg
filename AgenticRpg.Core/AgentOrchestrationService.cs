using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Messaging;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.State;
using Microsoft.Extensions.Logging;
using Polly;
using System.Text;
using System.Threading;

namespace AgenticRpg.Core;

/// <summary>
/// Creates and manages orchestration of AI agents within the RPG system using Microsoft Agent Framework (Microsoft.Agents.AI.OpenAI)
/// Handles agent handoffs, context preservation, and routes messages to the appropriate agent based on GameState.ActiveAgent
/// </summary>
public class AgentOrchestrationService
{
    private readonly IGameStateManager _stateManager;
    private readonly ILogger<AgentOrchestrationService> _logger;
    private readonly IAgentContextProvider _agentContextProvider;

    // Agent instances
    private readonly GameMasterAgent _gameMasterAgent;
    private readonly CombatAgent _combatAgent;
    private readonly CharacterCreationAgent _characterCreationAgent;
    private readonly CharacterManagerAgent _characterLevelUpAgent;
    private readonly ShopKeeperAgent _economyManagerAgent;
    private readonly WorldBuilderAgent _worldBuilderAgent;
    
    private readonly Dictionary<string, CampaignMessageQueue> _messageQueues = [];
    private readonly Lock _lock = new();
    private const string GlobalModelScope = "__global__";
    private const string HandoffContextKey = "HandoffContext";

    /// <summary>
    /// Creates and manages orchestration of AI agents within the RPG system using Microsoft Agent Framework (Microsoft.Agents.AI.OpenAI)
    /// Handles agent handoffs, context preservation, and routes messages to the appropriate agent based on GameState.ActiveAgent
    /// </summary>
    public AgentOrchestrationService(IGameStateManager stateManager,
        ILogger<AgentOrchestrationService> logger,
        GameMasterAgent gameMasterAgent,
        CombatAgent combatAgent,
        CharacterCreationAgent characterCreationAgent,
        CharacterManagerAgent characterLevelUpAgent,
        ShopKeeperAgent economyManagerAgent,
        WorldBuilderAgent worldBuilderAgent, IAgentContextProvider agentContextProvider)
    {
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gameMasterAgent = gameMasterAgent ?? throw new ArgumentNullException(nameof(gameMasterAgent));
        _combatAgent = combatAgent ?? throw new ArgumentNullException(nameof(combatAgent));
        _characterCreationAgent = characterCreationAgent ?? throw new ArgumentNullException(nameof(characterCreationAgent));
        _characterLevelUpAgent = characterLevelUpAgent ?? throw new ArgumentNullException(nameof(characterLevelUpAgent));
        _economyManagerAgent = economyManagerAgent ?? throw new ArgumentNullException(nameof(economyManagerAgent));
        _worldBuilderAgent = worldBuilderAgent ?? throw new ArgumentNullException(nameof(worldBuilderAgent));
        _agentContextProvider = agentContextProvider;
    }


    public async Task ChangeModel(string? scopeId, string? modelId)
    {
        var resolvedScope = string.IsNullOrWhiteSpace(scopeId) ? GlobalModelScope : scopeId;
        await _agentContextProvider.SetSessionOrCampaignModel(resolvedScope, modelId);
        
    }

    /// <summary>
    /// Adds a campaign message to the prioritized processing queue.
    /// </summary>
    public async Task<AgentResponse> EnqueueCampaignMessageAsync(PlayerMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var priority = await CalculatePriorityAsync(request).ConfigureAwait(false);
        var queue = GetOrCreateQueue(request.CampaignId);
        return await queue.EnqueueAsync(request, priority).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes a player message through the appropriate agent
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="playerId">The player ID</param>
    /// <param name="message">The player's message</param>
    /// <param name="targetAgentType">Optional: specific agent to route to. If null, uses active agent from GameState (defaults to GameMaster)</param>
    /// <returns>The agent's response</returns>
    public async Task<AgentResponse> ProcessMessageAsync(string campaignId,
        string playerId,
        string message,
        AgentType targetAgentType = AgentType.None)
    {
        return await ProcessMessageInternalAsync(campaignId, playerId, message, targetAgentType, null, null);
    }

    /// <summary>
    /// Processes a player message through the appropriate agent and streams generated tokens.
    /// </summary>
    /// <param name="campaignId">The campaign or session ID.</param>
    /// <param name="playerId">The player ID.</param>
    /// <param name="message">The player's message.</param>
    /// <param name="targetAgentType">Optional explicit agent target.</param>
    /// <param name="streamCallback">Callback receiving streaming token updates.</param>
    /// <param name="clientMessageId">Optional client message id for correlation.</param>
    /// <returns>The final aggregated response.</returns>
    public async Task<AgentResponse> ProcessMessageStreamingAsync(
        string campaignId,
        string playerId,
        string message,
        AgentType targetAgentType = AgentType.None,
        Func<MessageStreamUpdate, Task>? streamCallback = null,
        string? clientMessageId = null)
    {
        return await ProcessMessageInternalAsync(campaignId, playerId, message, targetAgentType, streamCallback, clientMessageId);
    }

    private async Task<AgentResponse> ProcessMessageInternalAsync(
        string campaignId,
        string playerId,
        string message,
        AgentType targetAgentType,
        Func<MessageStreamUpdate, Task>? streamCallback,
        string? clientMessageId)
    {
        BaseGameAgent? activeAgent = null;
        try
        {
            // Get game state to determine active agent
            var gameState = await _stateManager.GetCampaignStateAsync(campaignId);
            var currentActiveAgent = targetAgentType == AgentType.None ? gameState?.ActiveAgentType : targetAgentType;
            // Get the agent to use - either specified target or current active agent from GameState
            
            if (targetAgentType != AgentType.None)
            {
                // Route directly to specified agent
                activeAgent = GetAgentByType(targetAgentType);

                // Update GameState.ActiveAgent if different
                if (gameState.ActiveAgentType != targetAgentType)
                {
                    _logger.LogInformation(
                        "Routing campaign {CampaignId} directly to {AgentType}",
                        campaignId, targetAgentType);
                    gameState.ActiveAgentType = targetAgentType;
                    await _stateManager.UpdateCampaignStateAsync(gameState);
                    //await _stateManager.SaveStateAsync(campaignId);
                }
            }
            else
            {
                activeAgent = GetAgentByType(gameState.ActiveAgentType);
            }

            _logger.LogInformation(
                "Processing message for campaign {CampaignId} from player {PlayerId} using {AgentType}",
                campaignId, playerId, activeAgent.AgentType);
            var model = await GetEffectiveModel(campaignId);
            // Process the message through the active agent

            var response = await ExecuteAgentMessageAsync(activeAgent, campaignId, playerId, message, model, streamCallback, clientMessageId);
            var updatedState = await _stateManager.GetCampaignStateAsync(campaignId);
            // Check for handoff
            if (currentActiveAgent == updatedState.ActiveAgentType) return response;
            _logger.LogInformation(
                "Agent handoff detected for campaign {CampaignId} from {SourceAgent} to {TargetAgent}",
                campaignId, currentActiveAgent, updatedState.ActiveAgentType);
            if (updatedState.Metadata.TryGetValue(HandoffContextKey, out var handoffContext))
            {
                _logger.LogInformation("Handoff context: {HandoffContext}", handoffContext);
                message = BuildHandoffMessage(currentActiveAgent, updatedState.ActiveAgentType, message,
                    handoffContext.ToString()!);
            }

            var newAgent = GetAgentByType(updatedState.ActiveAgentType);
            response = await ExecuteAgentMessageAsync(newAgent, campaignId, playerId, message, model, streamCallback, clientMessageId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing message for campaign {CampaignId} from player {PlayerId}",
                campaignId, playerId);

            return new AgentResponse
            {
                Success = false,
                Message = "I apologize, but I encountered an error processing your request. Please try again.",
                Error = ex.ToString()
            };
        }
        
        
    }

    private async Task<AgentResponse> ExecuteAgentMessageAsync(
        BaseGameAgent activeAgent,
        string campaignId,
        string playerId,
        string message,
        string? model,
        Func<MessageStreamUpdate, Task>? streamCallback,
        string? clientMessageId)
    {
        if (streamCallback is null)
        {
            return await activeAgent.ProcessMessageAsync(campaignId, message, playerId, model);
        }

        var aggregated = new StringBuilder();
        AgentSuggestedActions? suggested = null;

        void CaptureSuggestedActions(AgentSuggestedActions actions)
        {
            suggested = actions;
        }

        activeAgent.OnSuggestedActions += CaptureSuggestedActions;
        try
        {
            await foreach (var token in activeAgent.ProcessMessageStreamingAsync(campaignId, message, playerId, model))
            {
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                aggregated.Append(token);
                await streamCallback(new MessageStreamUpdate
                {
                    MessageId = clientMessageId ?? string.Empty,
                    AgentType = activeAgent.AgentType.ToString(),
                    Token = token,
                    IsCompleted = false
                });
            }

            await streamCallback(new MessageStreamUpdate
            {
                MessageId = clientMessageId ?? string.Empty,
                AgentType = activeAgent.AgentType.ToString(),
                IsCompleted = true,
                Token = string.Empty
            });

            return new AgentResponse
            {
                Success = true,
                FormattedResponse = new AgentFormattedResponse
                {
                    MessageToPlayers = aggregated.ToString(),
                    SuggestedActions = suggested?.SuggestedActions ?? []
                }
            };
        }
        catch (Exception ex)
        {
            await streamCallback(new MessageStreamUpdate
            {
                MessageId = clientMessageId ?? string.Empty,
                AgentType = activeAgent.AgentType.ToString(),
                IsCompleted = true,
                Token = string.Empty,
                Note = ex.Message
            });

            throw;
        }
        finally
        {
            activeAgent.OnSuggestedActions -= CaptureSuggestedActions;
        }
    }

    private void HandleSuggestedActions(AgentSuggestedActions actions)
    {
        
    }

    private void HandleTokenGenerated(string token)
    {
        //OnTokenGenerated?.Invoke(token);
    }

    private CampaignMessageQueue GetOrCreateQueue(string campaignId)
    {
        lock (_lock)
        {
            if (!_messageQueues.TryGetValue(campaignId, out var queue))
            {
                queue = new CampaignMessageQueue(ProcessQueuedMessageAsync, _logger);
                _messageQueues[campaignId] = queue;
            }

            return queue;
        }
    }

    private async Task<string?> GetEffectiveModel(string? scopeId)
    {
        return await _agentContextProvider.GetSessionOrCampaignModel(scopeId);
        
    }

    // # Reason: Centralize handoff formatting so future changes remain consistent.
    private static string BuildHandoffMessage(AgentType? sourceAgent, AgentType targetAgent, string originalMessage, string handoffContext)
    {
        var sourceLabel = sourceAgent?.ToString() ?? AgentType.GameMaster.ToString();
        return $"[Handoff from {sourceLabel} to {targetAgent}]\nOriginal User Message: {originalMessage}\n\n**Instructions from Game Master:** {handoffContext}";
    }

    private Task<AgentResponse> ProcessQueuedMessageAsync(PlayerMessageRequest request)
    {
        if (request.StreamCallback is null)
        {
            return ProcessMessageAsync(request.CampaignId, request.PlayerId, request.Message, request.TargetAgentType);
        }

        return ProcessMessageStreamingAsync(
            request.CampaignId,
            request.PlayerId,
            request.Message,
            request.TargetAgentType,
            request.StreamCallback,
            request.ClientMessageId);
    }

    private async Task<int> CalculatePriorityAsync(PlayerMessageRequest request)
    {
        var priority = 0;

        try
        {
            var gameState = await _stateManager.GetCampaignStateAsync(request.CampaignId).ConfigureAwait(false);
            if (gameState == null)
            {
                return priority;
            }

            if (!string.IsNullOrEmpty(request.CharacterId))
            {
                if (string.Equals(gameState.Campaign.CurrentTurnCharacterId, request.CharacterId, StringComparison.OrdinalIgnoreCase))
                {
                    priority += 100;
                }

                if (gameState is { IsInCombat: true, CurrentCombat: not null })
                {
                    var orderIndex = gameState.CurrentCombat.InitiativeOrder
                        .FindIndex(id => string.Equals(id, request.CharacterId, StringComparison.OrdinalIgnoreCase));

                    if (orderIndex >= 0 && gameState.CurrentCombat.InitiativeOrder.Count > 0)
                    {
                        var currentIndex = gameState.CurrentCombat.CurrentTurnIndex;
                        var distance = orderIndex - currentIndex;
                        if (distance < 0)
                        {
                            distance += gameState.CurrentCombat.InitiativeOrder.Count;
                        }

                        priority += Math.Max(0, 50 - distance * 10);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Message) && request.Message.Length < 140)
            {
                priority += 5;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate message priority for campaign {CampaignId}", request.CampaignId);
        }

        return priority;
    }
    
    /// <summary>
    /// Gets the currently active agent for a campaign from GameState
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <returns>The active agent instance</returns>
    public async Task<BaseGameAgent> GetActiveAgentAsync(string campaignId)
    {
        var gameState = await _stateManager.GetCampaignStateAsync(campaignId);
        return GetAgentByType(gameState.ActiveAgentType);
    }
    
    /// <summary>
    /// Gets the type name of the currently active agent for a campaign from GameState
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <returns>The agent type name</returns>
    public async Task<AgentType> GetActiveAgentTypeAsync(string campaignId)
    {
        var gameState = await _stateManager.GetCampaignStateAsync(campaignId);
        return gameState.ActiveAgentType;
    }

    /// <summary>
    /// Gets an agent instance by its type name
    /// </summary>
    private BaseGameAgent GetAgentByType(AgentType agentType)
    {
        return agentType switch
        {
            AgentType.GameMaster or AgentType.None => _gameMasterAgent,
            AgentType.Combat => _combatAgent,
            AgentType.CharacterCreation => _characterCreationAgent,
            AgentType.CharacterManager => _characterLevelUpAgent,
            AgentType.ShopKeeper => _economyManagerAgent,
            AgentType.WorldBuilder => _worldBuilderAgent,
            _ => throw new ArgumentException($"Unknown agent type: {agentType}", nameof(agentType))
        };
    }
}

/// <summary>
/// Handles prioritized sequential processing for a single campaign.
/// </summary>
internal sealed class CampaignMessageQueue(Func<PlayerMessageRequest, Task<AgentResponse>> processor, ILogger logger)
{
    private readonly object _sync = new();
    private readonly PriorityQueue<QueuedMessage, MessageQueueKey> _queue = new();
    private long _sequence;
    private bool _isProcessing;

    public Task<AgentResponse> EnqueueAsync(PlayerMessageRequest request, int priority)
    {
        var tcs = new TaskCompletionSource<AgentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var message = new QueuedMessage(request, priority, Interlocked.Increment(ref _sequence), tcs);

        int queuePosition;
        lock (_sync)
        {
            _queue.Enqueue(message, new MessageQueueKey(priority, message.Sequence));
            queuePosition = (_isProcessing ? 1 : 0) + _queue.Count - 1;

            if (!_isProcessing)
            {
                _isProcessing = true;
                _ = ProcessQueueAsync();
            }
        }

        _ = NotifyStatusAsync(message, MessageProcessingStatus.Queued, queuePosition);
        return tcs.Task;
    }

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            QueuedMessage next;

            lock (_sync)
            {
                if (_queue.Count == 0)
                {
                    _isProcessing = false;
                    return;
                }

                next = _queue.Dequeue();
            }

            try
            {
                await NotifyStatusAsync(next, MessageProcessingStatus.Processing, 0).ConfigureAwait(false);
                var response = await processor(next.Request).ConfigureAwait(false);
                next.TaskSource.TrySetResult(response);
                await NotifyStatusAsync(next, MessageProcessingStatus.Completed, 0).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Queued message failed for campaign {CampaignId}", next.Request.CampaignId);
                next.TaskSource.TrySetException(ex);
                await NotifyStatusAsync(next, MessageProcessingStatus.Failed, 0, ex.Message).ConfigureAwait(false);
            }
        }
    }

    private Task NotifyStatusAsync(QueuedMessage message, MessageProcessingStatus status, int? position, string? note = null)
    {
        if (message.Request.StatusCallback == null || string.IsNullOrEmpty(message.Request.ClientMessageId))
        {
            return Task.CompletedTask;
        }

        var update = new MessageQueueUpdate
        {
            MessageId = message.Request.ClientMessageId!,
            Status = status,
            Position = position,
            Note = note
        };

        return SafeInvokeCallbackAsync(message.Request.StatusCallback, update);
    }

    private async Task SafeInvokeCallbackAsync(Func<MessageQueueUpdate, Task> callback, MessageQueueUpdate update)
    {
        try
        {
            await callback(update).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Message status callback failed for message {MessageId}", update.MessageId);
        }
    }

    private sealed class QueuedMessage(
        PlayerMessageRequest request,
        int priority,
        long sequence,
        TaskCompletionSource<AgentResponse> taskSource)
    {
        public PlayerMessageRequest Request { get; } = request;
        public int Priority { get; } = priority;
        public long Sequence { get; } = sequence;
        public TaskCompletionSource<AgentResponse> TaskSource { get; } = taskSource;
    }

    private readonly struct MessageQueueKey(int priority, long sequence) : IComparable<MessageQueueKey>
    {
        private int Priority { get; } = -priority; // invert to ensure highest priority first
        private long Sequence { get; } = sequence;

        public int CompareTo(MessageQueueKey other)
        {
            var priorityComparison = Priority.CompareTo(other.Priority);
            if (priorityComparison != 0)
            {
                return priorityComparison;
            }

            return Sequence.CompareTo(other.Sequence);
        }
    }
}
