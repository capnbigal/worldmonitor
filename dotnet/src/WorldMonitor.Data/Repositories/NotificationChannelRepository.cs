using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Notifications;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Data.Repositories;

// Domain-entity timestamps use the app-side IClock (testable); the cache store uses SYSUTCDATETIME() for freshness-as-liveness.
public sealed class NotificationChannelRepository(WorldMonitorDbContext db, IClock clock)
{
    private const int MaxAttempts = 3;

    /// <summary>Registers/updates the caller's web-push subscription and TRANSFERS ownership of the
    /// endpoint away from any other user that previously held it (a browser endpoint is bound to the
    /// origin, not the account). Faithful to convex/notificationChannels.ts:77-132. Returns true if a
    /// new row was created for this user. A serializable transaction plus the filtered unique Endpoint
    /// index serialize concurrent same-endpoint registrations; a lost race (unique violation 2627/2601
    /// or deadlock 1205) is retried up to 3 times, then propagates.</summary>
    public async Task<bool> SetWebPushAsync(
        string userId, string endpoint, string p256dh, string auth, string? userAgent, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TransferAndUpsertAsync(userId, endpoint, p256dh, auth, userAgent, ct);
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransferRace(ex))
            {
                db.ChangeTracker.Clear(); // discard the failed tracked write before retrying the transfer
            }
        }
    }

    private async Task<bool> TransferAndUpsertAsync(
        string userId, string endpoint, string p256dh, string auth, string? userAgent, CancellationToken ct)
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

    // A concurrent same-endpoint registration can surface as a unique-index violation (2627/2601) from
    // SaveChanges or a deadlock (1205) from either statement; both are resolved by retrying the transfer.
    private static bool IsTransferRace(Exception ex) => ex switch
    {
        DbUpdateException { InnerException: SqlException s } => s.Number is 2627 or 2601 or 1205,
        SqlException s => s.Number is 1205,
        _ => false,
    };
}
