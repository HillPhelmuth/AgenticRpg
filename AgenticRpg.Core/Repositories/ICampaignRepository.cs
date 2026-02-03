using AgenticRpg.Core.Models;

namespace AgenticRpg.Core.Repositories;

/// <summary>
/// Repository interface for Campaign persistence
/// </summary>
public interface ICampaignRepository
{
    /// <summary>
    /// Gets a campaign by ID
    /// </summary>
    /// <param name="id">The campaign ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The campaign, or null if not found</returns>
    Task<Campaign?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all campaigns with optional pagination
    /// </summary>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of campaigns</returns>
    Task<IEnumerable<Campaign>> GetAllAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a new campaign
    /// </summary>
    /// <param name="campaign">The campaign to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created campaign</returns>
    Task<Campaign> CreateAsync(Campaign campaign, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates an existing campaign
    /// </summary>
    /// <param name="campaign">The campaign to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated campaign</returns>
    Task<Campaign> UpdateAsync(Campaign campaign, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a campaign
    /// </summary>
    /// <param name="id">The campaign ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deletion was successful</returns>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets campaigns by owner ID
    /// </summary>
    /// <param name="ownerId">The owner/creator ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of campaigns owned by the user</returns>
    Task<IEnumerable<Campaign>> GetByOwnerIdAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a campaign by invitation code and records the invited user.
    /// </summary>
    /// <param name="invitationCode">The invitation code provided to the player</param>
    /// <param name="userId">The user ID to record as invited</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The campaign, or null if not found</returns>
    Task<Campaign?> GetByInvitationCodeAsync(string invitationCode, string userId, CancellationToken cancellationToken = default);
}