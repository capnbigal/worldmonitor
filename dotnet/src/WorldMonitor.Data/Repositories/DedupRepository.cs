using System.Collections.Frozen;
using WorldMonitor.Data.Entities.Ml;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Data.Repositories;

// Domain-entity timestamps use the app-side IClock (testable); the cache store uses SYSUTCDATETIME() for freshness-as-liveness.
public sealed class DedupRepository(WorldMonitorDbContext db, IClock clock)
{
    private static readonly FrozenDictionary<string, TimeSpan> Ttls = new Dictionary<string, TimeSpan>
    {
        ["silent_divergence"] = TimeSpan.FromHours(6),
        ["flow_price_divergence"] = TimeSpan.FromHours(6),
        ["explained_market_move"] = TimeSpan.FromHours(6),
        ["prediction_leads_news"] = TimeSpan.FromHours(2),
        ["keyword_spike"] = TimeSpan.FromMinutes(30),
    }.ToFrozenDictionary();

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    public static TimeSpan TtlFor(string signalType) => Ttls.GetValueOrDefault(signalType, DefaultTtl);

    /// <summary>Marks a signal seen. Returns true if newly marked; false if it was seen within its TTL.</summary>
    public async Task<bool> TryMarkSeenAsync(string dedupKey, string signalType, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var existing = await db.DedupSeen.FindAsync([dedupKey], ct);
        if (existing is not null && now - existing.SeenAt < TtlFor(existing.SignalType))
            return false; // still within its TTL ⇒ duplicate

        if (existing is null)
            db.DedupSeen.Add(new DedupSeen { DedupKey = dedupKey, SignalType = signalType, SeenAt = now });
        else
        {
            existing.SignalType = signalType;
            existing.SeenAt = now;
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}
