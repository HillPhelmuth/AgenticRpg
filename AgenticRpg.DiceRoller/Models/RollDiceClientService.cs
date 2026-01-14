using Microsoft.AspNetCore.Components;

namespace AgenticRpg.DiceRoller.Models;

public class RollDiceClientService : IRollDiceService
{
    public List<DieRoleModalReference> DieRollReferences { get; set; } = [];
    public bool IsOpen { get; set; }
    public event Action<Guid, RollDiceResults?>? OnDiceRollComplete;
    public event Action<Guid, DiceRollComponentType, RollDiceParameters?, RollDiceWindowOptions?>? OnDiceRollRequest;

    public async Task<List<RollDiceResults?>> RequestDiceRoll(string sessionId, RollDiceParameters? parameters = null,
        RollDiceWindowOptions? options = null, int numberOfRollWindows = 1)
    {
        List<Task<RollDiceResults>> tasks = new();
        Console.WriteLine($"Requesting {numberOfRollWindows} dice rolls");
        for (var i = 0; i < numberOfRollWindows; i++)
        {
            TaskCompletionSource<RollDiceResults?> taskCompletionSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            DieRoleModalReference modalRef = new() { TaskCompletionSource = taskCompletionSource };
            DieRollReferences.Add(modalRef);
            OnDiceRollRequest?.Invoke(modalRef.Id, DiceRollComponentType.DiceRoller, parameters, options);
            var task = modalRef.TaskCompletionSource.Task;
            tasks.Add(task);
        }

        var results = new List<RollDiceResults>();
        foreach (var task in tasks)
        {
            var result = await task;
            if (result != null)
            {
                results.Add(result);
            }
            else
            {
                results.Add(RollDiceResults.Empty(false));
            }
        }
        return results.ToList();
    }

    public void CloseSelf(Guid modalId, RollDiceResults? results = null)
    {
        Close(modalId, results);
    }
   
    public void Close(Guid modalId, RollDiceResults? result)
    {
        var modalRef = DieRollReferences.FirstOrDefault(x => x.Id == modalId);
        if (modalRef != null)
        {
            try
            {
                DieRollReferences.Remove(modalRef);
            }
            catch (Exception e)
            {
                // TEMP Console.WriteLine("Die roll refernce removal error:\n\n"+ e);
            }
            OnClose(modalId, result);
        }
        var taskCompletion = modalRef?.TaskCompletionSource;
        if (taskCompletion == null || taskCompletion.Task.IsCompleted) return;
        taskCompletion.SetResult(result);
        
        // Update IsOpen based on whether any references remain
        IsOpen = DieRollReferences.Count > 0;
    }
    private void OnClose(Guid modalId, RollDiceResults? results)
    {
        OnDiceRollComplete?.Invoke(modalId, results);
    }   
}

public class DieRoleModalReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? SessionId { get; set; }
    public TaskCompletionSource<RollDiceResults?> TaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string RequestId => Id.ToString("N");
}

public enum Location
{
    Center, Left, Right, TopLeft, TopRight, Bottom
}
