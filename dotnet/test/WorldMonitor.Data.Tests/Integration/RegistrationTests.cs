using WorldMonitor.Data.Entities.Waitlist;
using WorldMonitor.Data.Repositories;
using WorldMonitor.Data.Time;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class RegistrationTests(LocalDbFixture fx)
{
    private RegistrationRepository Repo() => new(fx.NewContext(), new SystemClock());
    private static string Email() => Guid.NewGuid().ToString("N") + "@example.com";

    [Fact]
    public async Task First_registration_is_new_with_a_position()
    {
        var e = Email();
        var r = await Repo().RegisterAsync(e, e.ToLowerInvariant(), source: "web", appVersion: null, referralCode: null, referredBy: null);
        Assert.False(r.AlreadyRegistered);
        Assert.False(r.EmailSuppressed);
        Assert.True(r.Position >= 1);
    }

    [Fact]
    public async Task Re_registering_same_email_is_idempotent_with_same_position()
    {
        var e = Email();
        var n = e.ToLowerInvariant();
        var first = await Repo().RegisterAsync(e, n, "web", null, null, null);
        var second = await Repo().RegisterAsync(e, n, "web", null, null, null);
        Assert.True(second.AlreadyRegistered);
        Assert.Equal(first.Position, second.Position);
    }

    [Fact]
    public async Task Registration_reports_email_suppressed_when_suppressed()
    {
        var e = Email();
        var n = e.ToLowerInvariant();
        await using (var ctx = fx.NewContext())
        {
            ctx.EmailSuppressions.Add(new EmailSuppression { NormalizedEmail = n, Reason = "complaint", SuppressedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }
        var r = await Repo().RegisterAsync(e, n, "web", null, null, null);
        Assert.True(r.EmailSuppressed);
    }
}
