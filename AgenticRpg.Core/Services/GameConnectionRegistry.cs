using System.Collections.Concurrent;

namespace AgenticRpg.Core.Services;

public sealed record GameConnectionBinding(string ScopeId, string PlayerId, string CharacterId, string PlayerName,
    bool IsSession, string LeaseId);

public sealed class GameConnectionRegistry
{
    private readonly SemaphoreSlim _membershipLock = new(1, 1);
    public async Task<IDisposable> LockAsync()
    {
        await _membershipLock.WaitAsync();
        return new Release(_membershipLock);
    }
    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
    private readonly ConcurrentDictionary<string, GameConnectionBinding> _connections = new();
    public GameConnectionBinding? Get(string connectionId) => _connections.GetValueOrDefault(connectionId);
    public void Attach(string connectionId, GameConnectionBinding binding) => _connections[connectionId] = binding;
    public GameConnectionBinding? Detach(string connectionId, string? leaseId = null)
    {
        if (!_connections.TryGetValue(connectionId, out var binding) ||
            (leaseId is not null && binding.LeaseId != leaseId)) return null;
        return ((ICollection<KeyValuePair<string, GameConnectionBinding>>)_connections)
            .Remove(new(connectionId, binding)) ? binding : null;
    }
    public KeyValuePair<string, GameConnectionBinding>[] InScope(string scopeId) =>
        _connections.Where(c => c.Value.ScopeId == scopeId).ToArray();
    public bool HasPlayer(string scopeId, string playerId) =>
        _connections.Any(c => c.Value.ScopeId == scopeId && c.Value.PlayerId == playerId);
}
