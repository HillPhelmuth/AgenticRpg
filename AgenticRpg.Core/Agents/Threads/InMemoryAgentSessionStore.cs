using System.Collections.Concurrent;
using System.Text.Json;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Repositories;
using Microsoft.Agents.AI;

namespace AgenticRpg.Core.Agents.Threads;

/// <summary>Only checkpoints from successful complete requests become durable.</summary>
public sealed class InMemoryAgentSessionStore(IRecoveryRepository? repository = null) : IAgentSessionStore
{
    private readonly ConcurrentDictionary<ThreadKey, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<ThreadKey, JsonElement> _staged = new();
    private readonly ConcurrentDictionary<ThreadKey, SemaphoreSlim> _gates = new();
    public Task<AgentSession> GetOrCreate(string scopeId, AgentType agentType, Func<Task<AgentSession>> factory) => GetOrCreate(scopeId, agentType, factory, _ => factory());
    public async Task<AgentSession> GetOrCreate(string scopeId, AgentType agentType, Func<Task<AgentSession>> factory, Func<JsonElement, Task<AgentSession>> restore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        if (agentType == AgentType.None) throw new ArgumentException("An agent type is required.");
        var key = new ThreadKey(scopeId, agentType);
        var gate = _gates.GetOrAdd(key, _ => new(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_sessions.TryGetValue(key, out var existing)) return existing;
            var checkpoint = repository is null ? null : await repository.GetAsync(scopeId, $"agent-{agentType}");
            AgentSession session;
            if (checkpoint is null) session = await factory();
            else { using var document = JsonDocument.Parse(checkpoint.Payload); session = await restore(document.RootElement.Clone()); }
            _sessions[key] = session;
            return session;
        }
        finally { gate.Release(); }
    }
    public void Stage(string scopeId, AgentType agentType, JsonElement checkpoint) => _staged[new(scopeId, agentType)] = checkpoint.Clone();
    public async Task CommitScopeAsync(string scopeId)
    {
        foreach (var key in _staged.Keys.Where(k => k.ScopeId == scopeId).ToList())
        {
            if (!_staged.TryGetValue(key, out var json)) continue;
            if (repository is not null)
            {
                var previous = await repository.GetAsync(scopeId, $"agent-{key.AgentType}");
                await repository.WriteAsync(new() { Id = $"agent-{key.AgentType}", ScopeId = scopeId, Kind = "agent",
                    Version = (previous?.Version ?? 0) + 1, UpdatedAt = DateTime.UtcNow, Payload = json.GetRawText() }, previous?.Version ?? 0);
            }
            _staged.TryRemove(key, out _);
        }
    }
    public void DiscardScope(string scopeId) => TryRemoveScope(scopeId);
    public bool TryRemoveScope(string scopeId)
    {
        var removed = false;
        foreach (var key in _sessions.Keys.Concat(_staged.Keys).Where(k => k.ScopeId == scopeId).Distinct().ToList())
        { removed |= _sessions.TryRemove(key, out _); _staged.TryRemove(key, out _); }
        return removed;
    }
    private readonly record struct ThreadKey(string ScopeId, AgentType AgentType);
}
