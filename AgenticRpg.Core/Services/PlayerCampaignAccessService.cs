using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.State;

namespace AgenticRpg.Core.Services;

public sealed record PlayerCampaignAccess(bool Allowed, bool IsOwner, string? CharacterId);

/// <summary>Resolves private content using persisted ownership and the authenticated identity.</summary>
public sealed class PlayerCampaignAccessService(ICharacterRepository characters, ICampaignRepository campaigns,
    IGameStateManager states)
{
    public async Task<PlayerCampaignAccess> ResolveAsync(string campaignId, string? playerId, string? requestedCharacterId = null)
    {
        if (string.IsNullOrWhiteSpace(playerId)) return new(false, false, null);
        var campaign = await campaigns.GetByIdAsync(campaignId);
        if (campaign is null) return new(false, false, null);
        var owned = (await characters.GetByPlayerIdAsync(playerId)).Where(c => c.CampaignId == campaignId).ToList();
        var owner = campaign.OwnerId == playerId;
        if (!string.IsNullOrEmpty(requestedCharacterId))
            return owned.Any(c => c.Id == requestedCharacterId) ? new(true, owner, requestedCharacterId) : new(false, owner, null);
        if (!owner && owned.Count == 0) return new(false, false, null);
        var state = await states.GetCampaignStateAsync(campaignId);
        var activeId = state?.GetPlayerCharacterId(playerId);
        return new(true, owner, owned.FirstOrDefault(c => c.Id == activeId)?.Id ?? owned.FirstOrDefault()?.Id);
    }
}
