using System.Data;
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Notifications;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Data.Repositories;

// Domain-entity timestamps use the app-side IClock (testable); the cache store uses SYSUTCDATETIME() for freshness-as-liveness.
public sealed class NotificationChannelRepository(WorldMonitorDbContext db, IClock clock)
{
    /// <summary>Registers/updates the caller's web-push subscription and TRANSFERS ownership of the
    /// endpoint away from any other user that previously held it (a browser endpoint is bound to the
    /// origin, not the account). Faithful to convex/notificationChannels.ts:77-132. Returns true if a
    /// new row was created for this user. Serializable + the filtered unique Endpoint index serialize
    /// concurrent registrations of the same endpoint.</summary>
    public async Task<bool> SetWebPushAsync(
        string userId, string endpoint, string p256dh, string auth, string? userAgent, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Step 1: ownership transfer — delete any web-push row (any user) bound to this endpoint.
        await db.Set<WebPushChannel>().Where(w => w.Endpoint == endpoint).ExecuteDeleteAsync(ct);

        // Step 2: upsert this user's single web-push row.
        var existing = await db.Set<WebPushChannel>().FirstOrDefaultAsync(w => w.UserId == userId, ct);
        var isNew = existing is null;
        if (existing is null)
        {
            db.Add(new WebPushChannel
            {
                UserId = userId, Endpoint = endpoint, P256dh = p256dh, Auth = auth,
                UserAgent = userAgent, Verified = true, LinkedAt = clock.UtcNow,
            });
        }
        else
        {
            existing.Endpoint = endpoint;
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.UserAgent = userAgent;
            existing.Verified = true;
            existing.LinkedAt = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return isNew;
    }
}
