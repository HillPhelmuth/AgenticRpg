using System;
using System.Collections.Generic;
using System.Text;

namespace AgenticRpg.DiceRoller.Models;

public class DiceRollRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Optional per-window request IDs when <see cref="NumberOfRollWindows"/> is greater than 1.
    /// When provided, the client should submit exactly one <see cref="RollDiceResults"/> per entry,
    /// using the matching request id so the server can complete each pending window.
    /// </summary>
    public List<string>? WindowRequestIds { get; set; }

    public string SessionId { get; set; } = string.Empty;
    public string ReasonForRolling { get; set; } = string.Empty;
    public DieType DieType { get; set; }
    public int NumberOfDice { get; set; } = 1;
    public int NumberOfRollWindows { get; set; }
    public int Modifier { get; set; }
    public bool IsManual { get; set; } = true;
    public bool DropLowest { get; set; }
    public string? CampaignId { get; set; }
    public string? PlayerId { get; set; }
    public DiceRollComponentType ComponentType { get; set; } = DiceRollComponentType.DiceRoller;
    public RollDiceParameters Parameters { get; set; } = [];
    public RollDiceWindowOptions WindowOptions { get; set; } = new();
}