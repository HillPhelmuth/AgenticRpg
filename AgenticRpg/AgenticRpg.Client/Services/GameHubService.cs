using System.Text.Json;
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
    private readonly ILogger<GameHubService> _logger;
    private readonly HubConnection? _connection;
    private readonly HubConnection? _submitConnection;
    private readonly SemaphoreSlim _submitConnectionStartLock = new(1, 1);
    private readonly IRollDiceService _rollDiceService;
    private string? _currentPlayerId;


    public event EventHandler<List<RollDiceResults>>? OnDiceRollResult;

    public GameHubService(IRollDiceService rollDiceService, IConfiguration configuration,
        ILogger<GameHubService> logger, NavigationManager navigationManager)
    {
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

        _connection?.Reconnecting += error =>
        {
            _logger.LogWarning("Connection reconnecting: {Error}", error?.Message);
            return Task.CompletedTask;
        };

        _connection?.Reconnected += connectionId =>
        {
            _logger.LogInformation("Connection reconnected: {ConnectionId}", connectionId);
            return Task.CompletedTask;
        };

        _connection?.Closed += error =>
        {
            _logger.LogError("Connection closed: {Error}", error?.Message);
            return Task.CompletedTask;
        };
        _connection?.On<DiceRollRequest>("RequestDiceRoll", HandleDiceRollRequestAsync);
    }

    public async Task StartAsync()
    {
        if (_connection?.State == HubConnectionState.Disconnected)
        {
            try
            {
                await _connection!.StartAsync();
                _logger.LogInformation("SignalR connection started");
            }
            catch (Exception ex)
            {
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

    public async Task JoinSessionAsync(string sessionId, string playerId, string sessionName)
    {
        try
        {
            _currentPlayerId = playerId;
            await _connection!.SendAsync("JoinSession", sessionId, playerId, sessionName);
            _logger.LogInformation("Joined session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task JoinCampaignAsync(string campaignId, string playerId, string characterId, string playerName)
    {
        try
        {
            _currentPlayerId = playerId;
            await _connection!.SendAsync("JoinCampaign", campaignId, playerId, characterId, playerName);
            _logger.LogInformation("Joined campaign {CampaignId}", campaignId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining campaign {CampaignId}", campaignId);
            throw;
        }
    }

    public async Task LeaveSessionAsync(string sessionOrCampaignId)
    {
        try
        {
            await _connection!.SendAsync("LeaveSession", sessionOrCampaignId);
            _logger.LogInformation("Left session/campaign {SessionId}", sessionOrCampaignId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving session {SessionId}", sessionOrCampaignId);
        }
    }

    public async Task LeaveCampaignAsync(string campaignId)
    {
        await LeaveSessionAsync(campaignId);
    }

    public async Task SendMessageAsync(string campaignId, string playerId, string message, AgentType targetAgentType = AgentType.None, string? clientMessageId = null)
    {
        try
        {
            await _connection!.SendAsync("SendMessage", campaignId, playerId, message, targetAgentType, clientMessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to campaign {CampaignId}", campaignId);
            throw;
        }
    }

    public async Task UpdateReadyStatusAsync(string campaignId, string playerId, bool isReady)
    {
        try
        {
            await _connection!.SendAsync("UpdateReadyStatus", campaignId, playerId, isReady);
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
            await _connection!.SendAsync("ChangeModel", sessionOrCampaignId, modelName);
            _logger.LogInformation("Changed model for {ScopeId} to {ModelName}", sessionOrCampaignId ?? "<global>", modelName ?? "<default>");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing model for {ScopeId} to {ModelName}", sessionOrCampaignId ?? "<global>", modelName ?? "<default>");
            throw;
        }
    }

    // Event subscription methods
    public void OnPlayerJoined(Action<string, string, string> handler)
    {
        _connection?.On("PlayerJoined", handler);
    }

    public void OnPlayerLeft(Action<string> handler)
    {
        _connection?.On("PlayerLeft", handler);
    }

    public void OnReceiveMessage(Action<string, AgentResponse, DateTime> handler)
    {
        _connection?.On("ReceiveMessage", handler);
    }

    public void OnReadyStatusUpdated(Action<string, bool> handler)
    {
        _connection?.On("ReadyStatusUpdated", handler);
    }

    public void OnReadyStatusSnapshot(Action<Dictionary<string, PlayerReadyStatus>> handler)
    {
        _connection?.On("ReadyStatusSnapshot", handler);
    }

    public void OnAllPlayersReady(Action<bool> handler)
    {
        _connection?.On("AllPlayersReady", handler);
    }

    public void OnCampaignStarted(Action<string> handler)
    {
        _connection?.On("CampaignStarted", handler);
    }

    public void OnStateUpdated(Action<GameState> handler)
    {
        _connection?.On("StateUpdated", handler);
    }

    public void OnSessionStateUpdated(Action<SessionState> handler)
    {
        _connection?.On("SessionStateUpdated", handler);
    }

    public void OnSessionCompleted(Action<string, string> handler)
    {
        _connection?.On("SessionCompleted", handler);
    }

    public void OnNarrativeAdded(Action<Narrative> handler)
    {
        _connection?.On("NarrativeAdded", handler);
    }

    public void OnCombatStarted(Action<CombatEncounter> handler)
    {
        _connection?.On("CombatStarted", handler);
    }

    public void OnTurnChanged(Action<string, string> handler)
    {
        _connection?.On("TurnChanged", handler);
    }

    public void OnMessageStatusChanged(Action<string, string, int?, string?> handler)
    {
        _connection?.On("MessageStatusChanged", handler);
    }

    public async Task SubmitDiceRollResultAsync(List<RollDiceResults> results)
    {
        try
        {
            _logger.LogInformation("Submitted dice roll result for request {RequestId} rolls: {Rolls}", results.Count, JsonSerializer.Serialize(results.Select(x => x.Results)));
            await EnsureSubmitConnectionStartedAsync().ConfigureAwait(false);
            await _submitConnection.InvokeAsync("SubmitDiceRollResult", new RollDiceResultsList { Results = results }).ConfigureAwait(false);
            //OnDiceRollResult?.Invoke(this, results);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting dice roll result");
            throw;
        }
    }

    private async Task HandleDiceRollRequestAsync(DiceRollRequest request)
    {
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

            _logger.LogInformation("Received dice roll request {RequestId} for session {SessionId}, {Json}",
                request.RequestId, request.SessionId, JsonSerializer.Serialize(request));

            var parameters = request.Parameters ?? new RollDiceParameters();
            var windowOptions = request.WindowOptions ?? new RollDiceWindowOptions();

            var results = await _rollDiceService.RequestDiceRoll(request.SessionId, parameters, windowOptions, request.NumberOfRollWindows)
                .ConfigureAwait(false);

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
    }
}

