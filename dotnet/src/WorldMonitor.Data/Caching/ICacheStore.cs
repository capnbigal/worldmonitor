namespace WorldMonitor.Data.Caching;

public interface ICacheStore
{
    /// <summary>Tri-state read of a single live entry. Expired/absent ⇒ Miss; store failure ⇒ Error.</summary>
    Task<CacheReadResult> ReadAsync(string key, CancellationToken ct = default);

    /// <summary>Atomic, concurrency-safe upsert (MERGE WITH HOLDLOCK). Retries transient errors;
    /// rejects payloads over <paramref name="maxBytes"/>. Permanent errors throw.</summary>
    Task UpsertAsync(CacheUpsert entry, CancellationToken ct = default);

    /// <summary>Extends a LIVE entry's TTL without touching FetchedAt (last-good preservation on
    /// upstream failure). Returns false when no live row exists (cannot resurrect an expired entry).</summary>
    Task<bool> ExtendTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Batch read of live entries (bootstrap). Expired rows are omitted.</summary>
    Task<IReadOnlyDictionary<string, string>> ReadManyAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);

    /// <summary>Batch presence/size probe (health). Expired rows are omitted.</summary>
    Task<IReadOnlyList<CachePresence>> ProbeAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);
}
