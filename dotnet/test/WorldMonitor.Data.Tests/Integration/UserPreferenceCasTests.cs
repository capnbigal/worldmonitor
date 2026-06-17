using WorldMonitor.Data.Repositories;
using WorldMonitor.Data.Time;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class UserPreferenceCasTests(LocalDbFixture fx)
{
    private static string U() => "u_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task First_write_with_expected_zero_inserts_at_version_1()
    {
        var repo = new UserPreferenceRepository(fx.NewContext(), new SystemClock());
        var r = await repo.SetAsync(U(), "full", "{\"a\":1}", schemaVersion: 1, expectedSyncVersion: 0);
        Assert.True(r.Ok);
        Assert.Equal(1, r.SyncVersion);
    }

    [Fact]
    public async Task Matching_version_updates_and_increments()
    {
        var user = U();
        var repo = new UserPreferenceRepository(fx.NewContext(), new SystemClock());
        await repo.SetAsync(user, "full", "{\"a\":1}", 1, 0);            // → v1
        var r = await repo.SetAsync(user, "full", "{\"a\":2}", 1, 1);    // expected 1 → v2
        Assert.True(r.Ok);
        Assert.Equal(2, r.SyncVersion);
    }

    [Fact]
    public async Task Stale_expected_version_conflicts_and_reports_actual_without_throwing()
    {
        var user = U();
        var repo = new UserPreferenceRepository(fx.NewContext(), new SystemClock());
        await repo.SetAsync(user, "full", "{\"a\":1}", 1, 0);            // → v1
        var r = await repo.SetAsync(user, "full", "{\"a\":99}", 1, 0);   // expected 0 but actual is 1
        Assert.False(r.Ok);
        Assert.Equal(1, r.SyncVersion);                                  // actual reported back
    }
}
