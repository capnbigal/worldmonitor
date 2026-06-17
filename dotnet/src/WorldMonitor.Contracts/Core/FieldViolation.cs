namespace WorldMonitor.Contracts.Core;

public sealed record FieldViolation
{
    public string Field { get; init; } = "";
    public string Description { get; init; } = "";
}
