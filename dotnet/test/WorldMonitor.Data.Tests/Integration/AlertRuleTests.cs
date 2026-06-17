using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Notifications;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class AlertRuleTests(LocalDbFixture fx)
{
    private static string U() => "u_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task AlertRule_round_trips_json_arrays_and_enum_strings()
    {
        var u = U();
        await using (var ctx = fx.NewContext())
        {
            ctx.AlertRules.Add(new AlertRule
            {
                UserId = u, Variant = "full", Enabled = true,
                EventTypes = ["earthquake", "cyber"], Sensitivity = "high",
                Channels = ["telegram", "web_push"], Countries = ["US", "GB"],
                DigestMode = "twice_daily", AiDigestEnabled = true, UpdatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fx.NewContext())
        {
            var r = await ctx.AlertRules.SingleAsync(x => x.UserId == u && x.Variant == "full");
            Assert.Equal(["earthquake", "cyber"], r.EventTypes);
            Assert.Equal("high", r.Sensitivity);
            Assert.Equal("twice_daily", r.DigestMode);
            Assert.Equal(["US", "GB"], r.Countries);
        }
    }

    [Fact]
    public async Task Duplicate_user_variant_violates_unique_index()
    {
        var u = U();
        await using var ctx = fx.NewContext();
        ctx.AlertRules.Add(new AlertRule { UserId = u, Variant = "full", UpdatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.AlertRules.Add(new AlertRule { UserId = u, Variant = "full", UpdatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
