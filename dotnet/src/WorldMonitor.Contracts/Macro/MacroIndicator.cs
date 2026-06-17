namespace WorldMonitor.Contracts.Macro;

public sealed record MacroIndicator
{
    public required string Name { get; init; }
    public required string SeriesId { get; init; }
    public double Value { get; init; }
    public string? Date { get; init; }
    public string? Units { get; init; }
}

public sealed record ListMacroResponse
{
    public IReadOnlyList<MacroIndicator> Items { get; init; } = [];

    /// <summary>False when the panel's API key is not configured; the client then shows setup instructions.</summary>
    public bool Configured { get; init; } = true;
}
