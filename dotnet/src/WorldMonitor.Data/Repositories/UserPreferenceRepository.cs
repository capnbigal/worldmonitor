using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Identity;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Data.Repositories;

/// <summary>Outcome of a compare-and-set. <see cref="SyncVersion"/> is the NEW version on success,
/// or the ACTUAL stored version on conflict.</summary>
public readonly record struct PreferenceSetResult(bool Ok, int SyncVersion)
{
    public static PreferenceSetResult Success(int newVersion) => new(true, newVersion);
    public static PreferenceSetResult Conflict(int actualVersion) => new(false, actualVersion);
}

// Domain-entity timestamps use the app-side IClock (testable); the cache store uses SYSUTCDATETIME() for freshness-as-liveness.
public sealed class UserPreferenceRepository(WorldMonitorDbContext db, IClock clock)
{
    /// <summary>Client-supplied compare-and-set. expectedSyncVersion 0 with no existing row inserts at v1.
    /// A mismatch returns Conflict(actualVersion) without throwing.</summary>
    public async Task<PreferenceSetResult> SetAsync(
        string userId, string variant, string data, int schemaVersion, int expectedSyncVersion, CancellationToken ct = default)
    {
        // Atomic conditional update — only the row whose stored version equals the client's expectation moves.
        var affected = await db.UserPreferences
            .Where(p => p.UserId == userId && p.Variant == variant && p.SyncVersion == expectedSyncVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Data, data)
                .SetProperty(p => p.SchemaVersion, schemaVersion)
                .SetProperty(p => p.UpdatedAt, clock.UtcNow)
                .SetProperty(p => p.SyncVersion, p => p.SyncVersion + 1), ct);

        if (affected == 1) return PreferenceSetResult.Success(expectedSyncVersion + 1);

        var current = await db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Variant == variant, ct);

        if (current is null && expectedSyncVersion == 0)
        {
            db.UserPreferences.Add(new UserPreference
            {
                UserId = userId, Variant = variant, Data = data,
                SchemaVersion = schemaVersion, UpdatedAt = clock.UtcNow, SyncVersion = 1,
            });
            try
            {
                await db.SaveChangesAsync(ct);
                return PreferenceSetResult.Success(1);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 }) // lost the insert race on UX_UserPreferences_User_Variant
            {
                var raced = await db.UserPreferences.AsNoTracking()
                    .FirstAsync(p => p.UserId == userId && p.Variant == variant, ct);
                return PreferenceSetResult.Conflict(raced.SyncVersion);
            }
        }

        return PreferenceSetResult.Conflict(current?.SyncVersion ?? 0);
    }
}
