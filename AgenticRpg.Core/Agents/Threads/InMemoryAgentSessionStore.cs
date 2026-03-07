using System.Collections.Concurrent;
using AgenticRpg.Core.Models.Enums;
using Microsoft.Agents.AI;

namespace AgenticRpg.Core.Agents.Threads;

public sealed class InMemoryAgentSessionStore : IAgentSessionStore
{
    private readonly ConcurrentDictionary<ThreadKey, AgentSession> _sessions = new();

    public async Task<AgentSession> GetOrCreate(string scopeId, AgentType agentType, Func<Task<AgentSession>> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        if (agentType == AgentType.None)
        {
            throw new ArgumentException("AgentType.None is not a valid thread key.", nameof(agentType));
        }

        ArgumentNullException.ThrowIfNull(factory);

        var key = new ThreadKey(scopeId, agentType);
        var session = await factory();
        return _sessions.GetOrAdd(key, session);
    }

    public bool TryRemoveScope(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);

        var removedAny = false;

        var keys = _sessions.Keys.ToArray();
        foreach (var key in keys)
        {
            if (!string.Equals(key.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            removedAny |= _sessions.TryRemove(key, out _);
        }

        return removedAny;
    }

    private readonly record struct ThreadKey(string ScopeId, AgentType AgentType);
}
