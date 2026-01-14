using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgenticRpg.Core.Hubs;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.SignalR;

namespace AgenticRpg.Core.Services;

public class RollDiceHubService(IHubContext<GameHub> hubContext) : IRollDiceService
{
    private readonly object _syncRoot = new();

    public bool IsOpen { get; set; }
    public List<DieRoleModalReference> DieRollReferences { get; set; } = [];
    public event Action<Guid, RollDiceResults?>? OnDiceRollComplete;
    public event Action<Guid, DiceRollComponentType, RollDiceParameters?, RollDiceWindowOptions?>? OnDiceRollRequest;

    public async Task<List<RollDiceResults?>> RequestDiceRoll(string sessionId, RollDiceParameters? parameters = null,
        RollDiceWindowOptions? options = null, int numberOfRollWindows = 1)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session or campaign id is required for dice rolling", nameof(sessionId));
        }

        if (numberOfRollWindows < 1)
        {
            numberOfRollWindows = 1;
        }

        var modalRefs = new List<DieRoleModalReference>(capacity: numberOfRollWindows);
        lock (_syncRoot)
        {
            for (var i = 0; i < numberOfRollWindows; i++)
            {
                var modalRef = new DieRoleModalReference();
                DieRollReferences.Add(modalRef);
                modalRefs.Add(modalRef);
            }
        }

        var diceParameters = parameters ?? new RollDiceParameters();
        var windowOptions = options ?? new RollDiceWindowOptions();

        var dieType = diceParameters.DieType;
        diceParameters.Set("DieType", dieType);
        var rollCount = diceParameters.NumberOfRolls;
        diceParameters.Set("NumberOfRolls",rollCount);
        var modifier = diceParameters.Get("Modifier", 0);
        var isManual = diceParameters.IsManual;
        var dropLowest = diceParameters.Get("DropLowest", false);
        var playerId = diceParameters.Get("PlayerId", (string?)null);

        // One request containing N windows, with per-window RequestIds so the server can match
        // each returned result to the correct pending modal reference.
        var request = new DiceRollRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            WindowRequestIds = modalRefs.Select(r => r.RequestId).ToList(),
            SessionId = sessionId,
            CampaignId = sessionId,
            PlayerId = string.IsNullOrWhiteSpace(playerId) ? null : playerId,
            Parameters = diceParameters,
            WindowOptions = windowOptions,
            ReasonForRolling = windowOptions.Title ?? string.Empty,
            DieType = dieType,
            NumberOfDice = rollCount,
            NumberOfRollWindows = numberOfRollWindows,
            Modifier = modifier,
            IsManual = isManual,
            DropLowest = dropLowest,
            ComponentType = DiceRollComponentType.DiceRoller
        };

        await hubContext.Clients.Group(GameHub.GetCampaignGroupName(sessionId)).SendAsync("RequestDiceRoll", request);

        // Keep existing event signature by emitting a single request notification.
        OnDiceRollRequest?.Invoke(modalRefs.First().Id, request.ComponentType, diceParameters, windowOptions);

        var tasks = modalRefs.Select(r => r.TaskCompletionSource.Task).ToArray();
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    public void CloseSelf(Guid modalId, RollDiceResults? results = null)
    {
        Close(modalId, results);
    }

    public void Close(Guid modalId, RollDiceResults? result)
    {
        DieRoleModalReference? modalRef = null;

        lock (_syncRoot)
        {
            modalRef = DieRollReferences.FirstOrDefault(r => r.Id == modalId);

            if (modalRef != null)
            {
                DieRollReferences.Remove(modalRef);
            }
        }

        var taskCompletion = modalRef?.TaskCompletionSource;
        if (taskCompletion is { Task.IsCompleted: false })
        {
            taskCompletion.SetResult(result);
        }

        OnDiceRollComplete?.Invoke(modalId, result);
    }
}