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
        var first = await sut.TryAcquireAsync("r");
        Assert.NotNull(first);
        await first!.DisposeAsync();
        await using var again = await sut.TryAcquireAsync("r");
        Assert.NotNull(again);
    }
}
