namespace WorldMonitor.Data.Entities;

/// <summary>One cached object — the Redis substrate replacement. Payload + freshness columns
/// live on a single row written in one transaction, eliminating the legacy dual-write
/// (value key + seed-meta key) partial-failure window.</summary>
public sealed class CacheEntry
{
    public required string CacheKey { get; set; }      // final key (prefixing is the caller's concern — see P2)
    public required string Value { get; set; }         // JSON payload or the negative sentinel (P1a-2)
    public long ByteLength { get; private set; }        // computed: CAST(DATALENGTH(Value) AS bigint)
    public DateTime ExpiresAtUtc { get; set; }          // data-liveness flag AND eviction clock
    public DateTime? FetchedAt { get; set; }            // freshness; null ⇒ treated stale
    public int? RecordCount { get; set; }
    public string? State { get; set; }                  // OK|OK_ZERO|RETRY|ERROR
    public string? SourceVersion { get; set; }
    public DateTime? NewestItemAt { get; set; }
    public int? MaxContentAgeMin { get; set; }
    public DateTime UpdatedAt { get; set; }
}
