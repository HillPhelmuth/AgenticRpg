using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using Microsoft.Azure.Cosmos;

namespace AgenticRpg.Core.Repositories;

/// <summary>
/// Cosmos DB implementation of Narrative repository
/// </summary>
public class NarrativeRepository(CosmosClient cosmosClient)
    : CosmosDbRepository<Narrative>(cosmosClient, DatabaseName, ContainerName, PartitionKeyPath), INarrativeRepository
{
    private const string DatabaseName = "AgenticRpgDb";
    private const string ContainerName = "Narratives";
    private const string PartitionKeyPath = "/id";

    public async Task<Narrative?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        // For cross-partition queries, we need to query instead of direct read
        var results = await QueryAsync(
            q => q.Where(n => n.Id == id),
            cancellationToken);
        
        return results.FirstOrDefault();
    }

    public async Task<IEnumerable<Narrative>> GetByCampaignIdAsync(string campaignId,
        int takeLast = 50,
        CancellationToken cancellationToken = default)
    {
        return await QueryAsync(
            q => q.Where(n => n.CampaignId == campaignId)
                  .OrderByDescending(n => n.SequenceNumber)
                  .Take(takeLast),
            cancellationToken);
    }

    public async Task<IEnumerable<Narrative>> GetVisibleToCharacterAsync(
        string campaignId,
        string characterId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        return await QueryAsync(
            q => q.Where(n => n.CampaignId == campaignId &&
                            (n.Visibility == NarrativeVisibility.Global ||
                             (n.Visibility == NarrativeVisibility.CharacterSpecific && n.TargetCharacterId == characterId)))
                  .OrderByDescending(n => n.SequenceNumber)
                  .Take(take),
            cancellationToken);
    }

    public async Task<Narrative> CreateAsync(Narrative narrative, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(narrative.Id))
        {
            narrative.Id = Guid.NewGuid().ToString();
        }

        narrative.Timestamp = DateTime.UtcNow;
        
        // Auto-generate sequence number if not set
        if (narrative.SequenceNumber == 0)
        {
            narrative.SequenceNumber = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        return await CreateAsync(narrative, narrative.Id, cancellationToken);
    }

    public async Task<IEnumerable<Narrative>> CreateBatchAsync(
        IEnumerable<Narrative> narratives,
        CancellationToken cancellationToken = default)
    {
        var created = new List<Narrative>();
        var timestamp = DateTime.UtcNow;
        var baseSequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        var index = 0;
        foreach (var narrative in narratives)
        {
            if (string.IsNullOrEmpty(narrative.Id))
            {
                narrative.Id = Guid.NewGuid().ToString();
            }

            narrative.Timestamp = timestamp;
            
            if (narrative.SequenceNumber == 0)
            {
                narrative.SequenceNumber = baseSequence + index;
            }

            var result = await CreateAsync(narrative, narrative.CampaignId, cancellationToken);
            created.Add(result);
            index++;
        }

        return created;
    }

    public async Task<int> DeleteOldEntriesAsync(
        string campaignId,
        int keepCount = 1000,
        CancellationToken cancellationToken = default)
    {
        // Get all narratives for campaign ordered by sequence
        var allNarratives = await QueryAsync(
            q => q.Where(n => n.CampaignId == campaignId)
                  .OrderByDescending(n => n.SequenceNumber),
            cancellationToken);

        var narrativesList = allNarratives.ToList();
        
        // If we have fewer than keepCount, nothing to delete
        if (narrativesList.Count <= keepCount)
        {
            return 0;
        }

        // Delete the oldest entries (those beyond keepCount)
        var toDelete = narrativesList.Skip(keepCount).ToList();
        var deleted = 0;

        foreach (var narrative in toDelete)
        {
            var success = await DeleteAsync(narrative.Id, narrative.CampaignId, cancellationToken);
            if (success)
            {
                deleted++;
            }
        }

        return deleted;
    }
}
