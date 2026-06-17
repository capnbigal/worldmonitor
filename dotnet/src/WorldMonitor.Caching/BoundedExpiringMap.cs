using WorldMonitor.Data.Time;

namespace WorldMonitor.Caching;

/// <summary>Thread-safe map with absolute-expiry entries and FIFO eviction at a fixed cap.
/// Mirrors the legacy isolate-local fallback Maps; time is supplied by IClock for determinism.</summary>
public sealed class BoundedExpiringMap<TValue>(IClock clock, int maxEntries)
{
    private readonly object _gate = new();
    private readonly LinkedList<string> _order = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, DateTime ExpiresAt, TValue Value)> _map = new();

    public void Set(string key, TValue value, TimeSpan ttl)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing)) _order.Remove(existing.Node);
            var node = _order.AddLast(key);
            _map[key] = (node, clock.UtcNow + ttl, value);
            while (_map.Count > maxEntries)
            {
                var oldest = _order.First!;
                _order.RemoveFirst();
                _map.Remove(oldest.Value);
            }
        }
    }

    public bool TryGet(string key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var e))
            {
                if (e.ExpiresAt > clock.UtcNow) { value = e.Value; return true; }
                _order.Remove(e.Node);
                _map.Remove(key);
            }
            value = default!;
            return false;
        }
    }
}
