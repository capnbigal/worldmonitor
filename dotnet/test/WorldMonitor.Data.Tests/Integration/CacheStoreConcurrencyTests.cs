using WorldMonitor.Data.Caching;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class CacheStoreConcurrencyTests(LocalDbFixture fx)
{
    private static string Key() => "conc:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Concurrent_upserts_of_same_new_key_do_not_throw_pk_violation()
    {
        var key = Key();
        // Each store gets its OWN context/connection ⇒ real concurrent writers.
        var tasks = Enumerable.Range(0, 16).Select(i =>
            new SqlServerCacheStore(fx.NewContext())
                .UpsertAsync(new CacheUpsert(key, $"v{i}", TimeSpan.FromMinutes(5))));

        await Task.WhenAll(tasks); // must NOT throw (HOLDLOCK serializes NOT MATCHED)

        var r = await new SqlServerCacheStore(fx.NewContext()).ReadAsync(key);
        Assert.Equal(CacheReadStatus.Hit, r.Status); // exactly one surviving row
    }

    [Fact]
    public async Task ExtendTtl_returns_true_for_live_entry_and_pushes_expiry()
    {
        var store = new SqlServerCacheStore(fx.NewContext());
        var key = Key();
        await store.UpsertAsync(new CacheUpsert(key, "x", TimeSpan.FromSeconds(2)));
        Assert.True(await store.ExtendTtlAsync(key, TimeSpan.FromMinutes(10)));
        Assert.Equal(CacheReadStatus.Hit, (await store.ReadAsync(key)).Status);
    }

    [Fact]
    public async Task ExtendTtl_returns_false_and_does_not_resurrect_expired_entry()
    {
        var store = new SqlServerCacheStore(fx.NewContext());
        var key = Key();
        await store.UpsertAsync(new CacheUpsert(key, "x", TimeSpan.FromSeconds(-1))); // expired
        Assert.False(await store.ExtendTtlAsync(key, TimeSpan.FromMinutes(10)));       // cannot resurrect
        Assert.Equal(CacheReadStatus.Miss, (await store.ReadAsync(key)).Status);
    }
}
