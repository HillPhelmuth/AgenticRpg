using System.ComponentModel;
using System.Text.Json;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Agents.Tools;

public partial class GameMasterTools
{
    [Description("Transfers control to a specialized agent. Parameters: targetAgent (CharacterCreation, Combat, Economy, WorldBuilder, CharacterLevelUp), context (brief description of why handoff is needed), campaignId. Returns confirmation that the specified agent is now active.")]
    public async Task<string> HandoffToAgent(
        [Description("The specialized agent to transfer control to. Must be one of: CharacterCreation, Combat, Economy, WorldBuilder, CharacterLevelUp.")] AgentType targetAgent,
        [Description("A brief explanation of why control is being transferred to this agent. Provides context for the handoff (e.g., 'Player wants to buy items' for Economy).")] string context,
        [Description("The unique ID of the campaign where this handoff is occurring.")] string campaignId)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState == null)
        {
            return JsonSerializer.Serialize(new HandoffToAgentResult
            {
                Success = false,
                Message = string.Empty,
                Error = "Campaign not found"
            });
        }

        if (targetAgent == AgentType.Combat)
        {
            return JsonSerializer.Serialize(new HandoffToAgentResult
            {
                Success = false, Message = string.Empty,
                Error = "Use the InitiateCombat tool to handoff to the Combat Agent."
            });
        }

        gameState.ActiveAgent = targetAgent.ToString();
        gameState.Metadata["HandoffContext"] = context;
        await stateManager.UpdateCampaignStateAsync(gameState);

        return JsonSerializer.Serialize(new HandoffToAgentResult
        {
            Success = true,
            PreviousAgent = "GameMaster",
            NewAgent = targetAgent.ToString(),
            Context = context,
            Message = $"Control transferred to {targetAgent} Agent"
        });
    }
}
