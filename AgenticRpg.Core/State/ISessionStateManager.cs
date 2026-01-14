using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Agents; // For Message class

namespace AgenticRpg.Core.State;

/// <summary>
/// Interface for managing pre-game session state (character creation, world building, etc.)
/// </summary>
public interface ISessionStateManager
{
    /// <summary>
    /// Creates a new session
    /// </summary>
    /// <param name="sessionType">Type of session to create</param>
    /// <param name="playerId">ID of the player creating the session</param>
    /// <returns>The created session state</returns>
    Task<SessionState> CreateSessionAsync(SessionType sessionType, string playerId);
    
    /// <summary>
    /// Gets session state by ID
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <returns>The session state, or null if not found</returns>
    Task<SessionState?> GetSessionStateAsync(string sessionId);
    
    /// <summary>
    /// Updates an existing session state
    /// </summary>
    /// <param name="state">The updated session state</param>
    /// <returns>True if update was successful</returns>
    Task<bool> UpdateSessionStateAsync(SessionState state);
    
    /// <summary>
    /// Deletes a session
    /// </summary>
    /// <param name="sessionId">The session ID to delete</param>
    /// <returns>True if deletion was successful</returns>
    Task<bool> DeleteSessionAsync(string sessionId);

    /// <summary>
    /// Adds a message to session conversation history
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="agentType">The agent type</param>
    /// <param name="message">The message to add</param>
    Task AddMessageAsync(string sessionId, string agentType, Message message);

    /// <summary>
    /// Marks a session as completed
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="resultEntityId">The ID of the resulting entity (character, world, campaign)</param>
    Task<bool> CompleteSessionAsync(string sessionId, string? resultEntityId = null);
}
