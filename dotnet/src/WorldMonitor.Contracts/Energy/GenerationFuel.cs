namespace WorldMonitor.Contracts.Energy;

public sealed record GenerationFuel
{
    public required string Fuel { get; init; }
    public double Percent { get; init; }
}

public sealed record ListEnergyMixResponse
{
    public IReadOnlyList<GenerationFuel> Items { get; init; } = [];
}
