using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.JSInterop;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Hubs;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace AgenticRpg.Client.Services;

public class GameHubService : IGameHubService, IAsyncDisposable
{
    private readonly IJSRuntime? _js;
    private IJSObjectReference? _draftModule;
    private readonly Dictionary<string, PendingSubmission> _pendingSubmissions = [];
    private readonly ILogger<GameHubService> _logger;
    private readonly HubConnection? _connection;
    private readonly HubConnection? _submitConnection;
    private readonly SemaphoreSlim _submitConnectionStartLock = new(1, 1);
    private readonly IRollDiceService _rollDiceService;
    private string? _currentPlayerId;
    private ActiveScope? _activeScope;
    private readonly SemaphoreSlim _scopeLock = new(1, 1);
    private readonly Dictionary<string, RuntimeMessage> _messages = [];
    private readonly List<RuntimeMessage> _recoveryBuffer = [];
    private bool _recovering;
    private readonly List<Action> _scopeEventBuffer = [];
    private readonly HashSet<string> _openDiceRequests = [];
    private readonly Dictionary<string, List<Guid>> _diceWindows = [];
    private readonly HashSet<string> _obsoleteDiceRequests = [];
    private readonly Dictionary<string, RollDiceResults> _rolledWindows = [];
    private readonly Dictionary<string, List<RollDiceResults>> _unsentDice = [];
    private event Action<RuntimeMessage>? MessageUpdated;
    private event Action<ScopeRecoverySnapshot>? Recovered;
    private event Action<GameConnectionStatus>? StatusChanged;
    public GameConnectionStatus ConnectionStatus { get; private set; } = GameConnectionStatus.Disconnected;
    public bool HasPending(string scopeId, string playerId) => _messages.Values.Any(m => m.ScopeId == scopeId && m.PlayerId == playerId && (!m.IsComplete || m.Failure?.Code == "checkpoint_failed"));
    private sealed record ActiveScope(string Id, string PlayerId, string CharacterId, string Name, bool IsSession, string LeaseId);



    public event EventHandler<List<RollDiceResults>>? OnDiceRollResult;

    public GameHubService(IRollDiceService rollDiceService, IConfiguration configuration,
        ILogger<GameHubService> logger, NavigationManager navigationManager, IJSRuntime? js = null)
    {
        _js = js;
        _logger = logger;
        _rollDiceService = rollDiceService;
        var hubUrl = navigationManager.ToAbsoluteUri("/hubs/game");
        _logger.LogInformation("Initializing GameHubService with URL: {HubUrl}", hubUrl);
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        // Separate connection used for submitting dice roll results.
        // Reason: SignalR processes hub invocations sequentially per connection. When dice rolls are requested
        // from an agent tool invocation the tool result requires a separate connection to submit the result back
        _submitConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.Reconnecting += error =>
        {
            _recovering = true;
            SetStatus(GameConnectionStatus.Reconnecting);
            return Task.CompletedTask;
        };
        _connection.Reconnected += async connectionId =>
        {
            try { await RecoverAsync(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore active session");
                SetStatus(GameConnectionStatus.Disconnected);
            }
        };
        _connection.Closed += error =>
        {
            SetStatus(GameConnectionStatus.Disconnected);
            return Task.CompletedTask;
        };
        _connection.On<RuntimeMessage>("MessageUpdated", HandleMessageUpdated);
        _connection.On<string>("DiceRequestClosed", CloseDiceRequest);
        _connection?.On<DiceRollRequest>("RequestDiceRoll", request => { _ = HandleDiceRollRequestAsync(request); });
        _submitConnection.Reconnected += async _ => await FlushDiceAsync();
    }

    public async Task StartAsync()
    {
        if (_connection?.State == HubConnectionState.Disconnected)
        {
            try
            {
                SetStatus(GameConnectionStatus.Connecting);
                await _connection!.StartAsync();
                _logger.LogInformation("SignalR connection started");
            }
            catch (Exception ex)
            {
                SetStatus(GameConnectionStatus.Disconnected);
                _logger.LogError(ex, "Error starting SignalR connection");
                throw;
            }
        }

        await EnsureSubmitConnectionStartedAsync().ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            try
            {
                await _connection?.StopAsync();
                _logger.LogInformation("SignalR connection stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping SignalR connection");
            }
        }

        if (_submitConnection.State == HubConnectionState.Connected)
        {
            try
            {
                await _submitConnection.StopAsync();
                _logger.LogInformation("SignalR submit connection stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping SignalR submit connection");
            }
        }
    }

    private async Task EnsureSubmitConnectionStartedAsync()
    {
        if (_submitConnection.State != HubConnectionState.Disconnected)
        {
            return;
        }

        await _submitConnectionStartLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_submitConnection.State == HubConnectionState.Disconnected)
            {
                await _submitConnection.StartAsync().ConfigureAwait(false);
                _logger.LogInformation("SignalR submit connection started");
            }
        }
        finally
        {
            _submitConnectionStartLock.Release();
        }
    }

    public Task<ScopeRecoverySnapshot> JoinSessionAsync(string sessionId, string playerId, string sessionName, string? leaseId = null) =>
        JoinAsync(new(sessionId, playerId, string.Empty, sessionName, true, leaseId ?? Guid.NewGuid().ToString("N")));

    public Task<ScopeRecoverySnapshot> JoinCampaignAsync(string campaignId, string playerId, string characterId, string playerName, string? leaseId = null) =>
        JoinAsync(new(campaignId, playerId, characterId, playerName, false, leaseId ?? Guid.NewGuid().ToString("N")));

    private async Task<ScopeRecoverySnapshot> JoinAsync(ActiveScope scope)
    {
        await _scopeLock.WaitAsync();
        try
        {
            _activeScope = scope;
            _currentPlayerId = scope.PlayerId;
            _recovering = true;
            var snapshot = scope.IsSession
                ? await _connection!.InvokeAsync<ScopeRecoverySnapshot>("JoinSession", scope.Id, scope.PlayerId, scope.Name, scope.LeaseId)
                : await _connection!.InvokeAsync<ScopeRecoverySnapshot>("JoinCampaign", scope.Id, scope.PlayerId, scope.CharacterId, scope.Name, scope.LeaseId);
            foreach (var message in snapshot.Messages) ApplyMessage(message);
            if (_js is not null) _draftModule ??= await _js.InvokeAsync<IJSObjectReference>("import", "./_content/AgenticRpg.Components/ChatComponents/draftInput.js");
            if (!scope.IsSession && _draftModule is not null)
                await _draftModule.InvokeVoidAsync("set", $"rpg-draft:{Uri.EscapeDataString(scope.PlayerId)}:last-adventure", JsonSerializer.Serialize(new { CampaignId = scope.Id, CharacterId = scope.CharacterId }));
            if (_draftModule is not null)
            {
                var key = $"rpg-draft:{Uri.EscapeDataString(scope.PlayerId)}:outbox:{scope.Id}";
                var pending = await _draftModule.InvokeAsync<string>("get", key);
                var old = !string.IsNullOrEmpty(pending) ? JsonSerializer.Deserialize<PendingSubmission>(pending) : _pendingSubmissions.GetValueOrDefault(key);
                if (old is not null && snapshot.Messages.Any(m => m.PlayerId == scope.PlayerId && m.ClientMessageId == old.Id))
                {
                    _pendingSubmissions.Remove(key);
                    await _draftModule.InvokeVoidAsync("set", key, "");
                    var composerKey = $"rpg-draft:{Uri.EscapeDataString(scope.PlayerId)}:{scope.Id}";
                    if (await _draftModule.InvokeAsync<string>("get", composerKey) == old.Text)
                        await _draftModule.InvokeVoidAsync("set", composerKey, "");
                }
                else if (old is not null) _pendingSubmissions[key] = old;
            }
            Recovered?.Invoke(snapshot);
            _recovering = false;
            foreach (var message in _recoveryBuffer.ToArray()) ApplyMessage(message);
            _recoveryBuffer.Clear();
            foreach (var update in _scopeEventBuffer.ToArray()) update();
            _scopeEventBuffer.Clear();
            var retained = snapshot.PendingDice.Select(r => r.RequestId).ToHashSet();
            foreach (var requestId in _openDiceRequests.Where(id => !retained.Contains(id)).ToList())
            {
                _obsoleteDiceRequests.Add(requestId);
                if (_diceWindows.TryGetValue(requestId, out var windows))
                    foreach (var id in windows) _rollDiceService.Close(id, null);
            }
            var retainedWindows = snapshot.PendingDice.SelectMany(r => r.WindowRequestIds ?? []).ToHashSet();
            foreach (var entry in _unsentDice.ToList())
            {
                entry.Value.RemoveAll(r => !retainedWindows.Contains(r.RequestId));
                if (entry.Value.Count == 0) _unsentDice.Remove(entry.Key);
            }
            SetStatus(GameConnectionStatus.Connected);
            foreach (var request in snapshot.PendingDice) _ = HandleDiceRollRequestAsync(request);
            await FlushDiceAsync();
            return snapshot;
        }
        catch
        {
            SetStatus(GameConnectionStatus.Disconnected);
            throw;
        }
        finally { _scopeLock.Release(); }
    }

    private async Task RecoverAsync()
    {
        var scope = _activeScope;
        if (scope is not null) await JoinAsync(scope);
        else { _recovering = false; SetStatus(GameConnectionStatus.Connected); }
    }

    public async Task RetryConnectionAsync()
    {
        await StartAsync();
        await RecoverAsync();
    }

    public async Task DetachScopeAsync(string scopeId, string leaseId)
    {
        await _scopeLock.WaitAsync();
        try
        {
            if (_activeScope?.Id != scopeId || _activeScope.LeaseId != leaseId) return;
            _activeScope = null;
            _recovering = false;
            _recoveryBuffer.Clear();
            _scopeEventBuffer.Clear();
            if (_connection?.State == HubConnectionState.Connected)
                await _connection.InvokeAsync("DetachScope", scopeId, leaseId);
        }
        finally { _scopeLock.Release(); }
    }

    private void SetStatus(GameConnectionStatus status)
    {
        ConnectionStatus = status;
        StatusChanged?.Invoke(status);
    }

    private void HandleMessageUpdated(RuntimeMessage message)
    {
        if (_recovering) { _recoveryBuffer.Add(message); return; }
        ApplyMessage(message);
    }

    private void DispatchScopeEvent(string scopeId, Action callback)
    {
        if (_activeScope?.Id != scopeId) return;
        void Apply() { if (_activeScope?.Id == scopeId) callback(); }
        if (_recovering) _scopeEventBuffer.Add(Apply);
        else Apply();
    }

    private void ApplyMessage(RuntimeMessage message)
    {
        if (_activeScope?.Id != message.ScopeId) return;
        if (_messages.TryGetValue(message.Id, out var previous) && previous.Revision >= message.Revision) return;
        _messages[message.Id] = message;
        MessageUpdated?.Invoke(message);
    }

    public IDisposable OnMessageUpdated(Action<RuntimeMessage> handler)
    {
        MessageUpdated += handler;
        return new Subscription(() => MessageUpdated -= handler);
    }
    public IDisposable OnRecovered(Action<ScopeRecoverySnapshot> handler)
    {
        Recovered += handler;
        return new Subscription(() => Recovered -= handler);
    }
    public IDisposable OnConnectionStatusChanged(Action<GameConnectionStatus> handler)
    {
        StatusChanged += handler;
        return new Subscription(() => StatusChanged -= handler);
    }
    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }

    public Task LeaveSessionAsync(string scopeId, string? playerId = null, string? characterName = null) =>
        DetachScopeAsync(scopeId, _activeScope?.LeaseId ?? string.Empty);

    public async Task LeaveCampaignAsync(string campaignId, string playerId, string characterId)
    {
        await _connection!.InvokeAsync("LeaveCampaign", campaignId, playerId, characterId);
        if (_activeScope?.Id == campaignId) _activeScope = null;
    }

    private void CloseDiceRequest(string requestId)
    {
        _obsoleteDiceRequests.Add(requestId);
        if (_diceWindows.Remove(requestId, out var windows))
            foreach (var id in windows) _rollDiceService.Close(id, null);
        _openDiceRequests.Remove(requestId);
        _unsentDice.Remove(requestId);
    }

    public async Task CancelRequestAsync(string scopeId, string clientMessageId) =>
        HandleMessageUpdated(await _connection!.InvokeAsync<RuntimeMessage>("CancelRequest", scopeId, clientMessageId));
    public Task SendActionAsync(string scopeId, string playerId, string summary, PlayerActionIntent intent, string clientMessageId) =>
        SendSubmissionAsync(scopeId, playerId, summary, AgentType.GameMaster, clientMessageId, intent);
    private sealed record PendingSubmission(string Id, string Text, AgentType Agent, PlayerActionIntent? Intent = null);
    public Task SendMessageAsync(string scopeId, string playerId, string message, AgentType targetAgentType = AgentType.None, string? clientMessageId = null) =>
        SendSubmissionAsync(scopeId, playerId, message, targetAgentType, clientMessageId);
    private async Task SendSubmissionAsync(string scopeId, string playerId, string message, AgentType targetAgentType, string? clientMessageId, PlayerActionIntent? intent = null)
    {
        if (ConnectionStatus != GameConnectionStatus.Connected) throw new InvalidOperationException("Reconnect before sending an action.");
        var key = $"rpg-draft:{Uri.EscapeDataString(playerId)}:outbox:{scopeId}";
        var id = clientMessageId ?? Guid.NewGuid().ToString("N");
        if (_pendingSubmissions.TryGetValue(key, out var inMemory))
        {
            if (inMemory.Text != message || inMemory.Agent != targetAgentType || inMemory.Intent != intent)
                throw new InvalidOperationException("Resolve the previous unconfirmed message before sending a different action.");
            id = inMemory.Id;
        }
        if (_js is not null)
        {
            _draftModule ??= await _js.InvokeAsync<IJSObjectReference>("import", "./_content/AgenticRpg.Components/ChatComponents/draftInput.js");
            var pending = await _draftModule.InvokeAsync<string>("get", key);
            if (!string.IsNullOrEmpty(pending))
            {
                var old = JsonSerializer.Deserialize<PendingSubmission>(pending);
                if (old is not null && (old.Text != message || old.Agent != targetAgentType || old.Intent != intent))
                    throw new InvalidOperationException("Resolve the previous unconfirmed message before sending a different action.");
                if (old is not null) id = old.Id;
            }
            await _draftModule.InvokeVoidAsync("set", key, JsonSerializer.Serialize(new PendingSubmission(id, message, targetAgentType, intent)));
        }
        _pendingSubmissions[key] = new(id, message, targetAgentType, intent);
        RuntimeMessage accepted;
        try
        {
            accepted = intent is null
                ? await _connection!.InvokeAsync<RuntimeMessage>("SendMessage", scopeId, playerId, message, targetAgentType, id)
                : await _connection!.InvokeAsync<RuntimeMessage>("SendAction", scopeId, playerId, intent, id);
        }
        catch (Microsoft.AspNetCore.SignalR.HubException)
        {
            // A definitive server rejection is different from uncertain transport delivery.
            _pendingSubmissions.Remove(key);
            if (_draftModule is not null) await _draftModule.InvokeVoidAsync("set", key, "");
            throw;
        }
        _pendingSubmissions.Remove(key);
        if (_draftModule is not null) await _draftModule.InvokeVoidAsync("set", key, "");
        HandleMessageUpdated(accepted);
    }

    /// <summary>
    /// Streams PCM audio chunks generated from the provided text and invokes the handler for each chunk.
    /// </summary>
    /// <param name="text">The text to synthesize.</param>
    /// <param name="messageId"></param>
    /// <param name="onChunk">Callback invoked for each audio chunk.</param>
    /// <param name="cancellationToken">Token to cancel the streaming operation.</param>
    public async Task StreamSpeechAsync(string text, string messageId, Func<byte[], Task> onChunk,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var stream = _connection!.StreamAsync<byte[]>("StreamSpeech", text, messageId, cancellationToken);
        Console.WriteLine("Streaming speech...");
        var index = 0;
        await foreach (var chunk in stream.WithCancellation(cancellationToken))
        {
            Console.WriteLine($"Streaming chunk {index++}");
            await onChunk(chunk);
        }
    }

    public async Task UpdateReadyStatusAsync(string campaignId, string playerId, bool isReady)
    {
        try
        {
            await _connection!.InvokeAsync("UpdateReadyStatus", campaignId, playerId, isReady);
            _logger.LogInformation("Updated ready status: {IsReady}", isReady);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating ready status");
            throw;
        }
    }

    public async Task ChangeModelAsync(string? sessionOrCampaignId, string? modelName)
    {
        try
        {
            await _connection!.InvokeAsync("ChangeModel", sessionOrCampaignId, modelName);
            _logger.LogInformation("Changed model for {ScopeId} to {ModelName}", sessionOrCampaignId ?? "<global>", modelName ?? "<default>");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing model for {ScopeId} to {ModelName}", sessionOrCampaignId ?? "<global>", modelName ?? "<default>");
            throw;
        }
    }

    // Event subscription methods
    public IDisposable OnPlayerJoined(Action<string, string, string> handler)
    {
        return _connection!.On<string, string, string, string>("PlayerJoined", (scope, player, character, name) => DispatchScopeEvent(scope, () => handler(player, character, name)));
    }

    public IDisposable OnPlayerLeft(Action<string> handler)
    {
        return _connection!.On<string, string>("PlayerLeft", (scope, player) => DispatchScopeEvent(scope, () => handler(player)));
    }

    public IDisposable OnReceiveMessage(Action<string, AgentResponse, DateTime> handler)
    {
        return _connection!.On("ReceiveMessage", handler);
    }

    public IDisposable OnReadyStatusUpdated(Action<string, bool> handler)
    {
        return _connection!.On<string, string, bool>("ReadyStatusUpdated", (scope, player, ready) => DispatchScopeEvent(scope, () => handler(player, ready)));
    }

    public IDisposable OnReadyStatusSnapshot(Action<Dictionary<string, PlayerReadyStatus>> handler)
    {
        return _connection!.On<string, Dictionary<string, PlayerReadyStatus>>("ReadyStatusSnapshot", (scope, snapshot) => DispatchScopeEvent(scope, () => handler(snapshot)));
    }

    public IDisposable OnAllPlayersReady(Action<bool> handler)
    {
        return _connection!.On<string, bool>("AllPlayersReady", (scope, ready) => DispatchScopeEvent(scope, () => handler(ready)));
    }

    public IDisposable OnCampaignStarted(Action<string> handler)
    {
        return _connection!.On<string>("CampaignStarted", scope => DispatchScopeEvent(scope, () => handler(scope)));
    }

    public IDisposable OnStateUpdated(Action<GameState> handler)
    {
        return _connection!.On<GameState>("StateUpdated", state => DispatchScopeEvent(state.CampaignId, () => handler(state)));
    }

    public IDisposable OnSessionStateUpdated(Action<SessionState> handler)
    {
        return _connection!.On<SessionState>("SessionStateUpdated", state => DispatchScopeEvent(state.SessionId, () => handler(state)));
    }

    public IDisposable OnSessionCompleted(Action<string, string> handler)
    {
        return _connection!.On<string, string>("SessionCompleted", (scope, result) => DispatchScopeEvent(scope, () => handler(scope, result)));
    }

    public IDisposable OnNarrativeAdded(Action<Narrative> handler)
    {
        return _connection!.On<string, Narrative>("NarrativeAdded", (scope, narrative) => DispatchScopeEvent(scope, () => handler(narrative)));
    }

    public IDisposable OnCombatStarted(Action<CombatEncounter> handler)
    {
        return _connection!.On<CombatEncounter>("CombatStarted", encounter => DispatchScopeEvent(encounter.CampaignId, () => handler(encounter)));
    }

    public IDisposable OnTurnChanged(Action<string, string> handler)
    {
        return _connection!.On("TurnChanged", handler);
    }

    public IDisposable OnMessageStatusChanged(Action<string, string, int?, string?> handler)
    {
        return _connection!.On("MessageStatusChanged", handler);
    }

    public IDisposable OnMessageStreamStarted(Action<string, string, DateTime> handler)
    {
        return _connection!.On("MessageStreamStarted", handler);
    }

    public IDisposable OnMessageStreamToken(Action<string, string, string> handler)
    {
        return _connection!.On("MessageStreamToken", handler);
    }

    public IDisposable OnMessageStreamCompleted(Action<string, string, string?> handler)
    {
        return _connection!.On("MessageStreamCompleted", handler);
    }

    public IDisposable OnSuggestedActionsUpdated(Action<string, List<SuggestedAction>> handler)
    {
        return _connection!.On("SuggestedActionsUpdated", handler);
    }

    public async Task SubmitDiceRollResultAsync(List<RollDiceResults> results)
    {
        if (results.Count == 0) return;
        var key = results[0].RequestId;
        foreach (var result in results) _rolledWindows[result.RequestId] = result;
        _unsentDice[key] = results;
        await FlushDiceAsync();
    }

    private async Task FlushDiceAsync()
    {
        foreach (var entry in _unsentDice.ToArray())
        {
            try
            {
                await EnsureSubmitConnectionStartedAsync();
                if (_submitConnection!.State != HubConnectionState.Connected) return;
                await _submitConnection.InvokeAsync("SubmitDiceRollResult", new RollDiceResultsList { Results = entry.Value });
                _unsentDice.Remove(entry.Key);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Dice results retained until reconnect"); }
        }
    }

    private async Task HandleDiceRollRequestAsync(DiceRollRequest request)
    {
        if (_obsoleteDiceRequests.Contains(request.RequestId) || _activeScope?.Id != request.SessionId ||
            (!string.IsNullOrEmpty(request.PlayerId) && request.PlayerId != _currentPlayerId) ||
            !_openDiceRequests.Add(request.RequestId)) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(request.PlayerId) &&
                !string.IsNullOrWhiteSpace(_currentPlayerId) &&
                !string.Equals(request.PlayerId, _currentPlayerId, StringComparison.OrdinalIgnoreCase))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Ignoring dice roll request {RequestId} for player {TargetPlayerId}; current player is {CurrentPlayerId}",
                        request.RequestId, request.PlayerId, _currentPlayerId);
                }

                return;
            }

            _logger.LogInformation("Received dice roll request {RequestId} for player {TargetPlayerId}; current player is {CurrentPlayerId} for session {SessionId}, {Json}",
                request.RequestId, request.PlayerId, _currentPlayerId, request.SessionId, JsonSerializer.Serialize(request));

            if (request.WindowRequestIds is { Count: > 0 } ids)
            {
                var rolled = ids.Where(_rolledWindows.ContainsKey).Select(id => _rolledWindows[id]).ToList();
                if (rolled.Count > 0) await SubmitDiceRollResultAsync(rolled);
                request.WindowRequestIds = ids.Where(id => !_rolledWindows.ContainsKey(id)).ToList();
                request.NumberOfRollWindows = request.WindowRequestIds.Count;
                if (request.NumberOfRollWindows == 0) return;
            }
            var parameters = request.Parameters ?? new RollDiceParameters();
            if (_obsoleteDiceRequests.Contains(request.RequestId)) return;
            var windowOptions = request.WindowOptions ?? new RollDiceWindowOptions();

            var existingWindows = _rollDiceService.DieRollReferences.Select(r => r.Id).ToHashSet();
            var rolling = _rollDiceService.RequestDiceRoll(request.SessionId, parameters, windowOptions, request.NumberOfRollWindows);
            _diceWindows[request.RequestId] = _rollDiceService.DieRollReferences.Where(r => !existingWindows.Contains(r.Id)).Select(r => r.Id).ToList();
            var results = await rolling.ConfigureAwait(false);
            if (_obsoleteDiceRequests.Contains(request.RequestId)) return;

            // Normalize to a non-null list while preserving count so the server can complete
            // every pending window.
            var normalizedResults = results?.Select(r => r ?? RollDiceResults.Empty(false)).ToList() ?? [];

            if (request.WindowRequestIds is { Count: > 0 } windowIds && windowIds.Count == normalizedResults.Count)
            {
                for (var i = 0; i < normalizedResults.Count; i++)
                {
                    normalizedResults[i].RequestId = windowIds[i];
                    normalizedResults[i].SessionId = request.SessionId;
                }
            }
            else
            {
                foreach (var result in normalizedResults)
                {
                    result.RequestId = request.RequestId ?? string.Empty;
                    result.SessionId = request.SessionId;
                }
            }

            await SubmitDiceRollResultAsync(normalizedResults).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_obsoleteDiceRequests.Contains(request.RequestId)) return;
            _logger.LogError(ex, "Failed handling dice roll request {RequestId}", request.RequestId);

            if (request.WindowRequestIds is { Count: > 0 } windowIds)
            {
                var failures = windowIds
                    .Select(windowId => new RollDiceResults(false) { RequestId = windowId, SessionId = request.SessionId })
                    .ToList();

                await SubmitDiceRollResultAsync(failures).ConfigureAwait(false);
            }
            else
            {
                var failedResult = RollDiceResults.Empty(false);
                failedResult.RequestId = request.RequestId ?? string.Empty;
                failedResult.SessionId = request.SessionId;

                await SubmitDiceRollResultAsync([failedResult]).ConfigureAwait(false);
            }
        }
        finally { _openDiceRequests.Remove(request.RequestId); _diceWindows.Remove(request.RequestId); _obsoleteDiceRequests.Remove(request.RequestId); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }

        if (_submitConnection != null)
        {
            await _submitConnection.DisposeAsync();
        }

        _submitConnectionStartLock.Dispose();
        _scopeLock.Dispose();
    }
}

