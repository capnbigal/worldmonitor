using WorldMonitor.Data.Entities.Ml;
using WorldMonitor.Data.Ml;
using WorldMonitor.Data.Time;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class VectorSearchTests(LocalDbFixture fx)
{
    private SqlServerVectorSearch Search() => new(fx.NewContext(), new SystemClock());
    private static VectorEntry Entry(string id, float[] emb, DateTime ingestedAt) =>
        new() { Id = id, Text = "t-" + id, Embedding = VectorMath.ToBytes(emb), IngestedAt = ingestedAt };

    [Fact]
    public async Task Upsert_is_idempotent_by_id()
    {
        var id = "v_" + Guid.NewGuid().ToString("N");
        await Search().UpsertAsync(Entry(id, [1f, 0f, 0f], DateTime.UtcNow));
        await Search().UpsertAsync(Entry(id, [0f, 1f, 0f], DateTime.UtcNow)); // same id ⇒ replace, not duplicate

        await using var ctx = fx.NewContext();
        Assert.Equal(1, ctx.Vectors.Count(v => v.Id == id));
    }

    [Fact]
    public async Task Search_ranks_by_cosine_similarity()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var s = Search();
        await s.UpsertAsync(Entry(tag + "_near", [1f, 0.1f, 0f], DateTime.UtcNow));
        await s.UpsertAsync(Entry(tag + "_far", [0f, 0f, 1f], DateTime.UtcNow));

        var hits = await Search().SearchAsync([1f, 0f, 0f], topK: 50);
        var near = hits.Single(h => h.Id == tag + "_near");
        var far = hits.Single(h => h.Id == tag + "_far");
        Assert.True(near.Score > far.Score); // the aligned vector outranks the orthogonal one
    }

    [Fact]
    public async Task Prune_keeps_only_the_newest_N_by_ingested_at()
    {
        var prefix = "p_" + Guid.NewGuid().ToString("N");
        var s = Search();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 5; i++)
            await s.UpsertAsync(Entry($"{prefix}_{i}", [1f, 0f, 0f], t0.AddMinutes(i)));

        // Keep only the newest 3 *of this test's rows*: prune to (total - 5 + 3).
        await using var ctx = fx.NewContext();
        var total = ctx.Vectors.Count();
        await Search().PruneToFifoCapAsync(total - 2); // drop the 2 oldest overall, which are this test's _0 and _1

        await using var ctx2 = fx.NewContext();
        Assert.False(ctx2.Vectors.Any(v => v.Id == $"{prefix}_0"));
        Assert.True(ctx2.Vectors.Any(v => v.Id == $"{prefix}_4"));
    }
}
