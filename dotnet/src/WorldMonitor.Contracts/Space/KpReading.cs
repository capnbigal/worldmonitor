namespace WorldMonitor.Contracts.Space;

public sealed record KpReading
{
    public long At { get; init; }
    public double Kp { get; init; }
}

public sealed record ListKpResponse
{
    public IReadOnlyList<KpReading> Items { get; init; } = [];
}
