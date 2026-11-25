using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgenticRpg.Core.Hubs;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Core.Services;

public class RollDiceHubService(IHubContext<GameHub> hubContext, ILogger<RollDiceHubService>? logger = null, RuntimeProgress? progress = null) : IRollDiceService
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, DiceRollRequest> _pending = [];

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

        lock (_syncRoot) _pending[request.RequestId] = request;
        if (progress is not null) await progress.ReportAsync(sessionId, AgenticRpg.Core.Models.ProcessingPhase.WaitingForDice, $"Waiting for {(string.IsNullOrEmpty(playerId) ? "a roll" : playerId + " to roll")}: {request.ReasonForRolling}");
        try
        {
            await hubContext.Clients.Group(GameHub.GetCampaignGroupName(sessionId)).SendAsync("RequestDiceRoll", request);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Dice request {RequestId} retained for reconnect", request.RequestId);
        }

        // Keep existing event signature by emitting a single request notification.
        OnDiceRollRequest?.Invoke(modalRefs.First().Id, request.ComponentType, diceParameters, windowOptions);

        var tasks = modalRefs.Select(r => r.TaskCompletionSource.Task).ToArray();
        List<RollDiceResults?> results;
        try { results = (await Task.WhenAll(tasks).WaitAsync(RequestExecution.Token)).ToList(); }
        finally
        {
            lock (_syncRoot) _pending.Remove(request.RequestId);
            foreach (var modal in modalRefs) Close(modal.Id, null);
            try { await hubContext.Clients.Group(GameHub.GetCampaignGroupName(sessionId)).SendAsync("DiceRequestClosed", request.RequestId); }
            catch (Exception ex) { logger?.LogWarning(ex, "Dice cleanup will recover on reconnect"); }
        }
        if (progress is not null) await progress.ReportAsync(sessionId, AgenticRpg.Core.Models.ProcessingPhase.Resolving);
        return results;
    }

    public List<DiceRollRequest> GetPending(string scopeId, string playerId)
    {
        lock (_syncRoot)
        {
            return _pending.Values.Where(r => r.SessionId == scopeId && (string.IsNullOrEmpty(r.PlayerId) || r.PlayerId == playerId))
                .Select(r =>
                {
                    var windows = r.WindowRequestIds?.Where(id => DieRollReferences.Any(d => d.RequestId == id)).ToList() ?? [];
                    return new DiceRollRequest
                    {
                        RequestId = r.RequestId, WindowRequestIds = windows, SessionId = r.SessionId,
                        CampaignId = r.CampaignId, PlayerId = r.PlayerId, Parameters = r.Parameters, WindowOptions = r.WindowOptions,
                        ReasonForRolling = r.ReasonForRolling, DieType = r.DieType, NumberOfDice = r.NumberOfDice,
                        NumberOfRollWindows = windows.Count, Modifier = r.Modifier, IsManual = r.IsManual,
                        DropLowest = r.DropLowest, ComponentType = r.ComponentType
                    };
                }).Where(r => r.NumberOfRollWindows > 0).ToList();
        }
    }

    public void Submit(RollDiceResults result, string playerId)
    {
        lock (_syncRoot)
        {
            var request = _pending.Values.FirstOrDefault(r => r.WindowRequestIds?.Contains(result.RequestId) == true);
            if (request is null) return; // Already completed; duplicate delivery is harmless.
            if (request.SessionId != result.SessionId || (!string.IsNullOrEmpty(request.PlayerId) && request.PlayerId != playerId))
                throw new Microsoft.AspNetCore.SignalR.HubException("This dice request belongs to another player.");
            var modal = DieRollReferences.FirstOrDefault(d => d.RequestId == result.RequestId);
            if (modal is not null) Close(modal.Id, result);
        }
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
            taskCompletion.TrySetResult(result);
        }

        OnDiceRollComplete?.Invoke(modalId, result);
    }
}
