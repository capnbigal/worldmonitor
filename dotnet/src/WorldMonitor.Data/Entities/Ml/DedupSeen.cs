namespace WorldMonitor.Data.Entities.Ml;

/// <summary>Records that a correlation signal was emitted, for per-signal-type dedup.</summary>
public sealed class DedupSeen
{
    public required string DedupKey { get; set; }
    public required string SignalType { get; set; }
    public DateTime SeenAt { get; set; }
}
