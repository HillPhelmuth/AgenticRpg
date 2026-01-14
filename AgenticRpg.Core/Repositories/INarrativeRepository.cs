using AgenticRpg.Core.Models;

namespace AgenticRpg.Core.Repositories;

/// <summary>
/// Repository interface for Narrative persistence
/// </summary>
public interface INarrativeRepository
{
    /// <summary>
    /// Gets a narrative entry by ID
    /// </summary>
    /// <param name="id">The narrative ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The narrative, or null if not found</returns>
    Task<Narrative?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets narrative entries for a campaign with pagination
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="takeLast"></param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of narrative entries ordered by timestamp</returns>
    Task<IEnumerable<Narrative>> GetByCampaignIdAsync(string campaignId,
        int takeLast = 50,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets narrative entries visible to a specific character
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="characterId">The character ID</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of narrative entries visible to the character</returns>
    Task<IEnumerable<Narrative>> GetVisibleToCharacterAsync(
        string campaignId,
        string characterId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a new narrative entry
    /// </summary>
    /// <param name="narrative">The narrative to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created narrative</returns>
    Task<Narrative> CreateAsync(Narrative narrative, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates multiple narrative entries in a batch
    /// </summary>
    /// <param name="narratives">The narratives to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created narratives</returns>
    Task<IEnumerable<Narrative>> CreateBatchAsync(
        IEnumerable<Narrative> narratives, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes old narrative entries based on TTL or count limit
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="keepCount">Number of recent entries to keep</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of entries deleted</returns>
    Task<int> DeleteOldEntriesAsync(
        string campaignId, 
        int keepCount = 1000, 
        CancellationToken cancellationToken = default);
}
