using AgenticRpg.Core.Messaging;

namespace AgenticRpg.Core.Services;

/// <summary>Request-local identity and cancellation, including across agent handoffs and tools.</summary>
public static class RequestExecution
{
    private static readonly AsyncLocal<PlayerMessageRequest?> CurrentRequest = new();
    public static PlayerMessageRequest? Current => CurrentRequest.Value;
    public static CancellationToken Token => Current?.CancellationToken ?? default;
    public static IDisposable Enter(PlayerMessageRequest request)
    {
        var previous = CurrentRequest.Value;
        CurrentRequest.Value = request;
        return new Restore(() => CurrentRequest.Value = previous);
    }
    private sealed class Restore(Action action) : IDisposable { public void Dispose() => action(); }
}
