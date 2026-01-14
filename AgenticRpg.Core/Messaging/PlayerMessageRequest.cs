using System;
using System.Threading.Tasks;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Messaging;

/// <summary>
/// Represents a player-authored chat message that must be processed by an agent.
/// </summary>
public sealed class PlayerMessageRequest
{
    /// <summary>
    /// Campaign identifier associated with this request.
    /// </summary>
    public required string CampaignId { get; init; }

    /// <summary>
    /// Player identifier originating the message.
    /// </summary>
    public required string PlayerId { get; init; }

    /// <summary>
    /// Optional character identifier mapped to the player.
    /// </summary>
    public string? CharacterId { get; init; }

    /// <summary>
    /// Raw chat content to route through the active agent.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Optional explicit agent override (defaults to active campaign agent).
    /// </summary>
    public AgentType TargetAgentType { get; init; }

    /// <summary>
    /// Optional client generated message identifier for UI correlation.
    /// </summary>
    public string? ClientMessageId { get; init; }

    /// <summary>
    /// Optional callback invoked whenever queue status changes.
    /// </summary>
    public Func<MessageQueueUpdate, Task>? StatusCallback { get; init; }
}

/// <summary>
/// Represents a queue progress update pushed back to the caller.
/// </summary>
public sealed class MessageQueueUpdate
{
    /// <summary>
    /// Client supplied message identifier.
    /// </summary>
    public string MessageId { get; init; } = string.Empty;

    /// <summary>
    /// Status reported by the queue.
    /// </summary>
    public MessageProcessingStatus Status { get; init; }

    /// <summary>
    /// Optional queue position (0 == processing, higher == pending).
    /// </summary>
    public int? Position { get; init; }

    /// <summary>
    /// Optional human-readable note (typically populated for failures).
    /// </summary>
    public string? Note { get; init; }
}

/// <summary>
/// Possible queue states for a message.
/// </summary>
public enum MessageProcessingStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}
