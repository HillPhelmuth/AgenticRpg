using AgenticRpg.Core.Models;

namespace AgenticRpg.Core.Repositories;

public interface IGameStateRepository
{
    /// <summary>
    /// Gets a campaign by ID
    /// </summary>
    /// <param name="id">The campaign ID</param>
    /// <param name="campaignId"></param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The campaign, or null if not found</returns>
    Task<GameState?> GetStateByIdAsync(string id, string campaignId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all campaigns with optional pagination
    /// </summary>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of campaigns</returns>
    Task<IEnumerable<GameState>> GetAllStatesAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new campaign
    /// </summary>
    /// <param name="gameState">The campaign to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created campaign</returns>
    Task<GameState> CreateAsync(GameState gameState);

    /// <summary>
    /// Updates an existing campaign
    /// </summary>
    /// <param name="gameState">The campaign to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated campaign</returns>
    Task<GameState> UpdateAsync(GameState gameState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a campaign
    /// </summary>
    /// <param name="id">The campaign ID</param>
    /// <param name="campaignId"></param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deletion was successful</returns>
    Task<bool> DeleteStateAsync(string id, string campaignId, CancellationToken cancellationToken = default);

    Task<GameState?> GetStateByCampaignIdAsync(string campaignId, CancellationToken cancellationToken = default);
}