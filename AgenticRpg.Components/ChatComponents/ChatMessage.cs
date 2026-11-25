using AgenticRpg.Core.Messaging;

namespace AgenticRpg.Components.ChatComponents;

/// <summary>
/// Represents a single message in a chat conversation between user and AI agent.
/// </summary>
public class ChatMessage
{
    public string? SuggestionContext { get; set; }
    public bool WasRejected { get; set; }
    public AgenticRpg.Core.Models.PlayerActionIntent? ActionIntent { get; set; }
    /// <summary>
    /// Unique identifier for the message.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The content of the message (supports markdown).
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Whether this message is from the user (true) or AI agent (false).
    /// </summary>
    public bool IsUser { get; set; }

    /// <summary>
    /// Timestamp when the message was created.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional: Player identifier for multi-player scenarios.
    /// </summary>
    public string? PlayerId { get; set; }

    /// <summary>
    /// Optional: Player name for display.
    /// </summary>
    public string? PlayerName { get; set; }

    /// <summary>
    /// Optional: Client-side identifier used for correlating queue updates.
    /// </summary>
    public string? ClientMessageId { get; set; }

    /// <summary>
    /// Latest queue status reported by the server.
    /// </summary>
    public AgenticRpg.Core.Models.ProcessingPhase? Phase { get; set; }
    public AgenticRpg.Core.Models.PlayerFacingError? Failure { get; set; }
    public bool IsAuthoritative { get; set; }
    public string? ProgressDetail { get; set; }
    public MessageProcessingStatus? Status { get; set; }

    /// <summary>
    /// Optional queue position supplied with status updates.
    /// </summary>
    public int? QueuePosition { get; set; }

    /// <summary>
    /// Optional status note (typically failure details).
    /// </summary>
    public string? StatusNote { get; set; }

    /// <summary>
    /// Whether the message is currently being edited.
    /// </summary>
    public bool IsEditing { get; set; }
    public bool IsStreaming { get; set; }
    /// <summary>
    /// List of image URLs embedded in the message.
    /// </summary>
    public List<string> ImageUrls { get; set; } = [];
    public List<string> SuggestedActions { get; set; } = [];
}
