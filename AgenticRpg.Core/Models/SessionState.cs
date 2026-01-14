using System.Collections.Concurrent;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Models.Game; // For Message class

namespace AgenticRpg.Core.Models;

/// <summary>
/// Represents the state of a pre-game session (character creation, world building, etc.)
/// </summary>
public class SessionState
{
    private string _sessionId = Guid.NewGuid().ToString();

    /// <summary>
    /// Unique session identifier
    /// </summary>
    public string SessionId
    {
        get
        {
            if (_sessionId.StartsWith("session_"))
                return _sessionId;
            _sessionId = "session_" + _sessionId;
            return  _sessionId;
        }
        set
        {
            if (value.StartsWith("session_"))
                _sessionId = value;
            else 
                _sessionId = "session_" + value;

        }
    }
    public string? SelectedModel { get; set; }
    /// <summary>
    /// Type of session
    /// </summary>
    public SessionType SessionType { get; set; }
    
    /// <summary>
    /// ID of the player who owns this session
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;
    
    /// <summary>
    /// When the session was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the session was last updated
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the session expires (for automatic cleanup)
    /// </summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    
    /// <summary>
    /// Session-specific context data
    /// </summary>
    public SessionContext Context { get; set; } = new();
    
    /// <summary>
    /// Conversation history per agent type
    /// AgentType -> List of Messages
    /// </summary>
    public Dictionary<string, List<Message>> ConversationHistory { get; set; } = [];
    
    /// <summary>
    /// Metadata and custom properties
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = [];
    
    /// <summary>
    /// Whether the session has been completed
    /// </summary>
    public bool IsCompleted { get; set; }
    
    /// <summary>
    /// ID of the resulting entity (character ID, world ID, campaign ID) after completion
    /// </summary>
    public string? ResultEntityId { get; set; }
}

/// <summary>
/// Context data specific to a session type
/// </summary>
public class SessionContext
{
    /// <summary>
    /// Draft character being created (for CharacterCreation sessions)
    /// </summary>
    public Character? DraftCharacter { get; set; }
    
    /// <summary>
    /// Draft world being built (for WorldBuilding sessions)
    /// </summary>
    public World? DraftWorld { get; set; }
    
    /// <summary>
    /// Draft campaign being set up (for CampaignSetup sessions)
    /// </summary>
    public Campaign? DraftCampaign { get; set; }
    
    /// <summary>
    /// Free-form working data as JSON
    /// </summary>
    public Dictionary<string, object> WorkingData { get; set; } = [];
    
    /// <summary>
    /// Current step or phase in the session workflow
    /// </summary>
    public string CurrentStep { get; set; } = "start";
    
    /// <summary>
    /// Steps completed in the session
    /// </summary>
    public List<string> CompletedSteps { get; set; } = [];
}
