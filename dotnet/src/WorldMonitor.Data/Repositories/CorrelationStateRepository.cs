using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Ml;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Data.Repositories;

/// <summary>Single-row previous-cycle snapshot. GetLatestAsync returns null on cold start (the engine
/// emits nothing on the first run).</summary>
public sealed class CorrelationStateRepository(WorldMonitorDbContext db, IClock clock)
{
    public Task<CorrelationState?> GetLatestAsync(CancellationToken ct = default)
        => db.CorrelationStates.AsNoTracking().OrderByDescending(s => s.Timestamp).FirstOrDefaultAsync(ct);

    public async Task SaveAsync(string newsVelocity, string marketChanges, string predictionChanges, CancellationToken ct = default)
    {
        var existing = await db.CorrelationStates.FirstOrDefaultAsync(ct);
        if (existing is null)
            db.CorrelationStates.Add(new CorrelationState
            {
                Timestamp = clock.UtcNow, NewsVelocity = newsVelocity,
                MarketChanges = marketChanges, PredictionChanges = predictionChanges,
            });
        else
        {
            existing.Timestamp = clock.UtcNow;
            existing.NewsVelocity = newsVelocity;
            existing.MarketChanges = marketChanges;
            existing.PredictionChanges = predictionChanges;
        }
        await db.SaveChangesAsync(ct);
    }
}
