using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Caching;
using WorldMonitor.Data.Locking;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class SeedLockTests(LocalDbFixture fx)
{
    private static string Res() => "lock:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Second_acquire_while_held_returns_null_then_succeeds_after_release()
    {
        var sut = new SqlServerSeedLock(fx.ConnectionString);
        var res = Res();
        var first = await sut.TryAcquireAsync(res);
        Assert.NotNull(first);
        Assert.Null(await sut.TryAcquireAsync(res));   // contention ⇒ skip
        await first!.DisposeAsync();
        await using var again = await sut.TryAcquireAsync(res);
        Assert.NotNull(again);                          // reacquire after release
    }

    [Fact]
    public async Task Read_against_unreachable_server_returns_Error_not_Miss()
    {
        // A bogus instance name forces a real SqlException on open ⇒ tri-state Error.
        var ctx = new WorldMonitorDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<WorldMonitorDbContext>()
                .UseSqlServer(@"Server=(localdb)\WM_DoesNotExist;Database=x;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=2")
                .Options);
        var store = new SqlServerCacheStore(ctx);
        Assert.Equal(CacheReadStatus.Error, (await store.ReadAsync("anything")).Status);
    }
}
