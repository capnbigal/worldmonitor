using WorldMonitor.Data.Caching;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Caching.Tests.Fakes;

public sealed class InMemoryCacheStore(IClock clock) : ICacheStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Value, DateTime Exp, DateTime? Fetched, int? Rc)> _data = new();

    public bool FailReads { get; set; }
    public bool FailWrites { get; set; }
    public int UpsertCount { get; private set; }

    public Task<CacheReadResult> ReadAsync(string key, CancellationToken ct = default)
    {
        if (FailReads) return Task.FromResult(CacheReadResult.Error);
        lock (_gate)
        {
            if (_data.TryGetValue(key, out var e) && e.Exp > clock.UtcNow)
                return Task.FromResult(CacheReadResult.Hit(e.Value, e.Exp, e.Fetched, e.Rc));
            return Task.FromResult(CacheReadResult.Miss);
        }
    }

    public Task UpsertAsync(CacheUpsert e, CancellationToken ct = default)
    {
        if (FailWrites) throw new InvalidOperationException("simulated write failure");
        lock (_gate)
        {
            _data[e.Key] = (e.Value, clock.UtcNow + e.Ttl, e.FetchedAt ?? clock.UtcNow, e.RecordCount);
            UpsertCount++;
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExtendTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_data.TryGetValue(key, out var e) && e.Exp > clock.UtcNow)
            {
                _data[key] = (e.Value, clock.UtcNow + ttl, e.Fetched, e.Rc);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    public Task<IReadOnlyDictionary<string, string>> ReadManyAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var r = new Dictionary<string, string>();
            foreach (var k in keys)
                if (_data.TryGetValue(k, out var e) && e.Exp > clock.UtcNow) r[k] = e.Value;
            return Task.FromResult<IReadOnlyDictionary<string, string>>(r);
        }
    }

    public Task<IReadOnlyList<CachePresence>> ProbeAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var r = new List<CachePresence>();
            foreach (var k in keys)
                if (_data.TryGetValue(k, out var e) && e.Exp > clock.UtcNow)
                    r.Add(new CachePresence(k, e.Value.Length * 2, e.Exp));
            return Task.FromResult<IReadOnlyList<CachePresence>>(r);
        }
    }
}
