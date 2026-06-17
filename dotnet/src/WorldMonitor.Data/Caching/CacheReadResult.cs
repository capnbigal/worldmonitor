namespace WorldMonitor.Data.Caching;

public enum CacheReadStatus
{
    Hit,    // live row found
    Miss,   // no live row (absent or expired) — caller runs the factory
    Error,  // store failure (SqlException) — caller serves last-good (fail-safe), NEVER treated as Miss
}

public sealed record CacheReadResult(
    CacheReadStatus Status,
    string? Value,
    DateTime? ExpiresAtUtc,
    DateTime? FetchedAt,
    int? RecordCount)
{
    public static readonly CacheReadResult Miss = new(CacheReadStatus.Miss, null, null, null, null);
    public static CacheReadResult Error { get; } = new(CacheReadStatus.Error, null, null, null, null);
    public static CacheReadResult Hit(string value, DateTime exp, DateTime? fetched, int? rc)
        => new(CacheReadStatus.Hit, value, exp, fetched, rc);
}
