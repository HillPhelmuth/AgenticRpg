using AgenticRpg.Core.Models.Enums;
using Microsoft.Agents.AI;

namespace AgenticRpg.Core.Agents.Threads;

public interface IAgentThreadStore
{
    Task<AgentSession> GetOrCreate(string scopeId, AgentType agentType, Func<Task<AgentSession>> factory);
    bool TryRemoveScope(string scopeId);
}
