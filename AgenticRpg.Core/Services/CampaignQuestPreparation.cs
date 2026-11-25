using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Services;

public interface ICampaignQuestPreparation
{
    Task PrepareAsync(Campaign campaign, World world);
}

public sealed class CampaignQuestPreparation : ICampaignQuestPreparation
{
    public async Task PrepareAsync(Campaign campaign, World world)
    {
        if (campaign.PrimaryQuest is not null) return;
        var description = campaign.Settings.GeneratePrimaryQuest
            ? "Create a primary quest that fits the world and the campaign description: " + campaign.Description
            : campaign.Settings.PrimaryQuestDescription;
        if (string.IsNullOrWhiteSpace(description)) description = campaign.Description;
        if (!string.IsNullOrWhiteSpace(description))
            campaign.PrimaryQuest = await QuestGenService.GeneratePrimaryQuest(world, description, campaign.Settings.Difficulty);
    }
}
