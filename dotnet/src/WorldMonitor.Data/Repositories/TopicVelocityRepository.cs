using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Ml;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Data.Repositories;

public sealed class TopicVelocityRepository(WorldMonitorDbContext db, IClock clock)
{
    public async Task AddAsync(string topic, double velocity, CancellationToken ct = default)
    {
        db.TopicVelocityPoints.Add(new TopicVelocityPoint { Topic = topic, Timestamp = clock.UtcNow, Velocity = velocity });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Average velocity over the trailing 7 days, or null when the topic has no points in window.</summary>
    public async Task<double?> SevenDayBaselineAsync(string topic, CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow - TimeSpan.FromDays(7);
        var window = db.TopicVelocityPoints.Where(p => p.Topic == topic && p.Timestamp >= cutoff);
        return await window.AnyAsync(ct) ? await window.AverageAsync(p => p.Velocity, ct) : null;
    }
}
