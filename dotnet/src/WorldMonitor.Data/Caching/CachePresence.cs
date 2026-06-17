namespace WorldMonitor.Data.Caching;

/// <summary>Batch presence/size probe (the STRLEN analog) — lets health check size without loading blobs.</summary>
public sealed record CachePresence(string Key, long ByteLength, DateTime ExpiresAtUtc);
