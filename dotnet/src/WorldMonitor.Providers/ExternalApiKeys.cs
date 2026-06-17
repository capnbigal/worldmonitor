namespace WorldMonitor.Providers;

/// <summary>Optional, free (registration-only) third-party API keys, bound from the <c>ExternalApis</c>
/// configuration section. A blank/absent value disables the corresponding panel, which then renders setup
/// instructions instead of data — keeping the app fully functional and free out of the box.</summary>
public sealed class ExternalApiKeys
{
    public string? Fred { get; set; }
    public string? Finnhub { get; set; }
    public string? NasaFirms { get; set; }
    public string? AlphaVantage { get; set; }
    public string? OpenWeatherMap { get; set; }
}
