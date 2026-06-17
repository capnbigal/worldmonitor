namespace WorldMonitor.Data.Caching;

/// <summary>Input for an atomic upsert. TTL is server-relative (ExpiresAtUtc computed as
/// SYSUTCDATETIME()+Ttl) to avoid app/DB clock skew. FetchedAt defaults to server-now when null.</summary>
public sealed record CacheUpsert(
    string Key,
    string Value,
    TimeSpan Ttl,
    DateTime? FetchedAt = null,
    int? RecordCount = null,
    string? State = null,
    string? SourceVersion = null,
    DateTime? NewestItemAt = null,
    int? MaxContentAgeMin = null);
