using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Access;
using WorldMonitor.Data.Entities.Waitlist;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class AccessEntitiesTests(LocalDbFixture fx)
{
    private static string S() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task ApiKey_hash_is_unique()
    {
        var hash = "h_" + S();
        await using var ctx = fx.NewContext();
        ctx.UserApiKeys.Add(new UserApiKey { UserId = "u1", Name = "a", KeyPrefix = "wm_aaaa", KeyHash = hash, CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.UserApiKeys.Add(new UserApiKey { UserId = "u2", Name = "b", KeyPrefix = "wm_bbbb", KeyHash = hash, CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Referral_code_is_unique()
    {
        var code = "c_" + S();
        await using var ctx = fx.NewContext();
        ctx.UserReferralCodes.Add(new UserReferralCode { UserId = "ua" + S(), Code = code, CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.UserReferralCodes.Add(new UserReferralCode { UserId = "ub" + S(), Code = code, CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Referral_credit_is_unique_per_referrer_and_referee()
    {
        var referrer = "ref_" + S();
        var refereeEmail = S() + "@x.com";
        await using var ctx = fx.NewContext();
        ctx.UserReferralCredits.Add(new UserReferralCredit { ReferrerUserId = referrer, RefereeEmail = refereeEmail, CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.UserReferralCredits.Add(new UserReferralCredit { ReferrerUserId = referrer, RefereeEmail = refereeEmail, CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task EmailSuppression_round_trips_with_reason()
    {
        var email = S() + "@x.com";
        await using (var ctx = fx.NewContext())
        {
            ctx.EmailSuppressions.Add(new EmailSuppression { NormalizedEmail = email, Reason = "bounce", SuppressedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fx.NewContext())
            Assert.Equal("bounce", (await ctx.EmailSuppressions.SingleAsync(s => s.NormalizedEmail == email)).Reason);
    }
}
