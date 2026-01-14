using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using Microsoft.Azure.Cosmos;

namespace AgenticRpg.Core.Repositories;

/// <summary>
/// Cosmos DB implementation of World repository
/// </summary>
public class WorldRepository(CosmosClient cosmosClient)
    : CosmosDbRepository<World>(cosmosClient, DatabaseName, ContainerName, PartitionKeyPath), IWorldRepository
{
    private const string DatabaseName = "AgenticRpgDb";
    private const string ContainerName = "Worlds";
    private const string PartitionKeyPath = "/id";

    public async Task<World?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetByIdAsync(id, id, cancellationToken);
    }

    public new async Task<IEnumerable<World>> GetAllAsync(
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return await base.GetAllAsync(skip, take, cancellationToken);
    }

    public async Task<World> CreateAsync(World world, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(world.Id))
        {
            world.Id = Guid.NewGuid().ToString();
        }

        world.CreatedAt = DateTime.UtcNow;

        return await CreateAsync(world, world.Id, cancellationToken);
    }

    public async Task<World> UpdateAsync(World world, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(world.Name)) return world;
        return await UpdateAsync(world, world.Id, world.Id, null, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync(id, id, cancellationToken);
    }

    public async Task<IEnumerable<World>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync(
            q => q.Where(w => w.IsTemplate == true),
            cancellationToken);
    }
}
