using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Identity;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class UserEntityTests(LocalDbFixture fx)
{
    [Fact]
    public async Task User_round_trips_and_normalizedEmail_is_queryable()
    {
        var id = "u_" + Guid.NewGuid().ToString("N");
        await using (var ctx = fx.NewContext())
        {
            ctx.Users.Add(new User { UserId = id, Email = "Ada@Example.com", NormalizedEmail = "ada@example.com",
                FirstSeenAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fx.NewContext())
        {
            var found = await ctx.Users.SingleAsync(u => u.NormalizedEmail == "ada@example.com" && u.UserId == id);
            Assert.Equal("Ada@Example.com", found.Email);
        }
    }
}
