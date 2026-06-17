using System.Collections.Concurrent;

namespace WorldMonitor.Data.Locking;

/// <summary>Process-local lock for deterministic unit tests. Acquire-or-skip semantics.</summary>
public sealed class InMemorySeedLock : ISeedLock
{
    private readonly ConcurrentDictionary<string, byte> _held = new();

    public Task<ISeedLockHandle?> TryAcquireAsync(string resource, CancellationToken ct = default)
        => Task.FromResult<ISeedLockHandle?>(
            _held.TryAdd(resource, 0) ? new Handle(this, resource) : null);

    private sealed class Handle(InMemorySeedLock owner, string resource) : ISeedLockHandle
    {
        public string Resource => resource;
        public ValueTask DisposeAsync() { owner._held.TryRemove(resource, out _); return ValueTask.CompletedTask; }
    }
}
