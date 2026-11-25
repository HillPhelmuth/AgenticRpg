using AgenticRpg.Core.Repositories;
using System.Text.Json;
using AgenticRpg.Core.Agents; // For Message class
using AgenticRpg.Core.Hubs;
using AgenticRpg.Core.Services;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.State;

/// <summary>
/// Manages pre-game session state with in-memory caching
/// </summary>
public class SessionStateManager(ILogger<SessionStateManager> logger, IHubContext<GameHub> hubContext, ScopeOperationLocks locks, IRecoveryRepository? recovery = null) : ISessionStateManager
{
    private readonly IRecoveryRepository _recovery = recovery ?? new InMemoryRecoveryRepository();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly ILogger<SessionStateManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<string, SessionState> _sessionCache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    
    // Maximum conversation history size per agent
    private const int MaxConversationHistory = 100;

    /// <inheritdoc/>
    public async Task<SessionState> CreateSessionAsync(SessionType sessionType, string playerId, World? world = null)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            throw new ArgumentException("Player ID cannot be null or empty", nameof(playerId));
        }

        var sessionContext = world is not null ? new SessionContext(){ DraftWorld = world, OriginalWorldId = world.Id } : new SessionContext();
        var session = new SessionState
        {
            SessionId = Guid.NewGuid().ToString(),
            SessionType = sessionType,
            PlayerId = playerId,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            Context = sessionContext,
            ConversationHistory = [],
            Metadata = [],
            IsCompleted = false
        };
        
        await UpdateSessionStateAsync(session);
        
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
        
        if (_sessionCache.TryGetValue(sessionId, out var cached) && cached.ExpiresAt < DateTime.UtcNow && !locks.HasActiveRequests(sessionId))
            _sessionCache.TryRemove(sessionId, out _);
        if (_sessionCache.TryGetValue(sessionId, out var session))
        {
            _logger.LogDebug("Retrieved session state from cache: {SessionId}", sessionId);
            return session;
        }
        
        var document = await _recovery.GetAsync(sessionId, "session");
        if (document is null) return null;
        var restored = document.Read<SessionState>();
        restored.ExpiresAt = DateTime.UtcNow.AddHours(24);
        _sessionCache.TryAdd(sessionId, restored);
        return _sessionCache[sessionId];
    }
    
    /// <inheritdoc/>
    public async Task<bool> UpdateSessionStateAsync(SessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _saveGate.WaitAsync();
        try
        {
            var expected = state.Revision;
            var savedAt = DateTime.UtcNow;
            var payload = JsonSerializer.SerializeToNode(state, RecoveryJson.Options)!;
            payload["revision"] = expected + 1;
            payload["lastUpdatedAt"] = savedAt;
            payload["lastSavedAt"] = savedAt;
            await _recovery.WriteAsync(new RecoveryDocument { Id = "session", ScopeId = state.SessionId,
                OwnerId = state.PlayerId, Kind = "session", Version = expected + 1, UpdatedAt = savedAt,
                Payload = payload.ToJsonString(RecoveryJson.Options) }, expected);
            state.Revision = expected + 1;
            state.LastSavedAt = state.LastUpdatedAt = savedAt;
            state.ExpiresAt = savedAt.AddHours(24);
            _sessionCache[state.SessionId] = state;
            return true;
        }
        finally { _saveGate.Release(); }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteSessionAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }
        
        await _recovery.DeleteScopeAsync(sessionId);
        var removed = _sessionCache.TryRemove(sessionId, out var session);
        
        if (removed)
        {
            _logger.LogInformation("Deleted session: {SessionId}", sessionId);
        }
        else
        {
            _logger.LogWarning("Failed to delete session (not found): {SessionId}", sessionId);
        }

        return true;
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
