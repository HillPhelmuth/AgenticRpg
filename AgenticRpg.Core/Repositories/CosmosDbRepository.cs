using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace AgenticRpg.Core.Repositories;

/// <summary>
/// Generic base repository for Cosmos DB operations
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public abstract class CosmosDbRepository<T> where T : class
{
    protected readonly Container _container;
    protected readonly string _partitionKeyPath;

    /// <summary>
    /// Constructor for Cosmos DB repository
    /// </summary>
    /// <param name="cosmosClient">Cosmos DB client</param>
    /// <param name="databaseName">Database name</param>
    /// <param name="containerName">Container name</param>
    /// <param name="partitionKeyPath">Partition key path (e.g., "/id" or "/campaignId")</param>
    protected CosmosDbRepository(
        CosmosClient cosmosClient,
        string databaseName,
        string containerName,
        string partitionKeyPath)
    {
        var db = cosmosClient.GetDatabase(databaseName);
        var container = db.CreateContainerIfNotExistsAsync(new ContainerProperties {PartitionKeyPath = partitionKeyPath, Id = containerName }).GetAwaiter().GetResult();
        _container = container;
        _partitionKeyPath = partitionKeyPath;
    }

    /// <summary>
    /// Gets entity by ID
    /// </summary>
    protected async Task<T?> GetByIdAsync(string id, string partitionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<T>(
                id,
                new PartitionKey(partitionKey),
                cancellationToken: cancellationToken);
            
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets all entities with pagination
    /// </summary>
    protected async Task<IEnumerable<T>> GetAllAsync(
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = _container.GetItemLinqQueryable<T>()
            .Skip(skip)
            .Take(take);

        var results = new List<T>();
        using var iterator = query.ToFeedIterator();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    /// <summary>
    /// Queries entities with custom predicate
    /// </summary>
    protected async Task<IEnumerable<T>> QueryAsync(
        Func<IQueryable<T>, IQueryable<T>> queryBuilder,
        CancellationToken cancellationToken = default)
    {
        var queryable = _container.GetItemLinqQueryable<T>();
        var query = queryBuilder(queryable);

        var results = new List<T>();
        using var iterator = query.ToFeedIterator();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    /// <summary>
    /// Creates a new entity
    /// </summary>
    protected async Task<T> CreateAsync(
        T entity,
        string partitionKey,
        CancellationToken cancellationToken = default)
    {
        var response = await _container.UpsertItemAsync(
            entity,
            new PartitionKey(partitionKey),
            cancellationToken: cancellationToken);

        return response.Resource;
    }

    /// <summary>
    /// Updates an existing entity with optimistic concurrency
    /// </summary>
    protected async Task<T> UpdateAsync(
        T entity,
        string id,
        string partitionKey,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        var requestOptions = new ItemRequestOptions();
        
        if (!string.IsNullOrEmpty(etag))
        {
            requestOptions.IfMatchEtag = etag;
        }

        var response = await _container.UpsertItemAsync(
            entity,
            
            cancellationToken: cancellationToken);

        return response.Resource;
    }

    /// <summary>
    /// Upserts an entity (create if not exists, update if exists)
    /// </summary>
    protected async Task<T> UpsertAsync(
        T entity,
        string partitionKey,
        CancellationToken cancellationToken = default)
    {
        var response = await _container.UpsertItemAsync(
            entity,
            new PartitionKey(partitionKey),
            cancellationToken: cancellationToken);

        return response.Resource;
    }

    /// <summary>
    /// Deletes an entity
    /// </summary>
    protected async Task<bool> DeleteAsync(
        string id,
        string partitionKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.DeleteItemAsync<T>(
                id,
                new PartitionKey(partitionKey),
                cancellationToken: cancellationToken);
            
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// Counts entities matching a query
    /// </summary>
    protected async Task<int> CountAsync(
        Func<IQueryable<T>, IQueryable<T>>? queryBuilder = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<T> queryable = _container.GetItemLinqQueryable<T>();
        
        if (queryBuilder != null)
        {
            queryable = queryBuilder(queryable);
        }

        var results = new List<T>();
        using var iterator = queryable.ToFeedIterator();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results.Count;
    }

    /// <summary>
    /// Checks if entity exists
    /// </summary>
    protected async Task<bool> ExistsAsync(
        string id,
        string partitionKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.ReadItemAsync<T>(
                id,
                new PartitionKey(partitionKey),
                new ItemRequestOptions { ConsistencyLevel = ConsistencyLevel.Session },
                cancellationToken);
            
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
