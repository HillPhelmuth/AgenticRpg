using AgenticRpg.Core.Models.Enums;
using Microsoft.Agents.AI;

namespace AgenticRpg.Core.Agents.Threads;

public interface IAgentThreadStore
{
    AgentThread GetOrCreate(string scopeId, AgentType agentType, Func<AgentThread> factory);
    bool TryRemoveScope(string scopeId);
}
