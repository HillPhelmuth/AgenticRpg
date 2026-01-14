using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Repositories;

/// <summary>
/// Repository interface for World persistence
/// </summary>
public interface IWorldRepository
{
    /// <summary>
    /// Gets a world by ID
    /// </summary>
    /// <param name="id">The world ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The world, or null if not found</returns>
    Task<World?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all worlds (useful for premade world templates)
    /// </summary>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of worlds</returns>
    Task<IEnumerable<World>> GetAllAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a new world
    /// </summary>
    /// <param name="world">The world to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created world</returns>
    Task<World> CreateAsync(World world, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates an existing world
    /// </summary>
    /// <param name="world">The world to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated world</returns>
    Task<World> UpdateAsync(World world, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a world
    /// </summary>
    /// <param name="id">The world ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deletion was successful</returns>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets template worlds (premade worlds for quick campaign setup)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of template worlds</returns>
    Task<IEnumerable<World>> GetTemplatesAsync(CancellationToken cancellationToken = default);
}
