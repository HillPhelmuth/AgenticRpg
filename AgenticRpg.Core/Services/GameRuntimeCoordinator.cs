using System.Collections.Concurrent;
using System.Text.Json;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Hubs;
using AgenticRpg.Core.Messaging;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.State;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Core.Services;

public interface IGameMessageProcessor
{
    Task<AgentResponse> ProcessAsync(PlayerMessageRequest request, bool isSession);
}

public sealed class GameMessageProcessor(AgentOrchestrationService orchestration) : IGameMessageProcessor
{
    public Task<AgentResponse> ProcessAsync(PlayerMessageRequest request, bool isSession) => isSession
        ? orchestration.EnqueueSessionMessageAsync(request)
        : orchestration.EnqueueCampaignMessageAsync(request);
}

/// <summary>Owns accepted work independently of the SignalR connection that submitted it.</summary>
public sealed class GameRuntimeCoordinator(IGameMessageProcessor processor, IHubContext<GameHub> hub,
    IGameStateManager states, ISessionStateManager sessions, PlayerStateBroadcaster broadcaster,
    ILogger<GameRuntimeCoordinator> logger, ScopeOperationLocks locks, TimeProvider? clock = null, IRecoveryRepository? recovery = null, IAgentSessionStore? threads = null, RuntimeProgress? progress = null)
{
    private readonly IRecoveryRepository _recovery = recovery ?? new InMemoryRecoveryRepository();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _scopeGates = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, ScopeMessages> _scopes = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();

    public async Task<RuntimeMessage> CancelAsync(string scopeId, string playerId, string clientMessageId)
    {
        await RecoverAsync(scopeId);
        var gate = _scopeGates.GetOrAdd(scopeId, _ => new(1, 1));
        await gate.WaitAsync();
        try
        {
        var scope = _scopes[scopeId];
        RuntimeMessage message;
        lock (scope.Sync)
        {
            if (!scope.Messages.TryGetValue((playerId, clientMessageId), out message!))
                throw new HubException("That request does not belong to you or is no longer active.");
            if (message.IsComplete) return Copy(message);
            if (_cancellations.TryGetValue(message.Id, out var cancellation)) cancellation.Cancel();
        }
        await PublishAsync(scope, message, m => { m.Phase = ProcessingPhase.Cancelling; m.ProgressDetail = "Stopping… Already applied effects will remain."; });
        lock (scope.Sync) return Copy(message);
        }
        finally { gate.Release(); }
    }

    // Compatibility for synchronous callers; production hub awaits acceptance persistence.
    public RuntimeMessage Submit(string scopeId, string playerId, string characterId, string playerName,
        string text, AgentType agentType, string clientMessageId, bool isSession) =>
        SubmitAsync(scopeId, playerId, characterId, playerName, text, agentType, clientMessageId, isSession).GetAwaiter().GetResult();

    public async Task RecoverAsync(string scopeId)
    {
        var gate = _scopeGates.GetOrAdd(scopeId, _ => new(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_scopes.TryGetValue(scopeId, out var existingScope))
            {
                List<RuntimeMessage> retry;
                lock (existingScope.Sync) retry = existingScope.Messages.Values.Where(m => m.Failure?.Code == "checkpoint_failed").Select(Copy).ToList();
                foreach (var previous in retry)
                {
                    RuntimeMessage snapshot;
                    lock (existingScope.Sync)
                    {
                        if (!existingScope.Messages.TryGetValue((previous.PlayerId, previous.ClientMessageId), out var live) || live.Failure?.Code != "checkpoint_failed") continue;
                        live.Failure = null; live.Error = null; live.Revision++;
                        live.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                        snapshot = Copy(live);
                    }
                    try
                    {
                        await PersistAsync(existingScope, snapshot, force: true);
                        await broadcaster.MessageAsync(snapshot);
                        lock (existingScope.Sync)
                        {
                            existingScope.Finalizing.Remove(snapshot.Id);
                            if (existingScope.HeldLocks.Remove(snapshot.Id)) locks.EndRequest(scopeId);
                        }
                    }
                    catch
                    {
                        lock (existingScope.Sync)
                        {
                            if (existingScope.Messages.TryGetValue((snapshot.PlayerId, snapshot.ClientMessageId), out var live))
                            { live.Failure = previous.Failure; live.Error = previous.Error; }
                        }
                        throw;
                    }
                }
                return;
            }
            var scope = new ScopeMessages();
            foreach (var d in await _recovery.RecoverMessagesAsync(scopeId))
            {
                var m = d.Read<RuntimeMessage>();
                if (!m.IsComplete)
                {
                    m.Status = MessageProcessingStatus.Interrupted;
                    m.Phase = ProcessingPhase.Interrupted;
                    m.Error = "This action was interrupted by a server restart. Some effects may already have occurred. Review the current state before continuing.";
                    m.Failure = new("action_interrupted", m.Error, m.ClientMessageId, RecoveryAction.ReviewState);
                    m.Revision++;
                    m.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                    await _recovery.WriteAsync(Document(m), d.Version);
                }
                scope.Messages[(m.PlayerId, m.ClientMessageId)] = m;
                scope.Persisted[m.Id] = m.Revision;
                scope.HasAcceptedRequests = true;
            }
            Prune(scope);
            _scopes[scopeId] = scope;
        }
        finally { gate.Release(); }
    }

    public async Task<TranscriptPage> TranscriptAsync(string scopeId, string? cursor = null, int take = 50, string? characterId = null)
    {
        await RecoverAsync(scopeId);
        var page = await _recovery.TranscriptAsync(scopeId, cursor, take, characterId);
        var live = Snapshot(scopeId).ToDictionary(m => m.Id);
        page.Messages = page.Messages.Select(m => live.GetValueOrDefault(m.Id) ?? m).ToList();
        return page;
    }

    public async Task<(long Revision, DateTime? SavedAt)> RecoveryInfoAsync(string scopeId)
    {
        return await _recovery.InfoAsync(scopeId);
    }

    public async Task<bool> RequiresReviewAsync(string scopeId, string playerId) =>
        (await _recovery.ReviewMessagesAsync(scopeId, playerId)).Count > 0;

    public async Task ReviewAsync(string scopeId, string playerId)
    {
        await RecoverAsync(scopeId);
        var scope = _scopes[scopeId];
        foreach (var d in await _recovery.ReviewMessagesAsync(scopeId, playerId))
        {
            var m = d.Read<RuntimeMessage>();
            if (!m.NeedsReview) continue;
            scope.Persisted[m.Id] = d.Version;
            m.Reviewed = true; m.Revision++;
            await PersistAsync(scope, m, force: true);
            lock (scope.Sync) scope.Messages[(playerId, m.ClientMessageId)] = m;
        }
    }

    public async Task DeleteScopeAsync(string scopeId)
    {
        if (HasPending(scopeId)) throw new InvalidOperationException("Wait for the current action to finish before deleting.");
        await _recovery.DeleteScopeAsync(scopeId);
        _scopes.TryRemove(scopeId, out _);
        threads?.TryRemoveScope(scopeId);
    }

    public async Task<RuntimeMessage> SubmitAsync(string scopeId, string playerId, string characterId, string playerName,
        string text, AgentType agentType, string clientMessageId, bool isSession, PlayerActionIntent? actionIntent = null)
    {
        progress?.Bind(SetPhaseAsync);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await RecoverAsync(scopeId);
        var gate = _scopeGates.GetOrAdd(scopeId, _ => new(1, 1));
        await gate.WaitAsync();
        try
        {
            var persisted = (await _recovery.FindRequestAsync(scopeId, playerId, clientMessageId))?.Read<RuntimeMessage>();
            if (persisted is not null) return persisted;
            if (await RequiresReviewAsync(scopeId, playerId))
                throw new HubException("Review the current state before continuing after an interrupted action.");
            var scope = _scopes[scopeId];
            RuntimeMessage message;
            lock (scope.Sync)
            {
                Prune(scope);
                if (scope.Messages.TryGetValue((playerId, clientMessageId), out var existing)) return Copy(existing);
                if (scope.Messages.Values.Any(m => m.PlayerId == playerId && (!m.IsComplete || scope.Finalizing.Contains(m.Id))))
                    throw new HubException("An action is already pending. Wait for it to finish.");
                message = new RuntimeMessage
                {
                    ScopeId = scopeId, Id = $"ai-{Guid.NewGuid():N}", ClientMessageId = clientMessageId,
                    PlayerId = playerId, PlayerName = playerName, UserText = text,
                    AgentType = agentType == AgentType.None ? "Game Master" : agentType.ToString(), Revision = 1,
                    ActionIntent = actionIntent,
                    Timestamp = _clock.GetUtcNow().UtcDateTime, UpdatedAt = _clock.GetUtcNow().UtcDateTime
                };
                scope.Messages.Add((playerId, clientMessageId), message);
            }
            try { await PersistAsync(scope, message, force: true); }
            catch { lock (scope.Sync) scope.Messages.Remove((playerId, clientMessageId)); throw; }
            lock (scope.Sync) scope.HasAcceptedRequests = true;
            // RunAsync catches all failures and records them. It does not capture Hub.Context or Hub.Clients.
            locks.BeginRequest(scopeId);
            _cancellations[message.Id] = new CancellationTokenSource();
            _ = RunAsync(scope, message, agentType, characterId, isSession);
            lock (scope.Sync) return Copy(message);
        } finally { gate.Release(); }
    }

    public List<RuntimeMessage> Snapshot(string scopeId)
    {
        if (!_scopes.TryGetValue(scopeId, out var scope)) return [];
        lock (scope.Sync)
        {
            Prune(scope);
            return scope.Messages.Values.OrderBy(m => m.Timestamp).Select(m =>
            {
                var snapshot = Copy(m);
                if (scope.Finalizing.Contains(m.Id)) { snapshot.Status = MessageProcessingStatus.Processing; snapshot.Phase = ProcessingPhase.Saving; }
                return snapshot;
            }).ToList();
        }
    }

    public bool HasAcceptedRequests(string scopeId)
    {
        if (!_scopes.TryGetValue(scopeId, out var scope)) return false;
        lock (scope.Sync) return scope.HasAcceptedRequests;
    }

    public RuntimeMessage? Find(string scopeId, string playerId, string clientId)
    {
        if (!_scopes.TryGetValue(scopeId, out var scope)) return null;
        lock (scope.Sync)
        {
            Prune(scope);
            return scope.Messages.TryGetValue((playerId, clientId), out var message) ? Copy(message) : null;
        }
    }

    public bool HasPending(string scopeId) => Snapshot(scopeId).Any(m => !m.IsComplete);

    private async Task RunAsync(ScopeMessages scope, RuntimeMessage message, AgentType agentType,
        string characterId, bool isSession)
    {
        var cancellation = _cancellations[message.Id];
        var started = false;
        try
        {
            await PublishAsync(scope, message, _ => { });
            var response = await processor.ProcessAsync(new PlayerMessageRequest
            {
                CampaignId = message.ScopeId, PlayerId = message.PlayerId, CharacterId = characterId,
                CancellationToken = cancellation.Token, ActionIntent = message.ActionIntent,
                Message = message.UserText, TargetAgentType = agentType, ClientMessageId = message.ClientMessageId,
                StatusCallback = update => update.Status is MessageProcessingStatus.Completed or MessageProcessingStatus.Failed
                    ? Task.CompletedTask : PublishAsync(scope, message, m =>
                {
                    if (update.Status == MessageProcessingStatus.Processing ||
                        (update.Status == MessageProcessingStatus.Queued && m.Status == MessageProcessingStatus.Queued))
                    {
                        if (update.Status == MessageProcessingStatus.Processing) started = true;
                        if (cancellation.IsCancellationRequested) return;
                        m.Status = update.Status;
                        m.Phase = update.Status == MessageProcessingStatus.Queued ? ProcessingPhase.Queued : ProcessingPhase.Generating;
                        m.QueuePosition = update.Position;
                    }
                }),
                StreamCallback = update => string.IsNullOrEmpty(update.Token) ? Task.CompletedTask : PublishAsync(scope, message, m =>
                {
                    if (cancellation.IsCancellationRequested) return;
                    // Agent handoff completion is not request completion.
                    if (!string.IsNullOrEmpty(update.Token))
                    {
                        if (m.Content.Length > 0 && m.AgentType != update.AgentType) m.Content += "\n\n";
                        m.Phase = ProcessingPhase.Generating;
                        m.AgentType = update.AgentType;
                        m.Content += update.Token;
                    }
                })
            }, isSession);

            // Choose completion or cancellation under the same gate before committing a clean agent checkpoint.
            var completionGate = _scopeGates.GetOrAdd(message.ScopeId, _ => new(1, 1));
            await completionGate.WaitAsync();
            try
            {
            cancellation.Token.ThrowIfCancellationRequested();

            if (!response.Success) logger.LogError("Agent request {RequestId} failed in {ScopeId}: {Error}", message.ClientMessageId, message.ScopeId, response.Error);
            if (response.Success && threads is not null) await threads.CommitScopeAsync(message.ScopeId);
            else threads?.DiscardScope(message.ScopeId);
            try
            {
                if (isSession)
                {
                    var session = await sessions.GetSessionStateAsync(message.ScopeId);
                    if (session is not null)
                        await hub.Clients.Group(GameHub.GetCampaignGroupName(message.ScopeId)).SendAsync("SessionStateUpdated", session);
                }
                else
                {
                    var state = await states.GetCampaignStateAsync(message.ScopeId);
                    if (state is not null)
                    {
                        await broadcaster.StateAsync(state);
                        foreach (var narrative in state.RecentNarratives.DistinctBy(n => n.Id))
                            await broadcaster.NarrativeAsync(message.ScopeId, narrative);
                        if (state.IsInCombat && state.CurrentCombat is not null)
                            await hub.Clients.Group(GameHub.GetCampaignGroupName(message.ScopeId)).SendAsync("CombatStarted", state.CurrentCombat);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "State delivery failed; scope {ScopeId} remains recoverable", message.ScopeId);
            }
            var suggestionState = !isSession ? await states.GetCampaignStateAsync(message.ScopeId) : null;
            await PublishAsync(scope, message, m =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(m.Content)) m.Content = response.FormattedResponse?.MessageToPlayers ?? response.Message;
                m.Status = response.Success ? MessageProcessingStatus.Completed : MessageProcessingStatus.Failed;
                m.Phase = response.Success ? ProcessingPhase.Completed : ProcessingPhase.Failed;
                m.Error = response.Success ? null : "The action could not be completed. Review current state before restoring your message.";
                m.Failure = response.Success ? null : new("action_failed", m.Error!, m.ClientMessageId, RecoveryAction.RestoreText);
                m.SuggestedActions = response.FormattedResponse?.SuggestedActions ?? [];
                if (suggestionState is not null) m.SuggestionContext = PlayerActionRules.TurnContext(suggestionState);
                m.QueuePosition = null;
            });
            }
            finally { completionGate.Release(); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            threads?.DiscardScope(message.ScopeId);
            await PublishAsync(scope, message, m =>
            {
                m.Status = started ? MessageProcessingStatus.Interrupted : MessageProcessingStatus.Cancelled;
                m.Phase = ProcessingPhase.Cancelled;
                m.QueuePosition = null;
                m.SuggestedActions = [];
                m.ProgressDetail = null;
                m.Error = started ? "Stopped. Applied effects remain; review the current state before continuing." : "Cancelled before processing. Your brilliant idea remains available to retry.";
                m.Failure = new("action_cancelled", m.Error, m.ClientMessageId, started ? RecoveryAction.ReviewState : RecoveryAction.RestoreText);
            });
            if (!isSession && await states.GetCampaignStateAsync(message.ScopeId) is { } latest) await broadcaster.StateAsync(latest);
            if (isSession && await sessions.GetSessionStateAsync(message.ScopeId) is { } draft)
                await hub.Clients.Group(GameHub.GetCampaignGroupName(message.ScopeId)).SendAsync("SessionStateUpdated", draft);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Action failed in scope {ScopeId}, request {RequestId}", message.ScopeId, message.ClientMessageId);
            threads?.DiscardScope(message.ScopeId);
            await PublishAsync(scope, message, m =>
            {
                m.Status = MessageProcessingStatus.Failed;
                m.Phase = ProcessingPhase.Failed;
                m.Failure = new("action_failed", "Review the current state before restoring your message.", m.ClientMessageId, RecoveryAction.RestoreText);
                m.Error = "The action could not be completed. Please try again.";
                m.QueuePosition = null;
            });
        }
        finally
        {
            _cancellations.TryRemove(message.Id, out _);
            cancellation.Dispose();
            lock (scope.Sync)
            {
                if (scope.Finalizing.Contains(message.Id)) scope.HeldLocks.Add(message.Id);
                else locks.EndRequest(message.ScopeId);
            }
        }
    }

    private async Task PublishAsync(ScopeMessages scope, RuntimeMessage message, Action<RuntimeMessage> update)
    {
        RuntimeMessage snapshot;
        lock (scope.Sync)
        {
            if (message.IsComplete) return;
            update(message);
            message.Revision++;
            message.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            snapshot = Copy(message);
            if (snapshot.IsComplete) scope.Finalizing.Add(message.Id);

        }
        try { await PersistAsync(scope, snapshot, snapshot.IsComplete); lock (scope.Sync) Prune(scope); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Response checkpoint failed for {ScopeId}", snapshot.ScopeId);
            snapshot.Failure = new("checkpoint_failed", "Changes not saved. Reload state before continuing.", snapshot.ClientMessageId, RecoveryAction.ReloadState);
            snapshot.Error = snapshot.Failure.Message;
            lock (scope.Sync) { message.Failure = snapshot.Failure; message.Error = snapshot.Error; }
            try { await broadcaster.MessageAsync(snapshot); }
            catch (Exception delivery) { logger.LogWarning(delivery, "Checkpoint error delivery failed"); }
            return;
        }
        try { await broadcaster.MessageAsync(snapshot); }
        catch (Exception ex) { logger.LogWarning(ex, "Delivery failed; response remains available for recovery"); }
        finally { if (snapshot.IsComplete) lock (scope.Sync) scope.Finalizing.Remove(snapshot.Id); }
    }

    private static RecoveryDocument Document(RuntimeMessage m) => new()
    {
        Id = m.Id, ScopeId = m.ScopeId, OwnerId = m.PlayerId, Kind = "message", Version = m.Revision,
        UpdatedAt = m.UpdatedAt, Payload = JsonSerializer.Serialize(m, RecoveryJson.Options)
    };

    private async Task PersistAsync(ScopeMessages scope, RuntimeMessage m, bool force)
    {
        await scope.Persistence.WaitAsync();
        try
        {
            var previous = scope.Persisted.TryGetValue(m.Id, out var known) ? known : (await _recovery.GetAsync(m.ScopeId, m.Id))?.Version ?? 0;
            if (previous >= m.Revision) return;
            if (!force && scope.LastCheckpoint.TryGetValue(m.Id, out var last) && _clock.GetUtcNow().UtcDateTime - last < TimeSpan.FromSeconds(1)) return;
            await _recovery.WriteAsync(Document(m), previous);
            scope.Persisted[m.Id] = m.Revision;
            scope.LastCheckpoint[m.Id] = _clock.GetUtcNow().UtcDateTime;
        }
        finally { scope.Persistence.Release(); }
    }

    public async Task SetPhaseAsync(string scopeId, ProcessingPhase phase, string? detail = null)
    {
        if (!_scopes.TryGetValue(scopeId, out var scope)) return;
        RuntimeMessage? active;
        lock (scope.Sync) active = scope.Messages.Values.FirstOrDefault(m => m.Status == MessageProcessingStatus.Processing);
        if (active is not null) await PublishAsync(scope, active, m => { m.Phase = phase; m.ProgressDetail = detail; });
    }

    private void Prune(ScopeMessages scope)
    {
        var dirty = scope.Messages.Values.Where(m => m.IsComplete && scope.Persisted.GetValueOrDefault(m.Id) < m.Revision).Select(m => m.Id).ToHashSet();
        var keep = scope.Messages.Values.Where(m => m.IsComplete && m.UpdatedAt > _clock.GetUtcNow().UtcDateTime.AddHours(-24))
            .OrderByDescending(m => m.UpdatedAt).Take(100).Select(m => m.Id).ToHashSet();
        foreach (var key in scope.Messages.Where(kv => kv.Value.IsComplete && !keep.Contains(kv.Value.Id) && !dirty.Contains(kv.Value.Id)).Select(kv => kv.Key).ToList())
        {
            var id = scope.Messages[key].Id;
            scope.Messages.Remove(key);
            scope.Persisted.TryRemove(id, out _); scope.LastCheckpoint.TryRemove(id, out _);
        }
    }

    private static RuntimeMessage Copy(RuntimeMessage m) => new()
    {
        Visibility = m.Visibility, TargetCharacterId = m.TargetCharacterId, Reviewed = m.Reviewed,
        ActionIntent = m.ActionIntent, SuggestionContext = m.SuggestionContext,
        ScopeId = m.ScopeId, Id = m.Id, ClientMessageId = m.ClientMessageId, PlayerId = m.PlayerId,
        PlayerName = m.PlayerName, UserText = m.UserText, Content = m.Content, AgentType = m.AgentType,
        Timestamp = m.Timestamp, UpdatedAt = m.UpdatedAt, Revision = m.Revision, Status = m.Status,
        QueuePosition = m.QueuePosition, ProgressDetail = m.ProgressDetail, Error = m.Error, Phase = m.Phase, Failure = m.Failure,
        SuggestedActions = m.SuggestedActions.Select(a => new SuggestedAction { Player = a.Player, Suggestions = a.Suggestions.ToList() }).ToList()
    };

    private sealed class ScopeMessages
    {
        public SemaphoreSlim Persistence { get; } = new(1, 1);
        public ConcurrentDictionary<string, long> Persisted { get; } = [];
        public ConcurrentDictionary<string, DateTime> LastCheckpoint { get; } = [];
        public object Sync { get; } = new();
        public HashSet<string> Finalizing { get; } = [];
        public HashSet<string> HeldLocks { get; } = [];
        public bool HasAcceptedRequests { get; set; }
        public Dictionary<(string PlayerId, string ClientId), RuntimeMessage> Messages { get; } = [];
    }
}
