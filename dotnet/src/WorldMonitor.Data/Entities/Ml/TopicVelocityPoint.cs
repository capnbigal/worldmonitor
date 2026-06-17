namespace WorldMonitor.Data.Entities.Ml;

/// <summary>A time-series sample of a topic's mention velocity; baselined over a 7-day window.</summary>
public sealed class TopicVelocityPoint
{
    public int Id { get; set; }
    public required string Topic { get; set; }
    public DateTime Timestamp { get; set; }
    public double Velocity { get; set; }
}
