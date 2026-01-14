using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Repositories;

/// <summary>
/// Repository interface for Character persistence
/// </summary>
public interface ICharacterRepository
{
    /// <summary>
    /// Gets a character by ID
    /// </summary>
    /// <param name="id">The character ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The character, or null if not found</returns>
    Task<Character?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all characters in a campaign
    /// </summary>
    /// <param name="campaignId">The campaign ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of characters in the campaign</returns>
    Task<IEnumerable<Character>> GetByCampaignIdAsync(string campaignId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all characters owned by a player
    /// </summary>
    /// <param name="playerId">The player ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of characters owned by the player</returns>
    Task<IEnumerable<Character>> GetByPlayerIdAsync(string playerId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a new character
    /// </summary>
    /// <param name="character">The character to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created character</returns>
    Task<Character> CreateAsync(Character character, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates an existing character
    /// </summary>
    /// <param name="character">The character to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated character</returns>
    Task<Character> UpdateAsync(Character character, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a character
    /// </summary>
    /// <param name="id">The character ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deletion was successful</returns>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
