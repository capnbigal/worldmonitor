using System.Collections.Frozen;

namespace WorldMonitor.Contracts.Seismology;

public sealed record ListEarthquakesRequest
{
    public long Start { get; init; }
    public long End { get; init; }
    public int PageSize { get; init; }
    public string Cursor { get; init; } = "";
    public double MinMagnitude { get; init; }

    /// <summary>C# property name -> wire query-param name, from the proto (sebuf.http.query) annotations.</summary>
    public static readonly FrozenDictionary<string, string> QueryNames = new Dictionary<string, string>
    {
        [nameof(Start)]        = "start",
        [nameof(End)]          = "end",
        [nameof(PageSize)]     = "page_size",
        [nameof(Cursor)]       = "cursor",
        [nameof(MinMagnitude)] = "min_magnitude",
    }.ToFrozenDictionary();
}
