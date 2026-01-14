using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using Microsoft.Azure.Cosmos;

namespace AgenticRpg.Core.Repositories;

/// <summary>
/// Cosmos DB implementation of Character repository
/// </summary>
public class CharacterRepository(CosmosClient cosmosClient)
    : CosmosDbRepository<Character>(cosmosClient, DatabaseName, ContainerName, PartitionKeyPath), ICharacterRepository
{
    private const string DatabaseName = "AgenticRpgDb";
    private const string ContainerName = "Characters";
    private const string PartitionKeyPath = "/id";

    public async Task<Character?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        // For cross-partition queries, we need to query instead of direct read
        var results = await GetByIdAsync(id, id, cancellationToken);
        
        return results;
    }

    public async Task<IEnumerable<Character>> GetByCampaignIdAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        return await QueryAsync(
            q => q.Where(c => c.CampaignId == campaignId),
            cancellationToken);
    }

    public async Task<IEnumerable<Character>> GetByPlayerIdAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        return await QueryAsync(
            q => q.Where(c => c.PlayerId == playerId),
            cancellationToken);
    }

    public async Task<Character> CreateAsync(Character character, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(character.Id))
        {
            character.Id = Guid.NewGuid().ToString();
        }

        character.CreatedAt = DateTime.UtcNow;
        character.UpdatedAt = DateTime.UtcNow;

        return await CreateAsync(character, character.Id, cancellationToken);
    }

    public async Task<Character> UpdateAsync(Character character, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(character.Name)) return character;
        character.UpdatedAt = DateTime.UtcNow;
        return await UpdateAsync(character, character.Id, character.Id, null, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        // Need to find the character first to get its partition key (campaignId)
        var character = await GetByIdAsync(id, cancellationToken);
        
        if (character == null)
        {
            return false;
        }

        return await DeleteAsync(id, character.CampaignId, cancellationToken);
    }
}
