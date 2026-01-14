namespace AgenticRpg.Core.Agents.Tools.Results;

/// <summary>
/// Result from initiating an interactive attribute dice roll.
/// </summary>
public class AttributeDiceRollResult
{
    /// <summary>
    /// Whether the tool invocation was successful.
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Error message if unsuccessful.
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// Success message describing the action.
    /// </summary>
    public string? Message { get; set; }
    
    /// <summary>
    /// The name of the attribute being rolled for.
    /// </summary>
    public string? AttributeName { get; set; }
    
    /// <summary>
    /// Instructions for the next step (using RollPlayerDice tool).
    /// </summary>
    public string? Instructions { get; set; }
}
