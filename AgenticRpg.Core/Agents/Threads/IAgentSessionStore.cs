using AgenticRpg.Core.Models.Enums;
using Microsoft.Agents.AI;
using System.Text.Json;

namespace AgenticRpg.Core.Agents.Threads;

public interface IAgentSessionStore
{
    Task<AgentSession> GetOrCreate(string scopeId, AgentType agentType, Func<Task<AgentSession>> factory);
    Task<AgentSession> GetOrCreate(string scopeId, AgentType agentType, Func<Task<AgentSession>> factory,
        Func<JsonElement, Task<AgentSession>> restore) => GetOrCreate(scopeId, agentType, factory);
    void Stage(string scopeId, AgentType agentType, JsonElement checkpoint) { }
    Task CommitScopeAsync(string scopeId) => Task.CompletedTask;
    void DiscardScope(string scopeId) { }
    bool TryRemoveScope(string scopeId);
}
