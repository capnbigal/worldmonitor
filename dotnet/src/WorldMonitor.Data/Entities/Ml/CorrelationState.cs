namespace WorldMonitor.Data.Entities.Ml;

/// <summary>Single-row snapshot of the previous analysis cycle (for cross-run delta detection).
/// No row = cold start (the engine emits nothing on the first run).</summary>
public sealed class CorrelationState
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string NewsVelocity { get; set; } = "{}";       // JSON blob
    public string MarketChanges { get; set; } = "{}";      // JSON blob
    public string PredictionChanges { get; set; } = "{}";  // JSON blob
}
