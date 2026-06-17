namespace WorldMonitor.Data.Entities.Ml;

/// <summary>Per-(domain, cluster) state overwritten each run; supports trend matching (±5 score delta).</summary>
public sealed class CorrelationClusterState
{
    public int Id { get; set; }
    public required string Domain { get; set; }
    public required string ClusterKey { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Country { get; set; }
    public string? EntityKey { get; set; }
    public double Score { get; set; }
    public DateTime Timestamp { get; set; }
}
