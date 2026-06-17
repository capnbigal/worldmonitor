using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Waitlist;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Data.Repositories;

/// <summary>Result of a waitlist registration. <see cref="Position"/> is the gap-free 1-based waitlist
/// position; <see cref="AlreadyRegistered"/> is true when this email was already on the list.</summary>
public readonly record struct RegistrationResult(int Position, bool AlreadyRegistered, bool EmailSuppressed);

// Domain-entity timestamps use the app-side IClock (testable); the cache store uses SYSUTCDATETIME() for freshness-as-liveness.
public sealed class RegistrationRepository(WorldMonitorDbContext db, IClock clock)
{
    /// <summary>Idempotently registers an email (unique on NormalizedEmail) and returns its waitlist
    /// position. Reads EmailSuppression for the suppressed flag (retained dependency of the waitlist flow).</summary>
    public async Task<RegistrationResult> RegisterAsync(
        string email, string normalizedEmail, string? source, string? appVersion,
        string? referralCode, string? referredBy, CancellationToken ct = default)
    {
        var suppressed = await db.EmailSuppressions.AnyAsync(s => s.NormalizedEmail == normalizedEmail, ct);

        var existing = await db.Registrations.AsNoTracking().FirstOrDefaultAsync(r => r.NormalizedEmail == normalizedEmail, ct);
        if (existing is not null)
            return new RegistrationResult(await PositionAsync(existing.Id, ct), true, suppressed);

        var reg = new Registration
        {
            Email = email, NormalizedEmail = normalizedEmail, RegisteredAt = clock.UtcNow,
            Source = source, AppVersion = appVersion, ReferralCode = referralCode, ReferredBy = referredBy,
        };
        db.Registrations.Add(reg);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            // Concurrent duplicate lost the race on UX_Registrations_NormalizedEmail.
            var raced = await db.Registrations.AsNoTracking().FirstAsync(r => r.NormalizedEmail == normalizedEmail, ct);
            return new RegistrationResult(await PositionAsync(raced.Id, ct), true, suppressed);
        }
        return new RegistrationResult(await PositionAsync(reg.Id, ct), false, suppressed);
    }

    // Gap-free 1-based position: how many registrations have an Id at or before this one.
    private Task<int> PositionAsync(int id, CancellationToken ct)
        => db.Registrations.CountAsync(r => r.Id <= id, ct);
}
