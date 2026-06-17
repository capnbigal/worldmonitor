using WorldMonitor.Providers;
using Xunit;

namespace WorldMonitor.Api.Tests;

public class RegionalNewsProviderTests
{
    [Fact]
    public void RegionFeeds_are_nonempty_and_all_https()
    {
        Assert.NotEmpty(RegionalNewsProvider.RegionFeeds);
        Assert.All(RegionalNewsProvider.RegionFeeds, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Key));
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
            Assert.NotEmpty(r.Feeds);
            Assert.All(r.Feeds, f =>
            {
                Assert.False(string.IsNullOrWhiteSpace(f.Source));
                Assert.StartsWith("https://", f.Url);
            });
        });
    }

    [Fact]
    public void Region_keys_are_unique()
    {
        var keys = RegionalNewsProvider.RegionFeeds.Select(r => r.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task FetchAsync_unknown_region_returns_empty_without_calling_the_network()
    {
        // No HttpClient calls happen for an unknown region, so a throwing handler proves it short-circuits.
        using var http = new HttpClient(new ThrowingHandler());
        var provider = new RegionalNewsProvider(http);

        var result = await provider.FetchAsync("atlantis");

        Assert.Empty(result);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("network must not be called for an unknown region");
    }
}
