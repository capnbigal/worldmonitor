# P1b-1 — Identity + Watchlist Entities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the first domain entities to `WorldMonitor.Data` — `User`, `UserPreference`, and `FollowedCountry` — with the two hard-won concurrency behaviors faithfully ported: the `UserPreferences` **client-supplied-version compare-and-set** (not EF optimistic concurrency) and the `FollowedCountry` **`UNIQUE(UserId,Country)`** that retires three Convex OCC scaffolding tables. Follower counts come from a single `COUNT(*)` helper that applies the privacy floor in one place.

**Architecture:** Three EF Core entities + `IEntityTypeConfiguration` each, added to the existing `WorldMonitorDbContext` (which gains three `DbSet`s alongside `CacheEntries`), plus a new migration. Two thin repositories encapsulate the non-trivial write logic: `UserPreferenceRepository.SetAsync` performs an atomic conditional update keyed on the client's `expectedSyncVersion` (returns a conflict + the actual version instead of throwing), and `FollowedCountryRepository` does idempotent follow (the unique constraint is the hard guard against duplicates), a best-effort per-user cap, unfollow, and a floor-applying follower count. Everything is integration-tested against SQL Server LocalDB.

**Tech Stack:** .NET 10, EF Core 10 (`ExecuteUpdateAsync` for the CAS), xUnit + LocalDB.

**This is P1b-1 of the program roadmap** (phase P1b). Builds on P1a (stacks on `feat/dotnet-p1a2-cache-wrapper`). Source of truth: `convex/schema.ts` (`users` L587, `userPreferences` L24, `followedCountries` L134).

**Locked product decisions reflected here:** free/no-billing/no-generative-AI. **Dropped** (NOT created): the broadcast/email cluster, MCP tokens, billing/subscription/entitlement tables, `counters` (waitlist position → SQL `IDENTITY`, handled in P1b-3), `LlmAssessmentCache`. The `users.localePrimary` index is dropped (it only fed the now-removed broadcast filter).

**Explicitly deferred (so reviewers don't flag as gaps):**
- **Notifications** (NotificationChannel TPH, AlertRule, TelegramPairingToken + the web-push cross-user guard) → **P1b-2**.
- **Waitlist/Access** (Registration with IDENTITY, referral tables, ContactMessage, emailSuppressions, UserApiKey) → **P1b-3**.
- **Vector + correlation state** → **P1c**.
- The `clerkUserId`-linking / auth principal source → **P3 (auth)**. Here `UserId` is just a string key.
- DI registration of the repositories → P2/P3 host wiring.

---

## Prerequisites

- .NET 10 SDK + SQL Server LocalDB `MSSQLLocalDB` (verified).
- Branch: from `feat/dotnet-p1a2-cache-wrapper` (P1a tip; not merged), create `feat/dotnet-p1b1-identity-watchlist`.

## File structure

```
dotnet/
  src/WorldMonitor.Data/
    Entities/Identity/User.cs
    Entities/Identity/UserPreference.cs
    Entities/Watchlist/FollowedCountry.cs
    Configurations/UserConfiguration.cs
    Configurations/UserPreferenceConfiguration.cs
    Configurations/FollowedCountryConfiguration.cs
    Repositories/UserPreferenceRepository.cs   (+ PreferenceSetResult)
    Repositories/FollowedCountryRepository.cs  (+ FollowResult enum + WatchlistOptions)
    WorldMonitorDbContext.cs                    (MODIFY: add 3 DbSets)
    Migrations/                                  (new: AddIdentityWatchlist)
  test/WorldMonitor.Data.Tests/
    Integration/UserEntityTests.cs
    Integration/UserPreferenceCasTests.cs
    Integration/FollowedCountryTests.cs
```

---

### Task 1: Branch + `User` entity & config

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Entities/Identity/User.cs`
- Create: `dotnet/src/WorldMonitor.Data/Configurations/UserConfiguration.cs`

- [ ] **Step 1: Branch** (stacked on P1a tip)

Run:
```bash
git checkout feat/dotnet-p1a2-cache-wrapper && git checkout -b feat/dotnet-p1b1-identity-watchlist
```
Expected: `Switched to a new branch 'feat/dotnet-p1b1-identity-watchlist'`

- [ ] **Step 2: Entity** (fields from `convex/schema.ts:587-600`)

Create `Entities/Identity/User.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Identity;

/// <summary>An authenticated principal. UserId is the external subject id (Clerk today; the .NET
/// principal source is decided in P3). Email/NormalizedEmail are server-derived.</summary>
public sealed class User
{
    public required string UserId { get; set; }
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public string? LocaleTag { get; set; }
    public string? LocalePrimary { get; set; }
    public string? Timezone { get; set; }
    public string? Country { get; set; }            // ISO 3166-1 alpha-2; client-reported
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}
```

- [ ] **Step 3: Configuration** (`by_userId` unique, `by_normalizedEmail`; `localePrimary` index dropped — fed the removed broadcast filter)

Create `Configurations/UserConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Identity;

namespace WorldMonitor.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(u => u.UserId);
        b.Property(u => u.UserId).HasMaxLength(128);
        b.Property(u => u.Email).HasMaxLength(320);
        b.Property(u => u.NormalizedEmail).HasMaxLength(320);
        b.Property(u => u.LocaleTag).HasMaxLength(35);
        b.Property(u => u.LocalePrimary).HasMaxLength(16);
        b.Property(u => u.Timezone).HasMaxLength(64);
        b.Property(u => u.Country).HasMaxLength(2);
        b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("IX_Users_NormalizedEmail");
    }
}
```

- [ ] **Step 4: Build** (DbContext wiring + migration come in Task 4; just compile the new files)

Run: `dotnet build dotnet/src/WorldMonitor.Data`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add dotnet/
git commit -m "feat(data): add User identity entity + configuration"
```

---

### Task 2: `UserPreference` entity & config

`SyncVersion` is a **plain column**, NOT an EF concurrency token — the contract is a *client-supplied* expected-version CAS (Task 5), which is a different mechanism from EF's load-original optimistic concurrency.

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Entities/Identity/UserPreference.cs`
- Create: `dotnet/src/WorldMonitor.Data/Configurations/UserPreferenceConfiguration.cs`

- [ ] **Step 1: Entity** (fields from `convex/schema.ts:24-31`)

Create `Entities/Identity/UserPreference.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Identity;

/// <summary>Per-user, per-variant preference blob. <see cref="SyncVersion"/> implements a
/// client-supplied compare-and-set (see UserPreferenceRepository), NOT EF optimistic concurrency.</summary>
public sealed class UserPreference
{
    public int Id { get; set; }                     // surrogate PK
    public required string UserId { get; set; }
    public required string Variant { get; set; }
    public required string Data { get; set; }       // JSON blob (nvarchar(max))
    public int SchemaVersion { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int SyncVersion { get; set; }
}
```

- [ ] **Step 2: Configuration** (unique `(UserId, Variant)`)

Create `Configurations/UserPreferenceConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Identity;

namespace WorldMonitor.Data.Configurations;

public sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> b)
    {
        b.ToTable("UserPreferences");
        b.HasKey(p => p.Id);
        b.Property(p => p.UserId).HasMaxLength(128);
        b.Property(p => p.Variant).HasMaxLength(64);
        b.Property(p => p.Data).HasColumnType("nvarchar(max)");
        b.HasIndex(p => new { p.UserId, p.Variant }).IsUnique().HasDatabaseName("UX_UserPreferences_User_Variant");
    }
}
```

- [ ] **Step 3: Build + commit**

Run: `dotnet build dotnet/src/WorldMonitor.Data`
Expected: 0 errors.
```bash
git add dotnet/
git commit -m "feat(data): add UserPreference entity + unique(UserId,Variant) config"
```

---

### Task 3: `FollowedCountry` entity & config

The real **`UNIQUE(UserId,Country)`** is what makes the three Convex OCC tables (`followedCountriesCountryLocks`, `followedCountriesUserMeta`, `followedCountriesShards`) and the denormalized `followedCountriesCounts` unnecessary.

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Entities/Watchlist/FollowedCountry.cs`
- Create: `dotnet/src/WorldMonitor.Data/Configurations/FollowedCountryConfiguration.cs`

- [ ] **Step 1: Entity** (fields from `convex/schema.ts:134-141`)

Create `Entities/Watchlist/FollowedCountry.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Watchlist;

/// <summary>A country a user follows. UNIQUE(UserId, Country) is the hard guard against duplicate
/// follows and replaces the legacy Convex OCC shard/lock/meta scaffolding.</summary>
public sealed class FollowedCountry
{
    public int Id { get; set; }                     // surrogate PK
    public required string UserId { get; set; }
    public required string Country { get; set; }    // ISO 3166-1 alpha-2, uppercase
    public DateTime AddedAt { get; set; }
}
```

- [ ] **Step 2: Configuration** (unique `(UserId, Country)`; index on `Country` for follower counts)

Create `Configurations/FollowedCountryConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Watchlist;

namespace WorldMonitor.Data.Configurations;

public sealed class FollowedCountryConfiguration : IEntityTypeConfiguration<FollowedCountry>
{
    public void Configure(EntityTypeBuilder<FollowedCountry> b)
    {
        b.ToTable("FollowedCountries");
        b.HasKey(f => f.Id);
        b.Property(f => f.UserId).HasMaxLength(128);
        b.Property(f => f.Country).HasMaxLength(2);
        b.HasIndex(f => new { f.UserId, f.Country }).IsUnique().HasDatabaseName("UX_FollowedCountries_User_Country");
        b.HasIndex(f => f.Country).HasDatabaseName("IX_FollowedCountries_Country");
    }
}
```

- [ ] **Step 3: Build + commit**

Run: `dotnet build dotnet/src/WorldMonitor.Data`
Expected: 0 errors.
```bash
git add dotnet/
git commit -m "feat(data): add FollowedCountry entity + unique(UserId,Country) config"
```

---

### Task 4: Wire DbContext + create migration; verify it applies

**Files:**
- Modify: `dotnet/src/WorldMonitor.Data/WorldMonitorDbContext.cs`
- Create: `dotnet/src/WorldMonitor.Data/Migrations/` (new migration)
- Test: `dotnet/test/WorldMonitor.Data.Tests/Integration/UserEntityTests.cs`

- [ ] **Step 1: Add DbSets**

In `WorldMonitorDbContext.cs`, add `using WorldMonitor.Data.Entities.Identity;` and `using WorldMonitor.Data.Entities.Watchlist;`, then add these properties next to `CacheEntries`:
```csharp
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<FollowedCountry> FollowedCountries => Set<FollowedCountry>();
```
(`OnModelCreating` already calls `ApplyConfigurationsFromAssembly`, so the new configurations are picked up automatically.)

- [ ] **Step 2: Create the migration**

Run:
```bash
dotnet ef migrations add AddIdentityWatchlist --project dotnet/src/WorldMonitor.Data --startup-project dotnet/src/WorldMonitor.Data
dotnet build dotnet/src/WorldMonitor.Data
```
Expected: a new `*_AddIdentityWatchlist.cs` migration appears creating `Users`, `UserPreferences`, `FollowedCountries` with the unique indexes; build succeeds. (The `CacheEntries` table is untouched — it already exists in `InitialCache`.)

- [ ] **Step 3: Write the failing test** (proves the migration applies + basic CRUD)

Create `Integration/UserEntityTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Identity;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class UserEntityTests(LocalDbFixture fx)
{
    [Fact]
    public async Task User_round_trips_and_normalizedEmail_is_queryable()
    {
        var id = "u_" + Guid.NewGuid().ToString("N");
        await using (var ctx = fx.NewContext())
        {
            ctx.Users.Add(new User { UserId = id, Email = "Ada@Example.com", NormalizedEmail = "ada@example.com",
                FirstSeenAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fx.NewContext())
        {
            var found = await ctx.Users.SingleAsync(u => u.NormalizedEmail == "ada@example.com" && u.UserId == id);
            Assert.Equal("Ada@Example.com", found.Email);
        }
    }
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: PASS (migration applies; the new test + the existing 10 cache integration tests pass). If the cached test DB predates this migration, the fixture's `EnsureDeletedAsync` + `MigrateAsync` recreates it fresh.

- [ ] **Step 5: Commit**

```bash
git add dotnet/
git commit -m "feat(data): wire Identity/Watchlist DbSets + AddIdentityWatchlist migration"
```

---

### Task 5: `UserPreferenceRepository` — client-version compare-and-set

The legacy `setPreferences(expectedSyncVersion)` returns `{ ok: false, actualSyncVersion }` **without throwing** on a version mismatch — a client-supplied CAS, atomic via a conditional `UPDATE ... WHERE SyncVersion = @expected`.

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Repositories/UserPreferenceRepository.cs`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Integration/UserPreferenceCasTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Integration/UserPreferenceCasTests.cs`:
```csharp
using WorldMonitor.Data.Repositories;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class UserPreferenceCasTests(LocalDbFixture fx)
{
    private static string U() => "u_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task First_write_with_expected_zero_inserts_at_version_1()
    {
        var repo = new UserPreferenceRepository(fx.NewContext());
        var r = await repo.SetAsync(U(), "full", "{\"a\":1}", schemaVersion: 1, expectedSyncVersion: 0);
        Assert.True(r.Ok);
        Assert.Equal(1, r.SyncVersion);
    }

    [Fact]
    public async Task Matching_version_updates_and_increments()
    {
        var user = U();
        var repo = new UserPreferenceRepository(fx.NewContext());
        await repo.SetAsync(user, "full", "{\"a\":1}", 1, 0);            // → v1
        var r = await repo.SetAsync(user, "full", "{\"a\":2}", 1, 1);    // expected 1 → v2
        Assert.True(r.Ok);
        Assert.Equal(2, r.SyncVersion);
    }

    [Fact]
    public async Task Stale_expected_version_conflicts_and_reports_actual_without_throwing()
    {
        var user = U();
        var repo = new UserPreferenceRepository(fx.NewContext());
        await repo.SetAsync(user, "full", "{\"a\":1}", 1, 0);            // → v1
        var r = await repo.SetAsync(user, "full", "{\"a\":99}", 1, 0);   // expected 0 but actual is 1
        Assert.False(r.Ok);
        Assert.Equal(1, r.SyncVersion);                                  // actual reported back
    }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: FAIL — `UserPreferenceRepository` does not exist.

- [ ] **Step 3: Implement**

Create `Repositories/UserPreferenceRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Identity;

namespace WorldMonitor.Data.Repositories;

/// <summary>Outcome of a compare-and-set. <see cref="SyncVersion"/> is the NEW version on success,
/// or the ACTUAL stored version on conflict.</summary>
public readonly record struct PreferenceSetResult(bool Ok, int SyncVersion)
{
    public static PreferenceSetResult Success(int newVersion) => new(true, newVersion);
    public static PreferenceSetResult Conflict(int actualVersion) => new(false, actualVersion);
}

public sealed class UserPreferenceRepository(WorldMonitorDbContext db)
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
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow)
                .SetProperty(p => p.SyncVersion, p => p.SyncVersion + 1), ct);

        if (affected == 1) return PreferenceSetResult.Success(expectedSyncVersion + 1);

        var current = await db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Variant == variant, ct);

        if (current is null && expectedSyncVersion == 0)
        {
            db.UserPreferences.Add(new UserPreference
            {
                UserId = userId, Variant = variant, Data = data,
                SchemaVersion = schemaVersion, UpdatedAt = DateTime.UtcNow, SyncVersion = 1,
            });
            try
            {
                await db.SaveChangesAsync(ct);
                return PreferenceSetResult.Success(1);
            }
            catch (DbUpdateException) // lost the insert race on UX_UserPreferences_User_Variant
            {
                var raced = await db.UserPreferences.AsNoTracking()
                    .FirstAsync(p => p.UserId == userId && p.Variant == variant, ct);
                return PreferenceSetResult.Conflict(raced.SyncVersion);
            }
        }

        return PreferenceSetResult.Conflict(current?.SyncVersion ?? 0);
    }
}
```

- [ ] **Step 4: Run, verify pass; commit**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: PASS.
```bash
git add dotnet/
git commit -m "feat(data): UserPreferenceRepository client-version compare-and-set"
```

---

### Task 6: `FollowedCountryRepository` — idempotent follow, cap, count with privacy floor

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Repositories/FollowedCountryRepository.cs`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Integration/FollowedCountryTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Integration/FollowedCountryTests.cs`:
```csharp
using WorldMonitor.Data.Repositories;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class FollowedCountryTests(LocalDbFixture fx)
{
    private static string U() => "u_" + Guid.NewGuid().ToString("N");
    private FollowedCountryRepository Repo() => new(fx.NewContext(), new WatchlistOptions(MaxPerUser: 3, PrivacyFloor: 5));

    [Fact]
    public async Task Follow_is_idempotent_and_case_insensitive()
    {
        var u = U();
        Assert.Equal(FollowResult.Followed, await Repo().FollowAsync(u, "us"));
        Assert.Equal(FollowResult.AlreadyFollowing, await Repo().FollowAsync(u, "US")); // normalized, unique
        Assert.Equal(1, await Repo().CountForUserAsync(u));
    }

    [Fact]
    public async Task Cap_blocks_the_over_limit_follow()
    {
        var u = U();
        var repo = Repo();
        Assert.Equal(FollowResult.Followed, await repo.FollowAsync(u, "US"));
        Assert.Equal(FollowResult.Followed, await repo.FollowAsync(u, "GB"));
        Assert.Equal(FollowResult.Followed, await repo.FollowAsync(u, "FR"));
        Assert.Equal(FollowResult.CapReached, await repo.FollowAsync(u, "DE")); // MaxPerUser = 3
    }

    [Fact]
    public async Task Unfollow_removes_and_is_idempotent()
    {
        var u = U();
        var repo = Repo();
        await repo.FollowAsync(u, "US");
        Assert.True(await repo.UnfollowAsync(u, "us"));
        Assert.False(await repo.UnfollowAsync(u, "us"));
        Assert.Equal(0, await repo.CountForUserAsync(u));
    }

    [Fact]
    public async Task Follower_count_applies_privacy_floor()
    {
        var country = "Z" + Guid.NewGuid().ToString("N")[..1]; // unlikely to collide with seeded data
        // 4 followers — below the floor of 5 ⇒ reported as 0
        for (var i = 0; i < 4; i++) await Repo().FollowAsync(U(), country);
        Assert.Equal(0, await Repo().CountFollowersAsync(country));

        await Repo().FollowAsync(U(), country); // 5th ⇒ at/above floor
        Assert.Equal(5, await Repo().CountFollowersAsync(country));
    }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: FAIL — `FollowedCountryRepository`/`WatchlistOptions`/`FollowResult` do not exist.

- [ ] **Step 3: Implement**

Create `Repositories/FollowedCountryRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Watchlist;

namespace WorldMonitor.Data.Repositories;

public enum FollowResult { Followed, AlreadyFollowing, CapReached }

/// <param name="MaxPerUser">Per-user follow cap (legacy: 50).</param>
/// <param name="PrivacyFloor">Follower counts below this are reported as 0 (legacy: 5).</param>
public sealed record WatchlistOptions(int MaxPerUser = 50, int PrivacyFloor = 5);

public sealed class FollowedCountryRepository(WorldMonitorDbContext db, WatchlistOptions options)
{
    private static string Norm(string country) => country.Trim().ToUpperInvariant();

    public async Task<FollowResult> FollowAsync(string userId, string country, CancellationToken ct = default)
    {
        var c = Norm(country);
        if (await db.FollowedCountries.AnyAsync(f => f.UserId == userId && f.Country == c, ct))
            return FollowResult.AlreadyFollowing;
        // Best-effort cap; the UNIQUE(UserId,Country) constraint is the hard correctness guard.
        if (await db.FollowedCountries.CountAsync(f => f.UserId == userId, ct) >= options.MaxPerUser)
            return FollowResult.CapReached;

        db.FollowedCountries.Add(new FollowedCountry { UserId = userId, Country = c, AddedAt = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync(ct);
            return FollowResult.Followed;
        }
        catch (DbUpdateException) // concurrent duplicate lost the unique race
        {
            return FollowResult.AlreadyFollowing;
        }
    }

    public async Task<bool> UnfollowAsync(string userId, string country, CancellationToken ct = default)
    {
        var c = Norm(country);
        var removed = await db.FollowedCountries
            .Where(f => f.UserId == userId && f.Country == c)
            .ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    public Task<int> CountForUserAsync(string userId, CancellationToken ct = default)
        => db.FollowedCountries.CountAsync(f => f.UserId == userId, ct);

    /// <summary>The single place the privacy floor is applied. Counts below the floor read as 0.</summary>
    public async Task<int> CountFollowersAsync(string country, CancellationToken ct = default)
    {
        var n = await db.FollowedCountries.CountAsync(f => f.Country == Norm(country), ct);
        return n < options.PrivacyFloor ? 0 : n;
    }
}
```

- [ ] **Step 4: Run, verify pass; commit**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: PASS.
```bash
git add dotnet/
git commit -m "feat(data): FollowedCountryRepository (idempotent follow, cap, floor count)"
```

---

### Task 7: Green build, README, PR

**Files:**
- Modify: `dotnet/README.md`

- [ ] **Step 1: Update README**

Add to `dotnet/README.md` (after the `WorldMonitor.Caching` entry):
```markdown
- `src/WorldMonitor.Data` (P1b-1) — first domain entities: `User`, `UserPreference` (client-version
  compare-and-set repository), `FollowedCountry` (`UNIQUE(UserId,Country)` retires the legacy OCC
  scaffolding; follower count via a single floor-applying helper). LocalDB integration tests.
```

- [ ] **Step 2: Full clean build + test**

Run:
```bash
dotnet build dotnet/WorldMonitor.slnx -c Release
dotnet test dotnet/WorldMonitor.slnx -c Release --filter Category!=Integration
dotnet test dotnet/WorldMonitor.slnx -c Release
```
Expected: build 0/0; unit ring unchanged green (no new DB-free tests here); full suite green (adds the new Identity/Watchlist integration tests to the existing LocalDB set).

- [ ] **Step 3: Commit, push, open PR**

```bash
git add dotnet/
git commit -m "docs(data): document P1b-1 Identity/Watchlist entities"
git push -u origin feat/dotnet-p1b1-identity-watchlist
gh pr create --base feat/dotnet-p1a2-cache-wrapper --title "P1b-1: Identity + Watchlist entities" --body "Implements P1b-1: User, UserPreference (client-version compare-and-set, not EF optimistic concurrency), FollowedCountry (UNIQUE(UserId,Country) retiring 3 Convex OCC tables; idempotent follow; best-effort cap; follower count with privacy floor in one helper). EF entities + repositories + AddIdentityWatchlist migration; LocalDB integration tests. Dropped per product decision: broadcast/email cluster, MCP, billing, counters, LlmAssessmentCache. Stacked on #3 (P1a-2). Notifications (P1b-2) and Waitlist/Access (P1b-3) follow."
```
Expected: PR URL printed.

---

## Self-review

**Spec coverage (P1b-1 scope, from `convex/schema.ts` + the locked product decisions):**
- ✅ `User` entity (fields from `users` L587; `localePrimary` index dropped with broadcast) — Task 1.
- ✅ `UserPreference` + **client-version CAS** returning conflict+actual without throwing (the critic's MEDIUM finding) — Tasks 2, 5.
- ✅ `FollowedCountry` with **`UNIQUE(UserId,Country)`** retiring the 3 OCC tables + the count table; idempotent follow; floor-applying count helper (the critic's "floor in one place" requirement) — Tasks 3, 6.
- ✅ Migration applies to LocalDB; entities round-trip — Task 4.
- **Deferred (documented in header):** Notifications → P1b-2; Waitlist/Access → P1b-3; vector/correlation → P1c; auth principal/`clerkUserId` linking → P3; DI registration → P2/P3.
- **Documented simplification:** the per-user follow cap is best-effort (a serializable transaction or per-user app-lock would make it strict); the **hard** invariant — no duplicate follows — is enforced by the unique constraint, which is the actual correctness property the legacy OCC machinery protected.

**Placeholder scan:** none — every code/command step is complete.

**Type consistency:** the three entities are configured by matching `IEntityTypeConfiguration`s picked up via the existing `ApplyConfigurationsFromAssembly`. `UserPreferenceRepository.SetAsync` returns `PreferenceSetResult` (Task 5) used in its tests. `FollowedCountryRepository` returns `FollowResult` and takes `WatchlistOptions` (Task 6) used in its tests. The `WorldMonitorDbContext` DbSets (Task 4) back every repository query.

**Note for execution:** the migration recreates the LocalDB test database fresh via the fixture's `EnsureDeletedAsync` + `MigrateAsync`, so the new tables and the existing `CacheEntries` coexist. No DB-free unit tests are added in this slice (the logic is inherently DB-coupled); all new tests are `[Trait("Category","Integration")]`.
```
