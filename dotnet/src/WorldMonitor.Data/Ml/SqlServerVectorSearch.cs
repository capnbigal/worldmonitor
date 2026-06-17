using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Ml;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Data.Ml;

/// <summary>SQL Server 2019-compatible vector store: embeddings as VARBINARY, top-K via a C# brute-force
/// cosine scan (the legacy search is itself O(n)). A SQL Server 2025 VECTOR_DISTANCE path can replace
/// SearchAsync behind IVectorSearch without touching callers.</summary>
public sealed class SqlServerVectorSearch(WorldMonitorDbContext db, IClock clock) : IVectorSearch
{
    public async Task UpsertAsync(VectorEntry entry, CancellationToken ct = default)
    {
        var existing = await db.Vectors.FindAsync([entry.Id], ct);
        if (existing is null)
        {
            if (entry.IngestedAt == default) entry.IngestedAt = clock.UtcNow;
            db.Vectors.Add(entry);
        }
        else
        {
            existing.Text = entry.Text;
            existing.Embedding = entry.Embedding;
            existing.PubDate = entry.PubDate;
            existing.Source = entry.Source;
            existing.Url = entry.Url;
            existing.Tags = entry.Tags;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(float[] query, int topK, CancellationToken ct = default)
    {
        var rows = await db.Vectors.AsNoTracking()
            .Select(v => new { v.Id, v.Text, v.Url, v.Embedding })
            .ToListAsync(ct);

        return rows
            .Select(r => new VectorHit(r.Id, r.Text, VectorMath.Cosine(query, VectorMath.ToFloats(r.Embedding)), r.Url))
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToList();
    }

    public async Task<int> PruneToFifoCapAsync(int maxVectors, CancellationToken ct = default)
    {
        // Keep the newest maxVectors by IngestedAt (tie-broken on Id); delete everything else in one DELETE.
        var keepIds = db.Vectors
            .OrderByDescending(v => v.IngestedAt).ThenByDescending(v => v.Id)
            .Take(maxVectors)
            .Select(v => v.Id);
        return await db.Vectors.Where(v => !keepIds.Contains(v.Id)).ExecuteDeleteAsync(ct);
    }
}
