using AgenticRpg.Core.Models;
using Microsoft.Azure.Cosmos;

namespace AgenticRpg.Core.Repositories;

/// <summary>
/// Cosmos DB implementation of Campaign repository
/// </summary>
public class CampaignRepository(CosmosClient cosmosClient)
    : CosmosDbRepository<Campaign>(cosmosClient, DatabaseName, ContainerName, PartitionKeyPath), ICampaignRepository
{
    private const string DatabaseName = "AgenticRpgDb";
    private const string ContainerName = "Campaigns";
    private const string PartitionKeyPath = "/id";

    public async Task<Campaign?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await GetByIdAsync(id, id, cancellationToken);
        if (response is not null) return response;
        var allCampaigns = await GetAllAsync(cancellationToken: cancellationToken);
        return allCampaigns.FirstOrDefault(c => c.Id == id || c.Name == id);

    }

    public new async Task<IEnumerable<Campaign>> GetAllAsync(
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return await base.GetAllAsync(skip, take, cancellationToken);
    }

    public async Task<Campaign> CreateAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(campaign.Id))
        {
            campaign.Id = Guid.NewGuid().ToString();
        }

        campaign.CreatedAt = DateTime.UtcNow;
        campaign.UpdatedAt = DateTime.UtcNow;

        return await CreateAsync(campaign, campaign.Id, cancellationToken);
    }

    public async Task<Campaign> UpdateAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(campaign.Name)) return campaign;
        campaign.UpdatedAt = DateTime.UtcNow;
        return await UpdateAsync(campaign, campaign.Id, campaign.Id, null, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync(id, id, cancellationToken);
    }

    public async Task<IEnumerable<Campaign>> GetByOwnerIdAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        return await QueryAsync(
            q => q.Where(c => c.OwnerId == ownerId),
            cancellationToken);
    }
    public async Task<IEnumerable<Campaign>> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await QueryAsync(
            q => q.Where(c => c.Name == name),
            cancellationToken);
    }
}
