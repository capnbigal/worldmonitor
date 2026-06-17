using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Notifications;
using WorldMonitor.Data.Repositories;
using WorldMonitor.Data.Time;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class WebPushOwnershipTests(LocalDbFixture fx)
{
    private NotificationChannelRepository Repo() => new(fx.NewContext(), new SystemClock());
    private static string U() => "u_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Re_registering_same_endpoint_transfers_ownership_to_new_user()
    {
        var endpoint = "https://push.example/" + Guid.NewGuid().ToString("N");
        var userA = U();
        var userB = U();

        Assert.True(await Repo().SetWebPushAsync(userA, endpoint, "kA", "aA", "Chrome"));   // isNew
        Assert.True(await Repo().SetWebPushAsync(userB, endpoint, "kB", "aB", "Firefox"));  // transfer ⇒ isNew for B

        await using var ctx = fx.NewContext();
        // The endpoint now belongs to exactly one row, owned by user B.
        var rows = await ctx.NotificationChannels.OfType<WebPushChannel>().Where(w => w.Endpoint == endpoint).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(userB, rows[0].UserId);
        // User A no longer has a web-push row.
        Assert.Empty(await ctx.NotificationChannels.OfType<WebPushChannel>().Where(w => w.UserId == userA).ToListAsync());
    }

    [Fact]
    public async Task Same_user_re_register_updates_in_place_not_duplicates()
    {
        var u = U();
        var ep1 = "https://push/" + Guid.NewGuid().ToString("N");
        var ep2 = "https://push/" + Guid.NewGuid().ToString("N");
        Assert.True(await Repo().SetWebPushAsync(u, ep1, "k1", "a1", null));   // isNew
        Assert.False(await Repo().SetWebPushAsync(u, ep2, "k2", "a2", null));  // existing user ⇒ not new

        await using var ctx = fx.NewContext();
        var rows = await ctx.NotificationChannels.OfType<WebPushChannel>().Where(w => w.UserId == u).ToListAsync();
        Assert.Single(rows);                 // one row per user (UX_NotificationChannels_User_Channel)
        Assert.Equal(ep2, rows[0].Endpoint); // updated to the latest endpoint
    }
}
