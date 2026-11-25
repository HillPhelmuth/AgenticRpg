using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.DiceRoller.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgenticRpg.Client.Services;

public interface IGameHubService
{
    Task StartAsync();
    Task StopAsync();
    Task<ScopeRecoverySnapshot> JoinSessionAsync(string sessionId, string playerId, string sessionName, string? leaseId = null);
    Task<ScopeRecoverySnapshot> JoinCampaignAsync(string campaignId, string playerId, string characterId, string playerName, string? leaseId = null);
    Task DetachScopeAsync(string scopeId, string leaseId);
    Task RetryConnectionAsync();
    GameConnectionStatus ConnectionStatus { get; }
    bool HasPending(string scopeId, string playerId);
    Task LeaveSessionAsync(string sessionOrCampaignId, string? playerId = null, string? characterName = null);
    Task LeaveCampaignAsync(string campaignId, string playerId, string characterId);
    Task SendMessageAsync(string campaignId, string playerId, string message, AgentType targetAgentType = AgentType.None, string? clientMessageId = null);
    Task SendActionAsync(string scopeId, string playerId, string summary, PlayerActionIntent intent, string clientMessageId);
    Task CancelRequestAsync(string scopeId, string clientMessageId);
    Task StreamSpeechAsync(string text, string messageId, Func<byte[], Task> onChunk,
        CancellationToken cancellationToken = default);
    Task UpdateReadyStatusAsync(string campaignId, string playerId, bool isReady);

    // Event subscriptions
    IDisposable OnPlayerJoined(Action<string, string, string> handler);
    IDisposable OnPlayerLeft(Action<string> handler);
    IDisposable OnReceiveMessage(Action<string, AgentResponse, DateTime> handler);
    IDisposable OnReadyStatusUpdated(Action<string, bool> handler);
    IDisposable OnReadyStatusSnapshot(Action<Dictionary<string, PlayerReadyStatus>> handler);
    IDisposable OnAllPlayersReady(Action<bool> handler);
    IDisposable OnCampaignStarted(Action<string> handler);
    IDisposable OnStateUpdated(Action<GameState> handler);
    IDisposable OnSessionStateUpdated(Action<SessionState> handler);
    IDisposable OnSessionCompleted(Action<string, string> handler);
    IDisposable OnNarrativeAdded(Action<Narrative> handler);
    IDisposable OnCombatStarted(Action<CombatEncounter> handler);
    IDisposable OnTurnChanged(Action<string, string> handler);
    IDisposable OnMessageStatusChanged(Action<string, string, int?, string?> handler);
    IDisposable OnMessageStreamStarted(Action<string, string, DateTime> handler);
    IDisposable OnMessageStreamToken(Action<string, string, string> handler);
    IDisposable OnMessageStreamCompleted(Action<string, string, string?> handler);
    IDisposable OnSuggestedActionsUpdated(Action<string, List<SuggestedAction>> handler);
    
    IDisposable OnMessageUpdated(Action<RuntimeMessage> handler);
    IDisposable OnRecovered(Action<ScopeRecoverySnapshot> handler);
    IDisposable OnConnectionStatusChanged(Action<GameConnectionStatus> handler);

    // Dice rolling
    Task SubmitDiceRollResultAsync(List<RollDiceResults> results);
    event EventHandler<List<RollDiceResults>>? OnDiceRollResult;
    Task ChangeModelAsync(string? sessionOrCampaignId, string? modelName);
}
