using AgenticRpg.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.Cosmos;

namespace AgenticRpg.Core.Repositories;

public class GameStateRepository(CosmosClient cosmosClient) : CosmosDbRepository<GameState>(cosmosClient, DatabaseName, ContainerName, PartitionKeyPath), IGameStateRepository
{
    private const string DatabaseName = "AgenticRpgDb";
    private const string ContainerName = "GameStates";
    private const string PartitionKeyPath = "/CampaignId";
    public async Task<GameState?> GetStateByIdAsync(string id, string campaignId,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetByIdAsync(id, campaignId, cancellationToken: cancellationToken);
        return response;
    }
    public async Task<GameState?> GetStateByCampaignIdAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        // Reason: GameState serializes the computed property name as "CampaignId" (PascalCase).
        var query = new QueryDefinition("SELECT * FROM c WHERE c.CampaignId = @campaignId")
            .WithParameter("@campaignId", campaignId);
        var iterator = _container.GetItemQueryIterator<GameState>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(campaignId),
            MaxItemCount = 1
        });
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            foreach (var item in response)
            {
                return item;
            }
        }
        return null;
    }
    public async Task<IEnumerable<GameState>> GetAllStatesAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        return await base.GetAllAsync(skip, take, cancellationToken: cancellationToken);
    }

    public async Task<GameState> CreateAsync(GameState gameState)
    {
        var response = await base.CreateAsync(gameState, gameState.CampaignId);
        return response;
    }
    

    public async Task<GameState> UpdateAsync(GameState gameState, CancellationToken cancellationToken = default)
    {
        var response = await base.UpdateAsync(gameState, gameState.Id, gameState.CampaignId, cancellationToken: cancellationToken);
        return response;
    }

    public async Task<bool> DeleteStateAsync(string id, string campaignId, CancellationToken cancellationToken = default)
    {
        var response = await base.DeleteAsync(id, campaignId, cancellationToken: cancellationToken);
        return response;
    }

    
}