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
    Task JoinSessionAsync(string sessionId, string playerId, string sessionName);
    Task JoinCampaignAsync(string campaignId, string playerId, string characterId, string playerName);
    Task LeaveSessionAsync(string sessionOrCampaignId, string? playerId = null, string? characterName = null);
    Task LeaveCampaignAsync(string campaignId, string playerId, string characterId);
    Task SendMessageAsync(string campaignId, string playerId, string message, AgentType targetAgentType = AgentType.None, string? clientMessageId = null);
    Task StreamSpeechAsync(string text, string messageId, Func<byte[], Task> onChunk,
        CancellationToken cancellationToken = default);
    Task UpdateReadyStatusAsync(string campaignId, string playerId, bool isReady);

    // Event subscriptions
    void OnPlayerJoined(Action<string, string, string> handler);
    void OnPlayerLeft(Action<string> handler);
    void OnReceiveMessage(Action<string, AgentResponse, DateTime> handler);
    void OnReadyStatusUpdated(Action<string, bool> handler);
    void OnReadyStatusSnapshot(Action<Dictionary<string, PlayerReadyStatus>> handler);
    void OnAllPlayersReady(Action<bool> handler);
    void OnCampaignStarted(Action<string> handler);
    void OnStateUpdated(Action<GameState> handler);
    void OnSessionStateUpdated(Action<SessionState> handler);
    void OnSessionCompleted(Action<string, string> handler);
    void OnNarrativeAdded(Action<Narrative> handler);
    void OnCombatStarted(Action<CombatEncounter> handler);
    void OnTurnChanged(Action<string, string> handler);
    void OnMessageStatusChanged(Action<string, string, int?, string?> handler);
    
    // Dice rolling
    Task SubmitDiceRollResultAsync(List<RollDiceResults> results);
    event EventHandler<List<RollDiceResults>>? OnDiceRollResult;
    Task ChangeModelAsync(string? sessionOrCampaignId, string? modelName);
}