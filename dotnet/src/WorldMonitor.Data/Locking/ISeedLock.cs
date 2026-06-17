namespace WorldMonitor.Data.Locking;

/// <summary>Single-writer lock for seed publishes. TryAcquire returns null immediately on
/// contention (acquire-or-skip, never blocks). Dispose the handle to release.</summary>
public interface ISeedLock
{
    Task<ISeedLockHandle?> TryAcquireAsync(string resource, CancellationToken ct = default);
}

public interface ISeedLockHandle : IAsyncDisposable
{
    string Resource { get; }
}
