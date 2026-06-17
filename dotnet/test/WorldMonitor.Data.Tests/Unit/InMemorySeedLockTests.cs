using WorldMonitor.Data.Locking;
using Xunit;

namespace WorldMonitor.Data.Tests.Unit;

public class InMemorySeedLockTests
{
    [Fact]
    public async Task Second_acquire_while_held_returns_null()
    {
        var sut = new InMemorySeedLock();
        await using var first = await sut.TryAcquireAsync("r");
        Assert.NotNull(first);
        Assert.Null(await sut.TryAcquireAsync("r"));
    }

    [Fact]
    public async Task Resource_can_be_reacquired_after_release()
    {
        var sut = new InMemorySeedLock();
        (await sut.TryAcquireAsync("r")).Should();           // acquire + dispose immediately
        await using var again = await sut.TryAcquireAsync("r");
        Assert.NotNull(again);
    }
}

file static class Ext
{
    // dispose the handle right away to model release-after-use
    public static void Should(this ISeedLockHandle? h) { Assert.NotNull(h); h!.DisposeAsync().AsTask().Wait(); }
}
