using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Messaging;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Services;
using AgenticRpg.Core.State;
using AgenticRpg.DiceRoller;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Hubs;

/// <summary>
/// SignalR hub for real-time game communication
/// Manages campaign groups and player interactions
/// </summary>
public class GameHub(
    IGameStateManager stateManager,
    ISessionStateManager sessionStateManager,
    IAgentContextProvider contextProvider,
    AgentOrchestrationService orchestrationService,
    IRollDiceService diceService,
    ILogger<GameHub> logger)
    : Hub
{
    // Track connected users and their campaigns
    private static readonly ConcurrentDictionary<string, string> _connectionToCampaign = [];
    private static readonly ConcurrentDictionary<string, HashSet<string>> _campaignConnections = [];
    // Track player to character mapping (connectionId -> (playerId, characterId, playerName))
    private static readonly ConcurrentDictionary<string, (string PlayerId, string CharacterId, string PlayerName)> _connectionToPlayer = [];

    /// <summary>
    /// Called when a client connects to the hub
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        if (_connectionToPlayer.TryGetValue(connectionId, out var playerInfo))
        {
            var playerId = playerInfo.PlayerId;
            logger.LogInformation("Player {PlayerId} disconnected from hub", playerId);
        }
        // Clean up connection tracking
        if (_connectionToCampaign.TryGetValue(connectionId, out var campaignId))
        {
            await LeaveCampaign(campaignId, playerInfo.PlayerId, playerInfo.CharacterId);
        }
        

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Joins a standalone session (for character creation, world building, etc.)
    /// Does not require a campaign or character - just establishes a SignalR connection for AI chat
    /// </summary>
    /// <param name="sessionId">Unique session ID (typically a GUID)</param>
    /// <param name="playerId">The player's ID</param>
    /// <param name="sessionName">Display name for this session</param>
    public async Task JoinSession(string sessionId, string playerId, string sessionName)
    {
        var connectionId = Context.ConnectionId;

        logger.LogInformation("Player {PlayerId} joining standalone session {SessionId} ({SessionName})",
            playerId, sessionId, sessionName);

        // Add to SignalR group
        await Groups.AddToGroupAsync(connectionId, GetCampaignGroupName(sessionId));

        // Track connection
        _connectionToCampaign[connectionId] = sessionId;
        _connectionToPlayer[connectionId] = (playerId, string.Empty, sessionName);

        if (!_campaignConnections.TryGetValue(sessionId, out var value))
        {
            value = [];
            _campaignConnections[sessionId] = value;
        }

        value.Add(connectionId);

        logger.LogInformation("Player {PlayerId} successfully joined session {SessionId}", playerId, sessionId);
    }

    public void ChangeModel(string? sessionOrCampaignId, string? modelId)
    {
        var scopeLabel = string.IsNullOrWhiteSpace(sessionOrCampaignId) ? "<global>" : sessionOrCampaignId;

        // Allow clients to clear overrides by sending empty/whitespace model IDs.
        var resolvedModel = string.IsNullOrWhiteSpace(modelId) ? null : modelId;
        orchestrationService.ChangeModel(sessionOrCampaignId, resolvedModel);

        logger.LogInformation(
            "Connection {ConnectionId} set model override for {ScopeId} to {ModelId}",
            Context.ConnectionId,
            scopeLabel,
            resolvedModel ?? "<default>");
    }
    /// <summary>
    /// Joins a campaign group
    /// </summary>
    /// <param name="campaignId">The campaign ID to join</param>
    /// <param name="playerId">The player's ID</param>
    /// <param name="characterId">The character ID this player controls</param>
    /// <param name="playerName">The player's display name</param>
    public async Task JoinCampaign(string campaignId, string playerId, string characterId, string playerName)
    {
        var connectionId = Context.ConnectionId;

        logger.LogInformation("Player {PlayerId} ({PlayerName}) joining campaign {CampaignId} with character {CharacterId}",
            playerId, playerName, campaignId, characterId);

        // Add to SignalR group
        await Groups.AddToGroupAsync(connectionId, GetCampaignGroupName(campaignId));

        // Track connection
        _connectionToCampaign[connectionId] = campaignId;
        _connectionToPlayer[connectionId] = (playerId, characterId, playerName);

        if (!_campaignConnections.TryGetValue(campaignId, out var value))
        {
            value = [];
            _campaignConnections[campaignId] = value;
        }

        value.Add(connectionId);

        // Update game state with player ready tracking
        try
        {
            var gameState = await stateManager.GetCampaignStateAsync(campaignId);

            if (gameState != null)
            {
                if (!gameState.Campaign.PlayerIds.Contains(playerId))
                    gameState.Campaign.PlayerIds.Add(playerId);
                if (!gameState.Campaign.CharacterIds.Contains(characterId))
                    gameState.Campaign.CharacterIds.Add(characterId);
                gameState.Campaign.PlayerIds = gameState.Campaign.PlayerIds.Distinct().ToList();
                gameState.Campaign.CharacterIds = gameState.Campaign.CharacterIds.Distinct().ToList();

                // Reason: A reconnect/late join should not reset an existing ready state.
                // Only create a new ready entry when missing; otherwise, just refresh metadata.
                var existingReady = gameState.GetReadyStatus(playerId);
                if (existingReady is null)
                {
                    gameState.SetReadyStatus(playerId, characterId, playerName, false, connectionId);
                }
                else
                {
                    existingReady.CharacterId = characterId;
                    existingReady.PlayerName = playerName;
                    existingReady.ConnectionId = connectionId;
                }
                await stateManager.UpdateCampaignStateAsync(gameState);
                logger.LogInformation("State Updated for: Player {PlayerId} ({PlayerName}) joined campaign {CampaignId} with character {CharacterId}",
                    playerId, playerName, campaignId, characterId);
                await BroadcastReadyStatusSnapshot(campaignId, gameState);

            }
            else
            {
                logger.LogWarning("Campaign {CampaignId} not found in game state", campaignId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating game state for player join");
        }

        // Notify others in the campaign
        await Clients.OthersInGroup(GetCampaignGroupName(campaignId))
            .SendAsync("PlayerJoined", playerId, characterId, playerName);
    }

    /// <summary>
    /// Leaves a session or campaign group
    /// </summary>
    /// <param name="sessionOrCampaignId">The session/campaign ID to leave</param>
    public async Task LeaveSession(string sessionOrCampaignId, string? playerId = null, string? characterId = null)
    {
        var connectionId = Context.ConnectionId;
        

        // Get player info before removing
        if (_connectionToPlayer.TryGetValue(connectionId, out var playerInfo))
        {
            playerId ??= playerInfo.PlayerId;
            characterId ??= playerInfo.CharacterId;
        }

        logger.LogInformation("Player {PlayerId} leaving session/campaign {SessionId}", playerId, sessionOrCampaignId);
       
        // Remove from SignalR group
        await Groups.RemoveFromGroupAsync(connectionId, GetCampaignGroupName(sessionOrCampaignId));

        // Clean up tracking
        _connectionToCampaign.TryRemove(connectionId, out _);
        _connectionToPlayer.TryRemove(connectionId, out _);

        if (_campaignConnections.TryGetValue(sessionOrCampaignId, out var connections))
        {
            connections.Remove(connectionId);
            if (connections.Count == 0)
            {
                _campaignConnections.TryRemove(sessionOrCampaignId, out _);
            }
        }

        // Try to update game state if this is a real campaign (not a standalone session)
        if (playerId != null)
        {
            try
            {
                var gameState = await stateManager.GetCampaignStateAsync(sessionOrCampaignId);
                if (gameState != null)
                {
                    foreach (var character in gameState.Characters.Where(x => x.PlayerId == playerId || x.Id == characterId))
                    {
                        character.CampaignId = string.Empty;
                        
                    }
                    gameState.PlayerLeaveGame(playerId);
                    
                    await stateManager.UpdateCampaignStateAsync(gameState);
                    await BroadcastReadyStatusSnapshot(sessionOrCampaignId, gameState);

                    // Notify others in the campaign
                    await Clients.OthersInGroup(GetCampaignGroupName(sessionOrCampaignId))
                        .SendAsync("PlayerLeft", playerId);
                    await BroadcastStateUpdate(sessionOrCampaignId, gameState);
                }
            }
            catch (Exception)
            {
                // Standalone session - no game state to update
                logger.LogDebug("Session {SessionId} leave - no game state (standalone session)", sessionOrCampaignId);
            }
        }
    }

    /// <summary>
    /// Leaves a campaign group (alias for LeaveSession for backwards compatibility)
    /// </summary>
    /// <param name="campaignId">The campaign ID to leave</param>
    /// <param name="playerId"></param>
    /// <param name="characterId"></param>
    public async Task LeaveCampaign(string campaignId, string playerId, string characterId)
    {
        await LeaveSession(campaignId, playerId, characterId);
    }

    /// <summary>
    /// Sends a message to the campaign and processes it through the AI agent orchestration
    /// </summary>
    /// <param name="sessionOrCampaignId">The session/campaign ID</param>
    /// <param name="playerId">The player sending the message</param>
    /// <param name="message">The message content</param>
    /// <param name="targetAgentType">Optional: specific agent to route message to (e.g., "CharacterCreation", "WorldBuilder")</param>
    /// <param name="clientMessageId">Optional client-generated identifier for status tracking</param>
    public async Task SendMessage(string sessionOrCampaignId, string playerId, string message,
        AgentType targetAgentType = AgentType.None, string? clientMessageId = null)
    {
        try
        {
            logger.LogInformation("Processing message from player {PlayerId} in session {SessionId}: {Message}",
                playerId, sessionOrCampaignId, message);
            await Clients.Group(GetCampaignGroupName(sessionOrCampaignId))
                .SendAsync("ReceiveMessage",
                    playerId,
                    new AgentResponse() { FormattedResponse = new AgentFormattedResponse() { MessageToPlayers = message } },
                    DateTime.UtcNow);
            var isSession = await contextProvider.IsSessionAsync(sessionOrCampaignId);
            AgentResponse response;

            if (isSession)
            {
                response = await orchestrationService.ProcessMessageAsync(
                    sessionOrCampaignId,
                    playerId,
                    message,
                    targetAgentType);
            }
            else
            {
                _connectionToPlayer.TryGetValue(Context.ConnectionId, out var playerInfo);

                var queueRequest = new PlayerMessageRequest
                {
                    CampaignId = sessionOrCampaignId,
                    PlayerId = playerId,
                    CharacterId = string.IsNullOrEmpty(playerInfo.CharacterId) ? null : playerInfo.CharacterId,
                    Message = message,
                    TargetAgentType = targetAgentType,
                    ClientMessageId = clientMessageId,
                    StatusCallback = CreateStatusCallback(Context.ConnectionId, clientMessageId)
                };

                response = await orchestrationService.EnqueueCampaignMessageAsync(queueRequest);
            }

            if (!response.Success)
            {
                logger.LogError("Agent processing failed for session {SessionId}: {Error}",
                    sessionOrCampaignId, response.Error);

                // Send error message back to the sender
                await Clients.Caller.SendAsync("ReceiveMessage",
                    "System",
                    response,
                    DateTime.UtcNow);
                return;
            }

            var activeAgentType = await orchestrationService.GetActiveAgentTypeAsync(sessionOrCampaignId);
            await Clients.Group(GetCampaignGroupName(sessionOrCampaignId))
                .SendAsync("ReceiveMessage",
                    activeAgentType,
                    response,
                    DateTime.UtcNow);

            if (isSession)
            {
                // Get and broadcast session state
                var sessionState = await contextProvider.GetSessionStateAsync(sessionOrCampaignId);
                if (sessionState != null)
                {
                    await BroadcastSessionStateUpdate(sessionOrCampaignId, sessionState);
                }
            }
            else
            {
                // Campaign: broadcast game state
                var gameState = await contextProvider.GetGameStateAsync(sessionOrCampaignId);
                await BroadcastStateUpdate(sessionOrCampaignId, gameState);

                // Check for new narratives in the updated game state
                //var gameState = response.UpdatedGameState;
                if (gameState.RecentNarratives.Count != 0)
                {
                    foreach (var narrative in gameState.RecentNarratives)
                    {
                        await BroadcastNarrative(sessionOrCampaignId, narrative);
                    }
                }

                // Check if combat started
                if (gameState.IsInCombat && gameState.CurrentCombat != null)
                {
                    await BroadcastCombatStart(sessionOrCampaignId, gameState.CurrentCombat);
                }
            }

            logger.LogInformation("Successfully processed message from player {PlayerId} using agent {AgentType}",
                playerId, activeAgentType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing message from player {PlayerId} in session {SessionId}",
                playerId, sessionOrCampaignId);

            await Clients.Caller.SendAsync("ReceiveMessage",
                "System",
                new AgentResponse() { FormattedResponse = new AgentFormattedResponse() { MessageToPlayers = "An error occurred processing your message. Please try again." } },
                DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Streams PCM audio generated from the provided text.
    /// </summary>
    /// <param name="text">The text to synthesize.</param>
    /// <param name="cancellationToken">Cancellation token to stop streaming.</param>
    /// <returns>A stream of PCM audio chunks.</returns>
    public async IAsyncEnumerable<byte[]> StreamSpeech(string text, string messageId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling Speech Synthesis on server \n\n---\n\n {Text}", text);
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var chunksSent = 0;
        // # Reason: Stream chunks as they are generated to minimize latency to the client.
        await foreach (var chunk in TextToSpeechService.GenerateSpeechAsync(text, messageId).WithCancellation(cancellationToken))
        {
            yield return chunk;
            chunksSent++;
            logger.LogInformation("Sent speech chunk {ChunkIndex}", chunksSent);
        }
    }

    /// <summary>
    /// Updates a player's ready status in the lobby
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="playerId">The player ID</param>
    /// <param name="isReady">Whether the player is ready</param>
    public async Task UpdateReadyStatus(string campaignId, string playerId, bool isReady)
    {
        logger.LogInformation("Player {PlayerId} in campaign {CampaignId} set ready status to {IsReady}",
            playerId, campaignId, isReady);

        try
        {
            // Update game state
            var gameState = await stateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                logger.LogWarning("Campaign {CampaignId} not found when updating ready status", campaignId);
                return;
            }

            // Get player info
            var connectionId = Context.ConnectionId;
            if (!_connectionToPlayer.TryGetValue(connectionId, out var playerInfo))
            {
                logger.LogWarning("Player info not found for connection {ConnectionId}", connectionId);
                return;
            }

            // Update ready status
            gameState.SetReadyStatus(playerId, playerInfo.CharacterId, playerInfo.PlayerName, isReady, connectionId);
            await stateManager.UpdateCampaignStateAsync(gameState);
            await BroadcastReadyStatusSnapshot(campaignId, gameState);

            // Broadcast ready status update to all players
            await Clients.Group(GetCampaignGroupName(campaignId))
                .SendAsync("ReadyStatusUpdated", playerId, isReady);

            // Check if all players are ready
            var allReady = gameState.AreAllPlayersReady();

            logger.LogInformation("Campaign {CampaignId} all players ready: {AllReady} ({ReadyCount}/{TotalCount})",
                campaignId, allReady,
                gameState.PlayerReadyStatuses.Count(s => s.Value.IsReady),
                gameState.PlayerReadyStatuses.Count);

            // Broadcast all-ready status
            await Clients.Group(GetCampaignGroupName(campaignId))
                .SendAsync("AllPlayersReady", allReady);

            // If all ready, automatically start the campaign
            if (allReady)
            {
                logger.LogInformation("All players ready in campaign {CampaignId}, starting campaign", campaignId);
                await StartCampaign(campaignId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating ready status for player {PlayerId} in campaign {CampaignId}",
                playerId, campaignId);
        }
    }

    /// <summary>
    /// Manually starts a campaign (can be called by campaign owner or when all ready)
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    public async Task StartCampaign(string campaignId)
    {
        logger.LogInformation("Starting campaign {CampaignId}", campaignId);

        try
        {
            var gameState = await stateManager.GetCampaignStateAsync(campaignId);
            if (gameState == null)
            {
                logger.LogWarning("Campaign {CampaignId} not found when starting", campaignId);
                return;
            }

            // Reset ready statuses now that campaign is starting
            gameState.ResetReadyStatuses();
            await stateManager.UpdateCampaignStateAsync(gameState);
            await BroadcastReadyStatusSnapshot(campaignId, gameState);

            // Notify all clients that campaign has started
            await Clients.Group(GetCampaignGroupName(campaignId))
                .SendAsync("CampaignStarted", campaignId);

            logger.LogInformation("Campaign {CampaignId} started successfully", campaignId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error starting campaign {CampaignId}", campaignId);
        }
    }

    /// <summary>
    /// Gets the complete game state for a campaign
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <returns>The complete game state</returns>
    public async Task<GameState?> GetGameState(string campaignId)
    {
        try
        {
            logger.LogInformation("Retrieving game state for campaign: {CampaignId}", campaignId);
            var state = await stateManager.GetCampaignStateAsync(campaignId);

            if (state == null)
            {
                logger.LogWarning("Game state not found for campaign: {CampaignId}", campaignId);
            }

            return state;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving game state for campaign: {CampaignId}", campaignId);
            return null;
        }
    }

    /// <summary>
    /// Completes a session and saves the resulting entity
    /// </summary>
    /// <param name="sessionId">The session ID to complete</param>
    /// <param name="resultEntityId">The ID of the created entity (character, world, etc.)</param>
    public async Task CompleteSession(string sessionId, string resultEntityId)
    {
        logger.LogInformation("Completing session {SessionId} with result entity {EntityId}",
            sessionId, resultEntityId);

        try
        {
            // Mark session as completed
            await sessionStateManager.CompleteSessionAsync(sessionId, resultEntityId);

            // Notify clients that session is completed
            await Clients.Group(GetCampaignGroupName(sessionId))
                .SendAsync("SessionCompleted", sessionId, resultEntityId);

            logger.LogInformation("Session {SessionId} completed successfully", sessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error completing session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Broadcasts session state update to all session members
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="sessionState">The updated session state</param>
    public async Task BroadcastSessionStateUpdate(string sessionId, SessionState sessionState)
    {
        await Clients.Group(GetCampaignGroupName(sessionId))
            .SendAsync("SessionStateUpdated", sessionState);
    }

    private Func<MessageQueueUpdate, Task>? CreateStatusCallback(string connectionId, string? clientMessageId)
    {
        if (string.IsNullOrEmpty(connectionId) || string.IsNullOrEmpty(clientMessageId))
        {
            return null;
        }

        return update =>
        {
            var outboundId = string.IsNullOrEmpty(update.MessageId) ? clientMessageId : update.MessageId;
            return Clients.Client(connectionId)
                .SendAsync("MessageStatusChanged",
                    outboundId,
                    update.Status.ToString(),
                    update.Position,
                    update.Note);
        };
    }

    /// <summary>
    /// Broadcasts the current ready status dictionary to campaign members
    /// </summary>
    private async Task BroadcastReadyStatusSnapshot(string campaignId, GameState? gameState)
    {
        if (gameState == null)
        {
            return;
        }

        var snapshot = gameState.PlayerReadyStatuses
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new PlayerReadyStatus
                {
                    PlayerId = kvp.Value.PlayerId,
                    CharacterId = kvp.Value.CharacterId,
                    PlayerName = kvp.Value.PlayerName,
                    IsReady = kvp.Value.IsReady,
                    ReadyAt = kvp.Value.ReadyAt,
                    ConnectionId = kvp.Value.ConnectionId
                });

        await Clients.Group(GetCampaignGroupName(campaignId))
            .SendAsync("ReadyStatusSnapshot", snapshot);
    }

    /// <summary>
    /// Broadcasts game state update to all campaign members
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="gameState">The updated game state</param>
    public async Task BroadcastStateUpdate(string campaignId, GameState gameState)
    {
        await Clients.Group(GetCampaignGroupName(campaignId))
            .SendAsync("StateUpdated", gameState);
    }

    /// <summary>
    /// Broadcasts narrative update to campaign members
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="narrative">The new narrative entry</param>
    public async Task BroadcastNarrative(string campaignId, Narrative narrative)
    {
        // Determine which clients should receive this narrative
        if (narrative.Visibility == Models.Enums.NarrativeVisibility.Global)
        {
            // Send to all players in campaign
            await Clients.Group(GetCampaignGroupName(campaignId))
                .SendAsync("NarrativeAdded", narrative);
        }
        else if (narrative.Visibility == Models.Enums.NarrativeVisibility.CharacterSpecific
            && !string.IsNullOrEmpty(narrative.TargetCharacterId))
        {
            // Send only to specific character's player - find their connection
            var state = await GetGameState(campaignId);
            var targetCharacter = state.Characters.FirstOrDefault(c => c.Id == narrative.TargetCharacterId || c.Name == narrative.TargetCharacterId);
            var targetConnection = _connectionToPlayer
                .FirstOrDefault(kvp => kvp.Value.CharacterId == targetCharacter?.Id);

            if (!string.IsNullOrEmpty(targetConnection.Key))
            {
                await Clients.Client(targetConnection.Key)
                    .SendAsync("NarrativeAdded", narrative);

                logger.LogInformation("Sent character-specific narrative to player controlling character {CharacterId}",
                    narrative.TargetCharacterId);
            }
            else
            {
                logger.LogWarning("Could not find connection for character {CharacterId} to send narrative",
                    narrative.TargetCharacterId);
            }
        }
        // GM-only narratives are not broadcast to clients
    }

    /// <summary>
    /// Broadcasts combat start event
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="combatEncounter">The combat encounter</param>
    public async Task BroadcastCombatStart(string campaignId, CombatEncounter combatEncounter)
    {
        await Clients.Group(GetCampaignGroupName(campaignId))
            .SendAsync("CombatStarted", combatEncounter);
    }

    /// <summary>
    /// Broadcasts turn change in combat
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="currentCombatantId">ID of the combatant whose turn it is</param>
    /// <param name="combatantName">Name of the current combatant</param>
    public async Task BroadcastTurnChange(string campaignId, string currentCombatantId, string combatantName)
    {
        await Clients.Group(GetCampaignGroupName(campaignId))
            .SendAsync("TurnChanged", currentCombatantId, combatantName);
    }


    /// <summary>
    /// Client submits dice roll result back to server
    /// This completes the TaskCompletionSource in DiceRollerTools
    /// </summary>
    /// <param name="diceRollResults">The result of the dice rolls</param>
    public async Task SubmitDiceRollResult(RollDiceResultsList resultsList)
    {
        var diceRollResults = resultsList.Results;
        foreach (var diceRollResult in diceRollResults)
        {
            try
            {
                logger.LogInformation("Completing dice roll request {RequestId} (Session {SessionId})",
                    diceRollResult.RequestId, diceRollResult.SessionId);

                // Find the modal reference by RequestId and close it
                var modalRef = diceService.DieRollReferences.FirstOrDefault(r =>
                    string.Equals(r.RequestId, diceRollResult.RequestId, StringComparison.OrdinalIgnoreCase));

                if (modalRef != null)
                {
                    diceService.Close(modalRef.Id, diceRollResult);
                }
                else
                {
                    logger.LogDebug(
                        "No modal reference found for request {RequestId} (already completed or not owned by this server instance)",
                        diceRollResult.RequestId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to complete dice roll request {RequestId}", diceRollResult.RequestId);
            }
        }

        // return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the SignalR group name for a campaign
    /// </summary>
    internal static string GetCampaignGroupName(string campaignId) => $"campaign_{campaignId}";

    /// <summary>
    /// Gets the number of connected clients for a campaign
    /// </summary>
    public static int GetCampaignConnectionCount(string campaignId)
    {
        return _campaignConnections.TryGetValue(campaignId, out var connections)
            ? connections.Count
            : 0;
    }
}

public class RollDiceResultsList
{
    [JsonPropertyName("results")]
    public List<RollDiceResults> Results { get; set; } = [];
}
