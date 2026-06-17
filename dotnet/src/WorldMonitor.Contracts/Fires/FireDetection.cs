namespace WorldMonitor.Contracts.Fires;

public sealed record FireDetection
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double Brightness { get; init; }
    public string? Confidence { get; init; }
    public string? AcqDate { get; init; }
    public double Frp { get; init; }
}

public sealed record ListFiresResponse
{
    public IReadOnlyList<FireDetection> Items { get; init; } = [];

    /// <summary>False when the panel's API key is not configured; the client then shows setup instructions.</summary>
    public bool Configured { get; init; } = true;
}
