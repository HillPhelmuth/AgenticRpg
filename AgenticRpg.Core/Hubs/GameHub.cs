using System.Runtime.CompilerServices;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Services;
using AgenticRpg.Core.State;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Hubs;

[Authorize]
public class GameHub(IGameStateManager stateManager, ISessionStateManager sessionStateManager,
    IAgentContextProvider contextProvider, AgentOrchestrationService orchestrationService,
    IRollDiceService diceService, ILogger<GameHub> logger, GameConnectionRegistry connections,
    IPlayerStateProjector projector, PlayerStateBroadcaster broadcaster, GameRuntimeCoordinator runtime,
    ICharacterRepository characters, ICampaignRepository campaigns, RollDiceHubService pendingDice,
    ISessionCompletionService completionService, ScopeOperationLocks locks) : Hub
{
    private string UserId => PlayerIdentity.GetId(Context.User) ?? throw new HubException("Sign in to continue.");

    private void ValidatePlayer(string playerId)
    {
        if (playerId != UserId) throw new HubException("The player does not match the signed-in user.");
    }

    private GameConnectionBinding Joined(string scopeId)
    {
        var binding = connections.Get(Context.ConnectionId);
        if (binding is null || binding.ScopeId != scopeId || binding.PlayerId != UserId)
            throw new HubException("Join this session before performing an action.");
        return binding;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        using var membershipLease = await connections.LockAsync();
        var binding = connections.Detach(Context.ConnectionId);
        if (binding is not null) await MarkDisconnectedAsync(binding);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<ScopeRecoverySnapshot> JoinSession(string sessionId, string playerId, string sessionName, string leaseId)
    {
        using var membershipLease = await connections.LockAsync();
        ValidatePlayer(playerId);
        var session = await sessionStateManager.GetSessionStateAsync(sessionId);
        if (session is null || session.PlayerId != UserId) throw new HubException("Session not found or access denied.");
        await completionService.ReconcileAsync(sessionId);
        await AttachAsync(new(sessionId, UserId, string.Empty, sessionName, true, leaseId));
        await runtime.RecoverAsync(sessionId);
        var transcript = await runtime.TranscriptAsync(sessionId);
        var durable = await runtime.RecoveryInfoAsync(sessionId);
        return new ScopeRecoverySnapshot
        {
            ScopeId = sessionId, RequiresReview = await runtime.RequiresReviewAsync(sessionId, UserId), HasAcceptedRequests = runtime.HasAcceptedRequests(sessionId), SessionState = session, Messages = transcript.Messages, TranscriptCursor = transcript.Cursor, DurableRevision = durable.Revision, LastSavedAt = durable.SavedAt,
            PendingDice = pendingDice.GetPending(sessionId, UserId)
        };
    }

    public async Task<ScopeRecoverySnapshot> JoinCampaign(string campaignId, string playerId, string characterId, string playerName, string leaseId)
    {
        using var membershipLease = await connections.LockAsync();
        ValidatePlayer(playerId);
        var character = await characters.GetByIdAsync(characterId);
        var campaign = await campaigns.GetByIdAsync(campaignId);
        if (campaign is null || character is null || character.PlayerId != UserId || character.CampaignId != campaignId ||
            !(campaign.OwnerId == UserId || campaign.InvitedUserIds.Contains(UserId) || campaign.PlayerIds.Contains(UserId)))
            throw new HubException("Campaign or character access denied.");
        await AttachAsync(new(campaignId, UserId, character.Id, character.Name, false, leaseId));
        var state = await stateManager.GetCampaignStateAsync(campaignId) ?? throw new HubException("Campaign not found.");
        state.PlayerJoinGame(UserId, character.Id, character.Name, Context.ConnectionId);
        await stateManager.UpdateCampaignStateAsync(state);
        await BroadcastReadyStatusSnapshot(campaignId, state);
        await Clients.OthersInGroup(GetCampaignGroupName(campaignId)).SendAsync("PlayerJoined", campaignId, UserId, character.Id, character.Name);
        await runtime.RecoverAsync(campaignId);
        var transcript = await runtime.TranscriptAsync(campaignId, characterId: character.Id);
        var durable = await runtime.RecoveryInfoAsync(campaignId);
        return new ScopeRecoverySnapshot
        {
            ScopeId = campaignId, RequiresReview = await runtime.RequiresReviewAsync(campaignId, UserId), HasAcceptedRequests = runtime.HasAcceptedRequests(campaignId), GameState = projector.Project(state, character.Id), Messages = transcript.Messages, TranscriptCursor = transcript.Cursor, DurableRevision = durable.Revision, LastSavedAt = durable.SavedAt,
            PendingDice = pendingDice.GetPending(campaignId, UserId)
        };
    }

    private async Task AttachAsync(GameConnectionBinding binding)
    {
        var old = connections.Get(Context.ConnectionId);
        if (old is not null && old.ScopeId != binding.ScopeId)
        {
            connections.Detach(Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetCampaignGroupName(old.ScopeId));
            await MarkDisconnectedAsync(old);
        }
        connections.Attach(Context.ConnectionId, binding);
        await Groups.AddToGroupAsync(Context.ConnectionId, GetCampaignGroupName(binding.ScopeId));
    }

    public async Task DetachScope(string scopeId, string leaseId)
    {
        using var membershipLease = await connections.LockAsync();
        var current = connections.Get(Context.ConnectionId);
        if (current is null || current.ScopeId != scopeId || current.PlayerId != UserId) return;
        var binding = connections.Detach(Context.ConnectionId, leaseId);
        if (binding is null) return; // A replacement page has acquired a newer lease.
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetCampaignGroupName(scopeId));
        await MarkDisconnectedAsync(binding);
    }

    // Compatibility: leaving a session is transport-only. Withdrawal is always explicit.
    public Task LeaveSession(string scopeId) => DetachScope(scopeId, connections.Get(Context.ConnectionId)?.LeaseId ?? string.Empty);

    public async Task LeaveCampaign(string campaignId, string playerId, string characterId)
    {
        using var membershipLease = await connections.LockAsync();
        ValidatePlayer(playerId);
        var character = await characters.GetByIdAsync(characterId);
        if (character is null || character.PlayerId != UserId ||
            (!string.IsNullOrEmpty(character.CampaignId) && character.CampaignId != campaignId))
            throw new HubException("Character access denied.");
        if (character.CampaignId == campaignId)
        {
            character.CampaignId = string.Empty;
            try { await characters.UpdateAsync(character); }
            catch { character.CampaignId = campaignId; throw; }
        }
        var state = await stateManager.GetCampaignStateAsync(campaignId);
        if (state is null) return;
        state.PlayerLeaveGame(UserId, characterId);
        foreach (var connection in connections.InScope(campaignId).Where(c => c.Value.PlayerId == UserId))
        {
            connections.Detach(connection.Key, connection.Value.LeaseId);
            await Groups.RemoveFromGroupAsync(connection.Key, GetCampaignGroupName(campaignId));
        }
        await stateManager.UpdateCampaignStateAsync(state);
        await BroadcastReadyStatusSnapshot(campaignId, state);
        await Clients.Group(GetCampaignGroupName(campaignId)).SendAsync("PlayerLeft", campaignId, UserId);
    }

    private async Task MarkDisconnectedAsync(GameConnectionBinding binding)
    {
        if (binding.IsSession || connections.HasPlayer(binding.ScopeId, binding.PlayerId)) return;
        var state = await stateManager.GetCampaignStateAsync(binding.ScopeId);
        if (state is null) return;
        state.PlayerDisconnect(binding.PlayerId);
        await stateManager.UpdateCampaignStateAsync(state);
        await BroadcastReadyStatusSnapshot(binding.ScopeId, state);
        await Clients.Group(GetCampaignGroupName(binding.ScopeId)).SendAsync("PlayerDisconnected", binding.PlayerId);
    }

    public async Task<RuntimeMessage> SendMessage(string scopeId, string playerId, string message,
        AgentType targetAgentType = AgentType.None, string? clientMessageId = null)
    {
        ValidatePlayer(playerId);
        var binding = Joined(scopeId);
        if (string.IsNullOrWhiteSpace(clientMessageId)) throw new HubException("A request ID is required.");
        using var lease = binding.IsSession ? await locks.AcquireCompletionAsync(scopeId) : null;
        var existing = runtime.Find(scopeId, UserId, clientMessageId);
        if (existing is not null) return existing;
        if (binding.IsSession)
        {
            var session = await sessionStateManager.GetSessionStateAsync(scopeId);
            if (session?.PlayerId != UserId || session.IsCompleted) throw new HubException("This creation session has finished.");
        }
        return await runtime.SubmitAsync(scopeId, UserId, binding.CharacterId, binding.PlayerName, message,
            targetAgentType, clientMessageId, binding.IsSession);
    }

    public async Task<RuntimeMessage> SendAction(string scopeId, string playerId, PlayerActionIntent intent, string clientMessageId)
    {
        ValidatePlayer(playerId);
        var binding = Joined(scopeId);
        if (binding.IsSession) throw new HubException("Actions belong in an adventure, not character creation.");
        var existing = runtime.Find(scopeId, UserId, clientMessageId);
        if (existing is not null) return existing;
        var state = await stateManager.GetCampaignStateAsync(scopeId) ?? throw new HubException("Campaign not found.");
        var actor = state.Characters.FirstOrDefault(c => c.Id == binding.CharacterId && c.PlayerId == UserId)
            ?? throw new HubException("Character access denied.");
        string summary;
        try { summary = PlayerActionRules.Describe(state, actor, intent); }
        catch (InvalidOperationException ex) { throw new HubException(ex.Message); }
        return await runtime.SubmitAsync(scopeId, UserId, actor.Id, actor.Name, summary, AgentType.GameMaster, clientMessageId, false, intent);
    }

    public Task<RuntimeMessage> CancelRequest(string scopeId, string clientMessageId)
    {
        Joined(scopeId);
        return runtime.CancelAsync(scopeId, UserId, clientMessageId);
    }

    public async Task UpdateReadyStatus(string campaignId, string playerId, bool isReady)
    {
        using var membershipLease = await connections.LockAsync();
        ValidatePlayer(playerId);
        var binding = Joined(campaignId);
        var state = await stateManager.GetCampaignStateAsync(campaignId) ?? throw new HubException("Campaign not found.");
        state.SetReadyStatus(UserId, binding.CharacterId, binding.PlayerName, isReady, Context.ConnectionId);
        await stateManager.UpdateCampaignStateAsync(state);
        await BroadcastReadyStatusSnapshot(campaignId, state);
        await Clients.Group(GetCampaignGroupName(campaignId)).SendAsync("ReadyStatusUpdated", campaignId, UserId, isReady);
        var allReady = state.AreAllPlayersReady();
        await Clients.Group(GetCampaignGroupName(campaignId)).SendAsync("AllPlayersReady", campaignId, allReady);
        if (allReady) await StartCampaignCoreAsync(campaignId, state);
    }

    public async Task StartCampaign(string campaignId)
    {
        using var membershipLease = await connections.LockAsync();
        Joined(campaignId);
        var state = await stateManager.GetCampaignStateAsync(campaignId) ?? throw new HubException("Campaign not found.");
        if (state.Campaign.OwnerId != UserId && !state.AreAllPlayersReady()) throw new HubException("The party is not ready.");
        await StartCampaignCoreAsync(campaignId, state);
    }

    private async Task StartCampaignCoreAsync(string campaignId, GameState state)
    {
        if (state.Campaign.StartedAt is not null) return;
        state.Campaign.MarkStarted();
        state.ResetReadyStatuses();
        await stateManager.UpdateCampaignStateAsync(state);
        await BroadcastReadyStatusSnapshot(campaignId, state);
        await Clients.Group(GetCampaignGroupName(campaignId)).SendAsync("CampaignStarted", campaignId);
    }

    public async Task<GameState?> GetGameState(string campaignId)
    {
        var binding = Joined(campaignId);
        var character = await characters.GetByIdAsync(binding.CharacterId);
        if (character?.PlayerId != UserId || character.CampaignId != campaignId) throw new HubException("Character access denied.");
        var state = await stateManager.GetCampaignStateAsync(campaignId);
        return state is null ? null : projector.Project(state, binding.CharacterId);
    }

    public async Task CompleteSession(string sessionId, string resultEntityId)
    {
        Joined(sessionId);
        var session = await sessionStateManager.GetSessionStateAsync(sessionId) ?? throw new HubException("Session not found.");
        if (runtime.HasPending(sessionId) && !session.IsCompleted) throw new HubException("Wait for generation to finish before saving.");
        _ = session.SessionType switch
        {
            SessionType.CharacterCreation => await completionService.FinalizeCharacterAsync(sessionId, requireIdle: true),
            SessionType.WorldBuilding => await completionService.FinalizeWorldAsync(sessionId, requireIdle: true),
            SessionType.CampaignSetup => await completionService.FinalizeCampaignAsync(sessionId, requireIdle: true),
            _ => throw new HubException("Unsupported session type.")
        };
    }

    public async Task ChangeModel(string? scopeId, string? modelId)
    {
        if (string.IsNullOrEmpty(scopeId)) throw new HubException("Select a session before changing its model.");
        Joined(scopeId);
        await orchestrationService.ChangeModel(scopeId, modelId);
    }

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


    private Task BroadcastReadyStatusSnapshot(string campaignId, GameState state) =>
        Clients.Group(GetCampaignGroupName(campaignId)).SendAsync("ReadyStatusSnapshot", campaignId, state.PlayerReadyStatuses);

    internal Task BroadcastStateUpdate(string campaignId, GameState state) => broadcaster.StateAsync(state);
    internal Task BroadcastNarrative(string campaignId, Narrative narrative) => broadcaster.NarrativeAsync(campaignId, narrative);
    internal Task BroadcastSessionStateUpdate(string sessionId, SessionState state) =>
        Clients.Group(GetCampaignGroupName(sessionId)).SendAsync("SessionStateUpdated", state);

    public async Task SubmitDiceRollResult(RollDiceResultsList results)
    {
        foreach (var result in results.Results)
        {
            var session = await sessionStateManager.GetSessionStateAsync(result.SessionId);
            var allowed = session is not null ? session.PlayerId == UserId
                : (await characters.GetByPlayerIdAsync(UserId)).Any(c => c.CampaignId == result.SessionId);
            if (!allowed) throw new HubException("Dice request access denied.");
            pendingDice.Submit(result, UserId);
        }
    }

    internal static string GetCampaignGroupName(string scopeId) => $"campaign_{scopeId}";
}

public class RollDiceResultsList
{
    [JsonPropertyName("results")]
    public List<RollDiceResults> Results { get; set; } = [];
}
