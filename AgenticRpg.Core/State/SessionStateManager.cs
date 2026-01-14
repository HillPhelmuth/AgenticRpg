using System.Collections.Concurrent;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Agents; // For Message class
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Core.State;

/// <summary>
/// Manages pre-game session state with in-memory caching
/// </summary>
public class SessionStateManager(ILogger<SessionStateManager> logger) : ISessionStateManager
{
    private readonly ILogger<SessionStateManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<string, SessionState> _sessionCache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    
    // Maximum conversation history size per agent
    private const int MaxConversationHistory = 100;

    /// <inheritdoc/>
    public async Task<SessionState> CreateSessionAsync(SessionType sessionType, string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            throw new ArgumentException("Player ID cannot be null or empty", nameof(playerId));
        }
        
        var session = new SessionState
        {
            SessionId = Guid.NewGuid().ToString(),
            SessionType = sessionType,
            PlayerId = playerId,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            Context = new SessionContext(),
            ConversationHistory = [],
            Metadata = [],
            IsCompleted = false
        };
        
        _sessionCache.TryAdd(session.SessionId, session);
        
        _logger.LogInformation(
            "Created new {SessionType} session {SessionId} for player {PlayerId}",
            sessionType, session.SessionId, playerId);
        
        await Task.CompletedTask;
        return session;
    }
    
    /// <inheritdoc/>
    public async Task<SessionState?> GetSessionStateAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            _logger.LogWarning("GetSessionStateAsync called with null or empty sessionId");
            return null;
        }
        
        if (_sessionCache.TryGetValue(sessionId, out var session))
        {
            // Check if session has expired
            if (DateTime.UtcNow > session.ExpiresAt)
            {
                _logger.LogWarning(
                    "Session {SessionId} has expired (expired at {ExpiresAt})",
                    sessionId, session.ExpiresAt);
                
                // Remove expired session
                _sessionCache.TryRemove(sessionId, out _);
                return null;
            }
            
            _logger.LogDebug("Retrieved session state from cache: {SessionId}", sessionId);
            return session;
        }
        
        _logger.LogWarning("Session not found: {SessionId}", sessionId);
        await Task.CompletedTask;
        return null;
    }
    
    /// <inheritdoc/>
    public Task<bool> UpdateSessionStateAsync(SessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (string.IsNullOrEmpty(state.SessionId))
        {
            throw new ArgumentException("SessionState must have a valid SessionId", nameof(state));
        }
        
        state.LastUpdatedAt = DateTime.UtcNow;
        
        _sessionCache.AddOrUpdate(state.SessionId, state, (key, oldValue) => state);
        
        _logger.LogDebug("Updated session state: {SessionId}", state.SessionId);
        
        
        return Task.FromResult(true);
    }
    
    /// <inheritdoc/>
    public Task<bool> DeleteSessionAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return Task.FromResult(false);
        }
        
        var removed = _sessionCache.TryRemove(sessionId, out var session);
        
        if (removed)
        {
            _logger.LogInformation("Deleted session: {SessionId}", sessionId);
        }
        else
        {
            _logger.LogWarning("Failed to delete session (not found): {SessionId}", sessionId);
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc/>
    public async Task AddMessageAsync(string sessionId, string agentType, Message message)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            throw new ArgumentException("SessionId cannot be null or empty", nameof(sessionId));
        }
        
        if (string.IsNullOrEmpty(agentType))
        {
            throw new ArgumentException("AgentType cannot be null or empty", nameof(agentType));
        }
        
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }
        
        await _lock.WaitAsync();
        try
        {
            var session = await GetSessionStateAsync(sessionId);
            
            if (session == null)
            {
                _logger.LogWarning(
                    "Cannot add message - session not found: {SessionId}",
                    sessionId);
                return;
            }
            
            if (!session.ConversationHistory.TryGetValue(agentType, out var history))
            {
                history = [];
                session.ConversationHistory[agentType] = history;
            }

            history.Add(message);
            
            // Trim history if it exceeds maximum
            if (history.Count > MaxConversationHistory)
            {
                var removeCount = history.Count - MaxConversationHistory;
                history.RemoveRange(0, removeCount);
                
                _logger.LogDebug(
                    "Trimmed {Count} old messages from conversation history for session {SessionId}, agent {AgentType}",
                    removeCount, sessionId, agentType);
            }
            
            session.LastUpdatedAt = DateTime.UtcNow;
            await UpdateSessionStateAsync(session);
            
            _logger.LogDebug(
                "Added {Role} message to session {SessionId}, agent {AgentType}",
                message.Role, sessionId, agentType);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> CompleteSessionAsync(string sessionId, string? resultEntityId = null)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }
        
        var session = await GetSessionStateAsync(sessionId);
        
        if (session == null)
        {
            _logger.LogWarning("Cannot complete session - not found: {SessionId}", sessionId);
            return false;
        }
        
        session.IsCompleted = true;
        session.ResultEntityId = resultEntityId;
        session.LastUpdatedAt = DateTime.UtcNow;
        
        await UpdateSessionStateAsync(session);
        
        _logger.LogInformation(
            "Completed session {SessionId} with result entity {ResultEntityId}",
            sessionId, resultEntityId ?? "none");
        
        return true;
    }
}
