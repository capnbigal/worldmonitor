using WorldMonitor.Data.Repositories;
using WorldMonitor.Data.Time;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class FollowedCountryTests(LocalDbFixture fx)
{
    private static string U() => "u_" + Guid.NewGuid().ToString("N");
    private FollowedCountryRepository Repo() => new(fx.NewContext(), new WatchlistOptions(MaxPerUser: 3, PrivacyFloor: 5), new SystemClock());

    [Fact]
    public async Task Follow_is_idempotent_and_case_insensitive()
    {
        var u = U();
        Assert.Equal(FollowResult.Followed, await Repo().FollowAsync(u, "us"));
        Assert.Equal(FollowResult.AlreadyFollowing, await Repo().FollowAsync(u, "US")); // normalized, unique
        Assert.Equal(1, await Repo().CountForUserAsync(u));
    }

    [Fact]
    public async Task Cap_blocks_the_over_limit_follow()
    {
        var u = U();
        var repo = Repo();
        Assert.Equal(FollowResult.Followed, await repo.FollowAsync(u, "US"));
        Assert.Equal(FollowResult.Followed, await repo.FollowAsync(u, "GB"));
        Assert.Equal(FollowResult.Followed, await repo.FollowAsync(u, "FR"));
        Assert.Equal(FollowResult.CapReached, await repo.FollowAsync(u, "DE")); // MaxPerUser = 3
    }

    [Fact]
    public async Task Unfollow_removes_and_is_idempotent()
    {
        var u = U();
        var repo = Repo();
        await repo.FollowAsync(u, "US");
        Assert.True(await repo.UnfollowAsync(u, "us"));
        Assert.False(await repo.UnfollowAsync(u, "us"));
        Assert.Equal(0, await repo.CountForUserAsync(u));
    }

    [Fact]
    public async Task Follower_count_applies_privacy_floor()
    {
        var country = "Z" + Guid.NewGuid().ToString("N")[..1]; // unlikely to collide with seeded data
        // 4 followers — below the floor of 5 ⇒ reported as 0
        for (var i = 0; i < 4; i++) await Repo().FollowAsync(U(), country);
        Assert.Equal(0, await Repo().CountFollowersAsync(country));

        await Repo().FollowAsync(U(), country); // 5th ⇒ at/above floor
        Assert.Equal(5, await Repo().CountFollowersAsync(country));
    }
}
