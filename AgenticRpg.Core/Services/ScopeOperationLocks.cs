using System.Collections.Concurrent;

namespace AgenticRpg.Core.Services;

/// <summary>Coordinates session submissions and finalization; AI tools can finalize their active request.</summary>
public sealed class ScopeOperationLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _completions = new();
    public SemaphoreSlim Completion(string scopeId) => _completions.GetOrAdd(scopeId, _ => new(1, 1));
    private readonly ConcurrentDictionary<string, int> _activeRequests = new();
    public bool HasActiveRequests(string scopeId) => _activeRequests.GetValueOrDefault(scopeId) > 0;
    public void BeginRequest(string scopeId) => _activeRequests.AddOrUpdate(scopeId, 1, (_, count) => count + 1);
    public void EndRequest(string scopeId) => _activeRequests.AddOrUpdate(scopeId, 0, (_, count) => Math.Max(0, count - 1));
    public async Task<IDisposable> AcquireCompletionAsync(string scopeId)
    {
        var gate = Completion(scopeId);
        await gate.WaitAsync();
        return new Release(gate);
    }
    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
