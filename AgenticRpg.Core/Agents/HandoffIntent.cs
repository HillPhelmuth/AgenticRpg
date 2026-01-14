namespace AgenticRpg.Core.Agents;

/// <summary>
/// Represents an intent to hand off from one agent to another
/// </summary>
public class HandoffIntent
{
    /// <summary>
    /// The target agent type to hand off to
    /// </summary>
    public string TargetAgent { get; set; } = string.Empty;
    
    /// <summary>
    /// Context information for the handoff
    /// </summary>
    public string Context { get; set; } = string.Empty;
    
    /// <summary>
    /// Reason for the handoff
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether a handoff was detected
    /// </summary>
    public bool HandoffDetected { get; set; }
}
