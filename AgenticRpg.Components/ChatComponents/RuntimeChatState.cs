using AgenticRpg.Core.Messaging;
using AgenticRpg.Core.Models;

namespace AgenticRpg.Components.ChatComponents;

/// <summary>Applies both live and recovered responses by ID and monotonic revision.</summary>
public sealed class RuntimeChatState
{
    private readonly Dictionary<string, RuntimeMessage> _responses = [];
    public bool HasPending(string playerId) => _responses.Values.Any(r => r.PlayerId == playerId && (!r.IsComplete || r.Failure?.Code == "checkpoint_failed"));
    public RuntimeMessage? Pending(string playerId) => _responses.Values.FirstOrDefault(r => r.PlayerId == playerId && !r.IsComplete);

    public bool Apply(List<ChatMessage> messages, RuntimeMessage response, string playerId, string? characterName = null)
    {
        if (_responses.TryGetValue(response.Id, out var previous) && previous.Revision >= response.Revision) return false;
        _responses[response.Id] = response;
        var own = response.PlayerId == playerId;
        if (own) messages.RemoveAll(m => m.IsUser && m.PlayerId == playerId && !m.IsAuthoritative && m.ClientMessageId != response.ClientMessageId && m.Content == response.UserText);
        var userId = own ? response.ClientMessageId : $"user-{response.Id}";
        var user = messages.FirstOrDefault(m => m.Id == userId);
        if (user is null)
        {
            user = new ChatMessage { Id = userId, ClientMessageId = response.ClientMessageId,
                Content = response.UserText, IsUser = true, PlayerId = response.PlayerId,
                PlayerName = own ? "You" : response.PlayerName, Timestamp = response.Timestamp };
            messages.Add(user);
        }
        user.IsAuthoritative = true;
        user.Content = response.UserText;
        user.ActionIntent = response.ActionIntent;
        user.ProgressDetail = response.ProgressDetail;
        user.Status = response.Status;
        user.Phase = response.Phase;
        user.Failure = response.Failure;
        user.QueuePosition = response.QueuePosition;
        user.StatusNote = response.Error;
        var agent = messages.FirstOrDefault(m => m.Id == response.Id);
        if (agent is null)
        {
            agent = new ChatMessage { Id = response.Id, IsUser = false, Timestamp = response.Timestamp };
            messages.Add(agent);
        }
        agent.Content = response.Content;
        agent.Status = response.Status;
        agent.SuggestionContext = response.SuggestionContext;
        if (!string.IsNullOrWhiteSpace(response.Error)) agent.Content += $"\n\n{response.Error}";
        agent.PlayerName = response.AgentType;
        agent.IsStreaming = !response.IsComplete;
        agent.SuggestedActions = response.SuggestedActions
            .Where(a => string.IsNullOrWhiteSpace(characterName) ? own : a.Player.Equals(characterName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(a => a.Suggestions).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();
        return true;
    }
}
