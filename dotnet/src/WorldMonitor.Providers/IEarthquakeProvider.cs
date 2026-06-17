using WorldMonitor.Contracts.Seismology;

namespace WorldMonitor.Providers;

/// <summary>Source of recent earthquake data (USGS in production; fakeable in tests).</summary>
public interface IEarthquakeProvider
{
    Task<IReadOnlyList<Earthquake>> FetchAsync(CancellationToken ct = default);
}
