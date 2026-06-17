using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Notifications;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class NotificationChannelTests(LocalDbFixture fx)
{
    private static string U() => "u_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Each_channel_type_round_trips_via_TPH()
    {
        var u = U();
        await using (var ctx = fx.NewContext())
        {
            ctx.NotificationChannels.Add(new TelegramChannel { UserId = u, ChatId = "123", Verified = true, LinkedAt = DateTime.UtcNow });
            ctx.NotificationChannels.Add(new EmailChannel { UserId = u, Email = "a@b.com", Verified = false, LinkedAt = DateTime.UtcNow });
            ctx.NotificationChannels.Add(new WebPushChannel { UserId = u, Endpoint = "https://push/" + u, P256dh = "k", Auth = "a", Verified = true, LinkedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fx.NewContext())
        {
            Assert.Equal("123", (await ctx.NotificationChannels.OfType<TelegramChannel>().SingleAsync(c => c.UserId == u)).ChatId);
            Assert.Equal("a@b.com", (await ctx.NotificationChannels.OfType<EmailChannel>().SingleAsync(c => c.UserId == u)).Email);
            Assert.Equal(3, await ctx.NotificationChannels.CountAsync(c => c.UserId == u));
        }
    }

    [Fact]
    public async Task Duplicate_channel_type_for_user_violates_unique_index()
    {
        var u = U();
        await using var ctx = fx.NewContext();
        ctx.NotificationChannels.Add(new EmailChannel { UserId = u, Email = "x@y.com", LinkedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.NotificationChannels.Add(new EmailChannel { UserId = u, Email = "z@y.com", LinkedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
