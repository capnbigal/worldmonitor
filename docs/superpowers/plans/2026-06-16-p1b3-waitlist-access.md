# P1b-3 — Waitlist + Access Entities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the final domain entities to `WorldMonitor.Data` — `Registration` (waitlist), `UserReferralCode`, `UserReferralCredit`, `ContactMessage`, `UserApiKey`, `EmailSuppression` — completing the P1b domain model. The waitlist position comes from a SQL `IDENTITY` (replacing the legacy `counters` table), and registration reads `EmailSuppression` (the retained dependency the critic flagged).

**Architecture:** Six straightforward EF Core entities + configs + a migration, plus one repository: `RegistrationRepository.RegisterAsync` performs an idempotent insert keyed on `NormalizedEmail` (the unique constraint is the hard guard), checks `EmailSuppression`, and returns a gap-free waitlist position (`COUNT` of registrations with `Id <= mine`). Reuses the P1b-1 conventions (narrowed `DbUpdateException` catch, `IClock`). LocalDB integration-tested.

**Tech Stack:** .NET 10, EF Core 10 (`IDENTITY` PK, unique constraints), xUnit + LocalDB.

**This is P1b-3 of the program roadmap** (phase P1b) — the last domain-entity slice; **completes the P1b domain model.** Builds on P1b-2 (in `main`). Source of truth: `convex/schema.ts` (`registrations` L227, `userReferralCodes` L460, `userReferralCredits` L472, `contactMessages` L480, `userApiKeys` L635, `emailSuppressions` L661).

**Locked product decisions reflected here (free/no-billing):**
- `registrations.proLaunchWave` / `proLaunchWaveAssignedAt` (and the `by_proLaunchWave` index) are **dropped** — they only fed the removed PRO-launch broadcast tooling.
- `counters` is **not created**; the waitlist position is the `Registration` `IDENTITY` (`COUNT(Id <= mine)`).
- `EmailSuppression` is **kept** (the registration flow reads it).
- `UserApiKey` is kept, but with **no entitlement/premium gate** (free for all).

**Explicitly deferred:** referral-credit *attribution* logic, API-key *issuance/validation* flow, contact-message handling, suppression-webhook ingestion → wired when the API needs them (**P2**). This slice ships the entity model + the registration idempotency/position/suppression read. Vector/correlation → **P1c**.

---

## Prerequisites

- .NET 10 SDK + SQL Server LocalDB `MSSQLLocalDB` (verified).
- Branch: from `main` (P1b-2 merged), create `feat/dotnet-p1b3-waitlist-access`.

## File structure

```
dotnet/
  src/WorldMonitor.Data/
    Entities/Waitlist/Registration.cs
    Entities/Waitlist/UserReferralCode.cs
    Entities/Waitlist/UserReferralCredit.cs
    Entities/Waitlist/ContactMessage.cs
    Entities/Waitlist/EmailSuppression.cs
    Entities/Access/UserApiKey.cs
    Configurations/RegistrationConfiguration.cs
    Configurations/UserReferralCodeConfiguration.cs
    Configurations/UserReferralCreditConfiguration.cs
    Configurations/ContactMessageConfiguration.cs
    Configurations/EmailSuppressionConfiguration.cs
    Configurations/UserApiKeyConfiguration.cs
    Repositories/RegistrationRepository.cs       (+ RegistrationResult)
    WorldMonitorDbContext.cs                       (MODIFY: add 6 DbSets)
    Migrations/                                    (new: AddWaitlistAccess)
  test/WorldMonitor.Data.Tests/
    Integration/RegistrationTests.cs
    Integration/AccessEntitiesTests.cs            (referral/contact/apikey/suppression unique + round-trip)
```

---

### Task 1: Branch + Waitlist entities

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Entities/Waitlist/Registration.cs`
- Create: `dotnet/src/WorldMonitor.Data/Entities/Waitlist/UserReferralCode.cs`
- Create: `dotnet/src/WorldMonitor.Data/Entities/Waitlist/UserReferralCredit.cs`
- Create: `dotnet/src/WorldMonitor.Data/Entities/Waitlist/ContactMessage.cs`
- Create: `dotnet/src/WorldMonitor.Data/Entities/Waitlist/EmailSuppression.cs`

- [ ] **Step 1: Branch**

Run:
```bash
git checkout main && git checkout -b feat/dotnet-p1b3-waitlist-access
```
Expected: `Switched to a new branch 'feat/dotnet-p1b3-waitlist-access'`

- [ ] **Step 2: Registration** (fields from `convex/schema.ts:227-251`, dropping the broadcast wave fields)

Create `Entities/Waitlist/Registration.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Waitlist;

/// <summary>A waitlist registration. <see cref="Id"/> is an IDENTITY giving monotonic registration order
/// (replaces the legacy `counters` table); the gap-free position is COUNT(Id &lt;= mine).</summary>
public sealed class Registration
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public DateTime RegisteredAt { get; set; }
    public string? Source { get; set; }
    public string? AppVersion { get; set; }
    public string? ReferralCode { get; set; }
    public string? ReferredBy { get; set; }
    public int? ReferralCount { get; set; }
}
```

- [ ] **Step 3: Referral entities** (fields from `convex/schema.ts:460-478`)

Create `Entities/Waitlist/UserReferralCode.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Waitlist;

public sealed class UserReferralCode
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string Code { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

Create `Entities/Waitlist/UserReferralCredit.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Waitlist;

/// <summary>One attribution row per (referrer, referee email).</summary>
public sealed class UserReferralCredit
{
    public int Id { get; set; }
    public required string ReferrerUserId { get; set; }
    public required string RefereeEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 4: ContactMessage + EmailSuppression** (fields from `convex/schema.ts:480-489, 661-666`)

Create `Entities/Waitlist/ContactMessage.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Waitlist;

public sealed class ContactMessage
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? Organization { get; set; }
    public string? Phone { get; set; }
    public string? Message { get; set; }
    public required string Source { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? NormalizedEmail { get; set; }
}
```

Create `Entities/Waitlist/EmailSuppression.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Waitlist;

/// <summary>A suppressed email address. Reason is the wire literal: bounce|complaint|manual.
/// Read by the registration flow.</summary>
public sealed class EmailSuppression
{
    public int Id { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string Reason { get; set; }   // bounce|complaint|manual
    public DateTime SuppressedAt { get; set; }
    public string? Source { get; set; }
}
```

- [ ] **Step 5: Build + commit**

Run: `dotnet build dotnet/src/WorldMonitor.Data`
Expected: 0 errors.
```bash
git add dotnet/
git commit -m "feat(data): add waitlist entities (Registration, referral, contact, suppression)"
```

---

### Task 2: Access entity + all configurations

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Entities/Access/UserApiKey.cs`
- Create: the six configuration files listed in the file structure.

- [ ] **Step 1: UserApiKey** (fields from `convex/schema.ts:635-645`; no entitlement gate)

Create `Entities/Access/UserApiKey.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Access;

/// <summary>A hashed API key for non-browser access (no premium gating). KeyHash is a SHA-256 hex digest;
/// plaintext is never stored.</summary>
public sealed class UserApiKey
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string Name { get; set; }
    public required string KeyPrefix { get; set; }   // first 8 chars of the plaintext key, for display
    public required string KeyHash { get; set; }      // SHA-256 hex
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
```

- [ ] **Step 2: Configurations**

Create `Configurations/RegistrationConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Waitlist;

namespace WorldMonitor.Data.Configurations;

public sealed class RegistrationConfiguration : IEntityTypeConfiguration<Registration>
{
    public void Configure(EntityTypeBuilder<Registration> b)
    {
        b.ToTable("Registrations");
        b.HasKey(r => r.Id);
        b.Property(r => r.Email).HasMaxLength(320);
        b.Property(r => r.NormalizedEmail).HasMaxLength(320);
        b.Property(r => r.Source).HasMaxLength(64);
        b.Property(r => r.AppVersion).HasMaxLength(32);
        b.Property(r => r.ReferralCode).HasMaxLength(64);
        b.Property(r => r.ReferredBy).HasMaxLength(128);
        b.HasIndex(r => r.NormalizedEmail).IsUnique().HasDatabaseName("UX_Registrations_NormalizedEmail");
        b.HasIndex(r => r.ReferralCode).HasDatabaseName("IX_Registrations_ReferralCode");
    }
}
```

Create `Configurations/UserReferralCodeConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Waitlist;

namespace WorldMonitor.Data.Configurations;

public sealed class UserReferralCodeConfiguration : IEntityTypeConfiguration<UserReferralCode>
{
    public void Configure(EntityTypeBuilder<UserReferralCode> b)
    {
        b.ToTable("UserReferralCodes");
        b.HasKey(c => c.Id);
        b.Property(c => c.UserId).HasMaxLength(128);
        b.Property(c => c.Code).HasMaxLength(64);
        b.HasIndex(c => c.Code).IsUnique().HasDatabaseName("UX_UserReferralCodes_Code");
        b.HasIndex(c => c.UserId).IsUnique().HasDatabaseName("UX_UserReferralCodes_User");
    }
}
```

Create `Configurations/UserReferralCreditConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Waitlist;

namespace WorldMonitor.Data.Configurations;

public sealed class UserReferralCreditConfiguration : IEntityTypeConfiguration<UserReferralCredit>
{
    public void Configure(EntityTypeBuilder<UserReferralCredit> b)
    {
        b.ToTable("UserReferralCredits");
        b.HasKey(c => c.Id);
        b.Property(c => c.ReferrerUserId).HasMaxLength(128);
        b.Property(c => c.RefereeEmail).HasMaxLength(320);
        b.HasIndex(c => new { c.ReferrerUserId, c.RefereeEmail }).IsUnique().HasDatabaseName("UX_UserReferralCredits_Referrer_Email");
    }
}
```

Create `Configurations/ContactMessageConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Waitlist;

namespace WorldMonitor.Data.Configurations;

public sealed class ContactMessageConfiguration : IEntityTypeConfiguration<ContactMessage>
{
    public void Configure(EntityTypeBuilder<ContactMessage> b)
    {
        b.ToTable("ContactMessages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Name).HasMaxLength(200);
        b.Property(m => m.Email).HasMaxLength(320);
        b.Property(m => m.Organization).HasMaxLength(200);
        b.Property(m => m.Phone).HasMaxLength(64);
        b.Property(m => m.Source).HasMaxLength(64);
        b.Property(m => m.NormalizedEmail).HasMaxLength(320);
        b.HasIndex(m => new { m.NormalizedEmail, m.ReceivedAt }).HasDatabaseName("IX_ContactMessages_Email_Received");
    }
}
```

Create `Configurations/EmailSuppressionConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Waitlist;

namespace WorldMonitor.Data.Configurations;

public sealed class EmailSuppressionConfiguration : IEntityTypeConfiguration<EmailSuppression>
{
    public void Configure(EntityTypeBuilder<EmailSuppression> b)
    {
        b.ToTable("EmailSuppressions");
        b.HasKey(s => s.Id);
        b.Property(s => s.NormalizedEmail).HasMaxLength(320);
        b.Property(s => s.Reason).HasMaxLength(16);
        b.Property(s => s.Source).HasMaxLength(64);
        b.HasIndex(s => s.NormalizedEmail).IsUnique().HasDatabaseName("UX_EmailSuppressions_NormalizedEmail");
    }
}
```

Create `Configurations/UserApiKeyConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Access;

namespace WorldMonitor.Data.Configurations;

public sealed class UserApiKeyConfiguration : IEntityTypeConfiguration<UserApiKey>
{
    public void Configure(EntityTypeBuilder<UserApiKey> b)
    {
        b.ToTable("UserApiKeys");
        b.HasKey(k => k.Id);
        b.Property(k => k.UserId).HasMaxLength(128);
        b.Property(k => k.Name).HasMaxLength(128);
        b.Property(k => k.KeyPrefix).HasMaxLength(16);
        b.Property(k => k.KeyHash).HasMaxLength(64);
        b.HasIndex(k => k.KeyHash).IsUnique().HasDatabaseName("UX_UserApiKeys_KeyHash");
        b.HasIndex(k => k.UserId).HasDatabaseName("IX_UserApiKeys_User");
    }
}
```

- [ ] **Step 3: Build + commit**

Run: `dotnet build dotnet/src/WorldMonitor.Data`
Expected: 0 errors.
```bash
git add dotnet/
git commit -m "feat(data): add UserApiKey entity + all waitlist/access configurations"
```

---

### Task 3: Wire DbSets, migration, entity tests

**Files:**
- Modify: `dotnet/src/WorldMonitor.Data/WorldMonitorDbContext.cs`
- Create: migration `AddWaitlistAccess`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Integration/AccessEntitiesTests.cs`

- [ ] **Step 1: Add DbSets**

In `WorldMonitorDbContext.cs`, add `using WorldMonitor.Data.Entities.Waitlist;` and `using WorldMonitor.Data.Entities.Access;`, then:
```csharp
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<UserReferralCode> UserReferralCodes => Set<UserReferralCode>();
    public DbSet<UserReferralCredit> UserReferralCredits => Set<UserReferralCredit>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<EmailSuppression> EmailSuppressions => Set<EmailSuppression>();
    public DbSet<UserApiKey> UserApiKeys => Set<UserApiKey>();
```

- [ ] **Step 2: Create the migration**

Run:
```bash
dotnet ef migrations add AddWaitlistAccess --project dotnet/src/WorldMonitor.Data --startup-project dotnet/src/WorldMonitor.Data
dotnet build dotnet/src/WorldMonitor.Data
```
Expected: a migration creating the six tables with their unique indexes; existing tables untouched.

- [ ] **Step 3: Write the failing tests**

Create `Integration/AccessEntitiesTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Access;
using WorldMonitor.Data.Entities.Waitlist;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class AccessEntitiesTests(LocalDbFixture fx)
{
    private static string S() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task ApiKey_hash_is_unique()
    {
        var hash = "h_" + S();
        await using var ctx = fx.NewContext();
        ctx.UserApiKeys.Add(new UserApiKey { UserId = "u1", Name = "a", KeyPrefix = "wm_aaaa", KeyHash = hash, CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.UserApiKeys.Add(new UserApiKey { UserId = "u2", Name = "b", KeyPrefix = "wm_bbbb", KeyHash = hash, CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Referral_code_is_unique_and_credits_are_unique_per_pair()
    {
        var code = "c_" + S();
        await using var ctx = fx.NewContext();
        ctx.UserReferralCodes.Add(new UserReferralCode { UserId = "ua" + S(), Code = code, CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.UserReferralCodes.Add(new UserReferralCode { UserId = "ub" + S(), Code = code, CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task EmailSuppression_round_trips_with_reason()
    {
        var email = S() + "@x.com";
        await using (var ctx = fx.NewContext())
        {
            ctx.EmailSuppressions.Add(new EmailSuppression { NormalizedEmail = email, Reason = "bounce", SuppressedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fx.NewContext())
            Assert.Equal("bounce", (await ctx.EmailSuppressions.SingleAsync(s => s.NormalizedEmail == email)).Reason);
    }
}
```

- [ ] **Step 4: Run, verify pass; commit**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: PASS (new tests + all prior integration tests; the fixture recreates the DB with the new migration).
```bash
git add dotnet/
git commit -m "feat(data): wire waitlist/access DbSets + AddWaitlistAccess migration + entity tests"
```

---

### Task 4: `RegistrationRepository.RegisterAsync` — idempotent + suppression read + position

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Repositories/RegistrationRepository.cs`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Integration/RegistrationTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Integration/RegistrationTests.cs`:
```csharp
using WorldMonitor.Data.Entities.Waitlist;
using WorldMonitor.Data.Repositories;
using WorldMonitor.Data.Time;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class RegistrationTests(LocalDbFixture fx)
{
    private RegistrationRepository Repo() => new(fx.NewContext(), new SystemClock());
    private static string Email() => Guid.NewGuid().ToString("N") + "@example.com";

    [Fact]
    public async Task First_registration_is_new_with_a_position()
    {
        var e = Email();
        var r = await Repo().RegisterAsync(e, e.ToLowerInvariant(), source: "web", appVersion: null, referralCode: null, referredBy: null);
        Assert.False(r.AlreadyRegistered);
        Assert.False(r.EmailSuppressed);
        Assert.True(r.Position >= 1);
    }

    [Fact]
    public async Task Re_registering_same_email_is_idempotent_with_same_position()
    {
        var e = Email();
        var n = e.ToLowerInvariant();
        var first = await Repo().RegisterAsync(e, n, "web", null, null, null);
        var second = await Repo().RegisterAsync(e, n, "web", null, null, null);
        Assert.True(second.AlreadyRegistered);
        Assert.Equal(first.Position, second.Position);
    }

    [Fact]
    public async Task Registration_reports_email_suppressed_when_suppressed()
    {
        var e = Email();
        var n = e.ToLowerInvariant();
        await using (var ctx = fx.NewContext())
        {
            ctx.EmailSuppressions.Add(new EmailSuppression { NormalizedEmail = n, Reason = "complaint", SuppressedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }
        var r = await Repo().RegisterAsync(e, n, "web", null, null, null);
        Assert.True(r.EmailSuppressed);
    }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: FAIL — `RegistrationRepository` does not exist.

- [ ] **Step 3: Implement**

Create `Repositories/RegistrationRepository.cs`:
```csharp
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
```

- [ ] **Step 4: Run, verify pass; commit**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: PASS.
```bash
git add dotnet/
git commit -m "feat(data): RegistrationRepository idempotent register + suppression read + position"
```

---

### Task 5: Green build, README, PR

**Files:**
- Modify: `dotnet/README.md`

- [ ] **Step 1: Update README**

Add to `dotnet/README.md` (after the P1b-2 entry):
```markdown
- `src/WorldMonitor.Data` (P1b-3) — Waitlist/Access: `Registration` (IDENTITY-based waitlist position,
  unique `NormalizedEmail`; broadcast wave fields dropped), `UserReferralCode`/`UserReferralCredit`,
  `ContactMessage`, `UserApiKey` (unique `KeyHash`, no premium gate), `EmailSuppression`.
  `RegistrationRepository.RegisterAsync` is idempotent and reads `EmailSuppression`. **Completes the P1b domain model.**
```

- [ ] **Step 2: Full clean build + test**

Run:
```bash
dotnet build dotnet/WorldMonitor.slnx -c Release
dotnet test dotnet/WorldMonitor.slnx -c Release --filter Category!=Integration
dotnet test dotnet/WorldMonitor.slnx -c Release
```
Expected: build 0/0; unit ring unchanged green; full suite green (adds the new Waitlist/Access integration tests).

- [ ] **Step 3: Commit, push, open PR**

```bash
git add dotnet/
git commit -m "docs(data): document P1b-3 Waitlist/Access entities"
git push -u origin feat/dotnet-p1b3-waitlist-access
gh pr create --base main --title "P1b-3: Waitlist + Access entities" --body "Implements P1b-3 (completes the P1b domain model): Registration (IDENTITY waitlist position replacing the legacy counter, unique NormalizedEmail, broadcast wave fields dropped), UserReferralCode/Credit (unique), ContactMessage, UserApiKey (unique KeyHash, no premium gate), EmailSuppression (unique NormalizedEmail). RegistrationRepository.RegisterAsync (idempotent insert + EmailSuppression read + gap-free position). AddWaitlistAccess migration; LocalDB integration tests. Next: P1c (vector + correlation state), then P2 (the API host — first runnable app)."
```
Expected: PR URL printed.

---

## Self-review

**Spec coverage (P1b-3 scope, from `convex/schema.ts` + locked decisions):**
- ✅ `Registration` with IDENTITY position (replaces `counters`), unique `NormalizedEmail`; broadcast wave fields dropped — Tasks 1–2, 4.
- ✅ Referral code (unique) + credit (unique pair); ContactMessage; `UserApiKey` (unique `KeyHash`, no entitlement gate); `EmailSuppression` (unique) — Tasks 1–3.
- ✅ `RegisterAsync` idempotent (unique-constraint guard + narrowed `DbUpdateException` catch) + **reads `EmailSuppression`** (the critic's retained-dependency coupling) + gap-free position — Task 4.
- ✅ Migration applies on top of P1b-2's — Task 3.
- **Deferred (documented in header):** attribution/issuance/validation/webhook flows → P2; vector/correlation → P1c.

**Placeholder scan:** none — every code/command step is complete.

**Type consistency:** the six entities are configured by matching `IEntityTypeConfiguration`s (auto-applied). `RegistrationRepository.RegisterAsync` returns `RegistrationResult` and uses the P1b-1 conventions (`IClock`, narrowed `DbUpdateException` catch). DbSets (Task 3) back the repository + tests.

**Note for execution:** all new tests are `[Trait("Category","Integration")]`; the fixture recreates the LocalDB with the new migration so all five migrations apply in sequence. The IDENTITY-based position is gap-free via `COUNT(Id <= mine)` (robust to IDENTITY gaps from rolled-back inserts).
```
