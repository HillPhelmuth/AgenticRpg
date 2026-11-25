using AgenticRpg.Core.Hubs;
using AgenticRpg.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Core.Services;

public sealed class PlayerStateBroadcaster(IHubContext<GameHub> hub, GameConnectionRegistry connections,
    IPlayerStateProjector projector, ILogger<PlayerStateBroadcaster>? logger = null)
{
    public Task StateAsync(GameState state, CancellationToken cancellationToken = default) =>
        Task.WhenAll(connections.InScope(state.CampaignId).Where(c => !c.Value.IsSession).Select(c =>
            SendAsync(c.Key, "StateUpdated", [projector.Project(state, c.Value.CharacterId)], cancellationToken)));

    public Task NarrativeAsync(string scopeId, Narrative narrative) =>
        Task.WhenAll(connections.InScope(scopeId).Where(c => !c.Value.IsSession && projector.CanSee(narrative, c.Value.CharacterId))
            .Select(c => SendAsync(c.Key, "NarrativeAdded", [scopeId, narrative])));
    public Task MessageAsync(RuntimeMessage message)
    {
        if (message.Visibility == AgenticRpg.Core.Models.Enums.NarrativeVisibility.Global)
            return hub.Clients.Group(GameHub.GetCampaignGroupName(message.ScopeId)).SendAsync("MessageUpdated", message);
        if (message.Visibility != AgenticRpg.Core.Models.Enums.NarrativeVisibility.CharacterSpecific) return Task.CompletedTask;
        return Task.WhenAll(connections.InScope(message.ScopeId).Where(c => c.Value.CharacterId == message.TargetCharacterId)
            .Select(c => SendAsync(c.Key, "MessageUpdated", [message])));
    }
    private async Task SendAsync(string connectionId, string method, object?[] args, CancellationToken cancellationToken = default)
    {
        try { await hub.Clients.Client(connectionId).SendCoreAsync(method, args, cancellationToken); }
        catch (Exception ex) { logger?.LogWarning(ex, "Delivery to {ConnectionId} failed; current state remains recoverable", connectionId); }
    }
}
