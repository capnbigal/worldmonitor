using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Ml;

public sealed record VectorHit(string Id, string Text, double Score, string? Url);

public interface IVectorSearch
{
    /// <summary>Idempotent upsert keyed on the content-hash Id (re-ingest replaces in place).</summary>
    Task UpsertAsync(VectorEntry entry, CancellationToken ct = default);

    /// <summary>Top-K most cosine-similar entries to the query embedding.</summary>
    Task<IReadOnlyList<VectorHit>> SearchAsync(float[] query, int topK, CancellationToken ct = default);

    /// <summary>FIFO retention: keep the newest <paramref name="maxVectors"/> by IngestedAt, delete the rest.
    /// Returns the number deleted.</summary>
    Task<int> PruneToFifoCapAsync(int maxVectors, CancellationToken ct = default);
}
