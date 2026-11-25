using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Messaging;
using AgenticRpg.DiceRoller.Models;

namespace AgenticRpg.Core.Models;

public sealed class ScopeRecoverySnapshot
{
    public string ScopeId { get; set; } = string.Empty;
    public bool RequiresReview { get; set; }
    public string? TranscriptCursor { get; set; }
    public long DurableRevision { get; set; }
    public DateTime? LastSavedAt { get; set; }
    public bool HasAcceptedRequests { get; set; }
    public GameState? GameState { get; set; }
    public SessionState? SessionState { get; set; }
    public List<RuntimeMessage> Messages { get; set; } = [];
    public List<DiceRollRequest> PendingDice { get; set; } = [];
}

/// <summary>An authoritative, revisioned response. Replaying it never appends duplicate text.</summary>
public sealed class RuntimeMessage
{
    public PlayerActionIntent? ActionIntent { get; set; }
    public string? SuggestionContext { get; set; }
    public string ScopeId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string ClientMessageId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string UserText { get; set; } = string.Empty;
    public AgenticRpg.Core.Models.Enums.NarrativeVisibility Visibility { get; set; }
    public string? TargetCharacterId { get; set; }
    public bool Reviewed { get; set; }
    public bool NeedsReview => !Reviewed && (Status == MessageProcessingStatus.Interrupted || Status == MessageProcessingStatus.Failed && Failure?.Code == "action_failed");
    public string Content { get; set; } = string.Empty;
    public string AgentType { get; set; } = "Game Master";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long Revision { get; set; }
    public MessageProcessingStatus Status { get; set; } = MessageProcessingStatus.Queued;
    public int? QueuePosition { get; set; }
    public string? ProgressDetail { get; set; }
    public ProcessingPhase Phase { get; set; }
    public PlayerFacingError? Failure { get; set; }
    public string? Error { get; set; }
    public List<SuggestedAction> SuggestedActions { get; set; } = [];
    public bool IsComplete => Status is MessageProcessingStatus.Completed or MessageProcessingStatus.Failed or MessageProcessingStatus.Interrupted or MessageProcessingStatus.Cancelled;
}

public enum GameConnectionStatus { Connecting, Connected, Reconnecting, Disconnected }

public enum ProcessingPhase { Queued, Generating, WaitingForDice, Resolving, Saving, Completed, Failed, Interrupted, Cancelling, Cancelled }
public enum RecoveryAction { None, RetryConnection, ReloadState, RetrySave, RestoreText, ReviewState }
public sealed record PlayerFacingError(string Code, string Message, string CorrelationId, RecoveryAction Action);
public sealed class TranscriptPage
{
    public List<RuntimeMessage> Messages { get; set; } = [];
    public string? Cursor { get; set; }
}

public sealed record DraftSummary(string SessionId, AgenticRpg.Core.Models.Enums.SessionType SessionType, string Name, DateTime? LastSavedAt);
