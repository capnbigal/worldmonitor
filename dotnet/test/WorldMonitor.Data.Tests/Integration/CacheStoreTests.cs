using WorldMonitor.Data.Caching;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class CacheStoreTests(LocalDbFixture fx)
{
    private ICacheStore NewStore() => new SqlServerCacheStore(fx.NewContext());
    private static string Key() => "test:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Upsert_then_read_returns_hit_with_value()
    {
        var store = NewStore();
        var key = Key();
        await store.UpsertAsync(new CacheUpsert(key, "{\"v\":1}", TimeSpan.FromMinutes(5), RecordCount: 3));

        var r = await store.ReadAsync(key);

        Assert.Equal(CacheReadStatus.Hit, r.Status);
        Assert.Equal("{\"v\":1}", r.Value);
        Assert.Equal(3, r.RecordCount);
    }

    [Fact]
    public async Task Read_missing_key_returns_miss()
        => Assert.Equal(CacheReadStatus.Miss, (await NewStore().ReadAsync(Key())).Status);

    [Fact]
    public async Task Expired_entry_reads_as_miss()
    {
        var store = NewStore();
        var key = Key();
        await store.UpsertAsync(new CacheUpsert(key, "x", TimeSpan.FromSeconds(-1))); // already expired
        Assert.Equal(CacheReadStatus.Miss, (await store.ReadAsync(key)).Status);
    }

    [Fact]
    public async Task Upsert_overwrites_existing_value()
    {
        var store = NewStore();
        var key = Key();
        await store.UpsertAsync(new CacheUpsert(key, "a", TimeSpan.FromMinutes(5)));
        await store.UpsertAsync(new CacheUpsert(key, "b", TimeSpan.FromMinutes(5)));
        Assert.Equal("b", (await store.ReadAsync(key)).Value);
    }

    [Fact]
    public async Task Oversized_payload_is_rejected()
    {
        var store = NewStore();
        var big = new string('x', 6 * 1024 * 1024); // 6 MB > 5 MB cap
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.UpsertAsync(new CacheUpsert(Key(), big, TimeSpan.FromMinutes(1))));
    }
}
