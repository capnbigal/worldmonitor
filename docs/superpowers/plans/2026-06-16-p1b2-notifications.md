# P1b-2 — Notifications Entities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the notification domain to `WorldMonitor.Data` — `NotificationChannel` (TPH over the six channel types), `AlertRule`, and `TelegramPairingToken` — with the **web-push cross-user ownership guard** ported faithfully from the legacy mutation (the critic's HIGH finding): a re-registered push endpoint transfers ownership to the new user instead of leaking alerts to two accounts.

**Architecture:** `NotificationChannel` is an abstract base mapped **Table-Per-Hierarchy** with `ChannelType` as the discriminator and one sealed subclass per channel (`Telegram`/`Slack`/`Email`/`Discord`/`Webhook`/`WebPush`); subclass-specific fields become nullable columns. `UNIQUE(UserId, ChannelType)` enforces one channel per type per user (the legacy `.unique()` model); a **filtered `UNIQUE(Endpoint)`** is the hard backstop for the web-push guard. `NotificationChannelRepository.SetWebPushAsync` reproduces the legacy ownership transfer: inside a serializable transaction it deletes *any* web-push row matching the endpoint (across all users) then upserts the caller's row. `AlertRule` stores its array fields (`eventTypes`/`channels`/`countries`) as JSON via EF Core primitive collections and its enum-like fields as plain wire-literal strings; `aiDigestEnabled` is retained as an inert nullable column (no generative AI). `TelegramPairingToken` has a unique `Token`. Everything is LocalDB integration-tested.

**Tech Stack:** .NET 10, EF Core 10 (TPH, `ExecuteDeleteAsync`, primitive collections), xUnit + LocalDB.

**This is P1b-2 of the program roadmap** (phase P1b). Builds on P1b-1 (now in `main`). Source of truth: `convex/schema.ts` (`notificationChannels` L33, `alertRules` L99, `telegramPairingTokens` L217), `convex/constants.ts` (enum values), and `convex/notificationChannels.ts:77-132` (the web-push ownership-transfer mutation).

**Enum wire values** (from `convex/constants.ts`): channelType = `telegram|slack|email|discord|webhook|web_push`; sensitivity = `all|high|critical`; digestMode = `realtime|daily|twice_daily|weekly`; quietHoursOverride = `critical_only|silence_all|batch_on_wake`. Stored as the literal strings.

**Conventions established in P1b-1 (reuse):** narrow `catch (DbUpdateException) when (ex.InnerException is SqlException { Number: 2627 or 2601 })`; inject `IClock` for domain timestamps.

**Explicitly deferred (so reviewers don't flag as gaps):**
- Non-web-push channel upserts (telegram/slack/email/discord/webhook) + AlertRule CRUD service methods → wired when the API needs them (**P2**). This slice ships the entity model + the web-push guard (the only non-trivial concurrency logic).
- Telegram-pairing token *claim* flow (multi-table write) + expiry cleanup cron → **P2/P7**.
- Waitlist/Access (Registration, referral, ContactMessage, emailSuppressions, UserApiKey) → **P1b-3**; vector/correlation → **P1c**.
- `aiDigestEnabled` is kept but inert (never read by any retained path).

---

## Prerequisites

- .NET 10 SDK + SQL Server LocalDB `MSSQLLocalDB` (verified).
- Branch: from `main` (P1b-1 merged), create `feat/dotnet-p1b2-notifications`.

## File structure

```
dotnet/
  src/WorldMonitor.Data/
    Entities/Notifications/NotificationChannel.cs        (abstract base + 6 sealed subclasses)
    Entities/Notifications/AlertRule.cs
    Entities/Notifications/TelegramPairingToken.cs
    Configurations/NotificationChannelConfiguration.cs   (TPH + unique + web-push endpoint filtered-unique)
    Configurations/AlertRuleConfiguration.cs
    Configurations/TelegramPairingTokenConfiguration.cs
    Repositories/NotificationChannelRepository.cs        (SetWebPushAsync ownership transfer)
    WorldMonitorDbContext.cs                              (MODIFY: add 3 DbSets)
    Migrations/                                            (new: AddNotifications)
  test/WorldMonitor.Data.Tests/
    Integration/NotificationChannelTests.cs              (TPH round-trip + unique)
    Integration/WebPushOwnershipTests.cs                 (the cross-user guard)
    Integration/AlertRuleTests.cs                        (JSON arrays + enums + unique)
```

---

### Task 1: Branch + entities

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Entities/Notifications/NotificationChannel.cs`
- Create: `dotnet/src/WorldMonitor.Data/Entities/Notifications/AlertRule.cs`
- Create: `dotnet/src/WorldMonitor.Data/Entities/Notifications/TelegramPairingToken.cs`

- [ ] **Step 1: Branch**

Run:
```bash
git checkout main && git checkout -b feat/dotnet-p1b2-notifications
```
Expected: `Switched to a new branch 'feat/dotnet-p1b2-notifications'`

- [ ] **Step 2: Channel entities** (fields from `convex/schema.ts:33-97`)

Create `Entities/Notifications/NotificationChannel.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Notifications;

/// <summary>Base for a user's notification channel (TPH; ChannelType is the discriminator).
/// One channel per (UserId, ChannelType) — enforced by a unique index.</summary>
public abstract class NotificationChannel
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public string ChannelType { get; set; } = null!; // discriminator — MUST be null! (not ""): EF will not override a non-null property initializer, so "" would collapse every type onto one discriminator value
    public bool Verified { get; set; }
    public DateTime LinkedAt { get; set; }
}

public sealed class TelegramChannel : NotificationChannel
{
    public required string ChatId { get; set; }
}

public sealed class SlackChannel : NotificationChannel
{
    public required string WebhookEnvelope { get; set; }
    public string? SlackChannelName { get; set; }
    public string? SlackTeamName { get; set; }
    public string? SlackConfigurationUrl { get; set; }
}

public sealed class EmailChannel : NotificationChannel
{
    public required string Email { get; set; }
}

public sealed class DiscordChannel : NotificationChannel
{
    public required string WebhookEnvelope { get; set; }
    public string? DiscordGuildId { get; set; }
    public string? DiscordChannelId { get; set; }
}

public sealed class WebhookChannel : NotificationChannel
{
    public required string WebhookEnvelope { get; set; }
    public string? WebhookLabel { get; set; }
    public string? WebhookSecret { get; set; }
}

public sealed class WebPushChannel : NotificationChannel
{
    public required string Endpoint { get; set; }
    public required string P256dh { get; set; }
    public required string Auth { get; set; }
    public string? UserAgent { get; set; }
}
```
> Note: `WebhookEnvelope` appears on Slack/Discord/Webhook. EF Core maps same-named, same-type sibling properties in a TPH hierarchy to **one shared column** by default. If `dotnet ef migrations add` (Task 3) errors about column sharing, add `.HasColumnName("WebhookEnvelope")` to each of the three properties in the configuration. (Either way is behavior-equivalent.)

- [ ] **Step 3: AlertRule** (fields from `convex/schema.ts:99-122`)

Create `Entities/Notifications/AlertRule.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Notifications;

/// <summary>A user's per-variant alerting rule. Array fields are JSON (EF primitive collections);
/// enum-like fields hold the wire literal strings. AiDigestEnabled is INERT (no generative AI).</summary>
public sealed class AlertRule
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string Variant { get; set; }
    public bool Enabled { get; set; }
    public List<string> EventTypes { get; set; } = [];
    public string Sensitivity { get; set; } = "all";        // all|high|critical
    public List<string> Channels { get; set; } = [];        // channelType values
    public DateTime UpdatedAt { get; set; }
    public bool? QuietHoursEnabled { get; set; }
    public int? QuietHoursStart { get; set; }
    public int? QuietHoursEnd { get; set; }
    public string? QuietHoursTimezone { get; set; }
    public string? QuietHoursOverride { get; set; }         // critical_only|silence_all|batch_on_wake
    public string? DigestMode { get; set; }                 // realtime|daily|twice_daily|weekly
    public int? DigestHour { get; set; }
    public string? DigestTimezone { get; set; }
    public bool? AiDigestEnabled { get; set; }              // inert
    public List<string> Countries { get; set; } = [];
}
```

- [ ] **Step 4: TelegramPairingToken** (fields from `convex/schema.ts:217-225`)

Create `Entities/Notifications/TelegramPairingToken.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Notifications;

public sealed class TelegramPairingToken
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string Token { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }
    public string? Variant { get; set; }
}
```

- [ ] **Step 5: Build + commit**

Run: `dotnet build dotnet/src/WorldMonitor.Data`
Expected: 0 errors.
```bash
git add dotnet/
git commit -m "feat(data): add notification channel (TPH), AlertRule, TelegramPairingToken entities"
```

---

### Task 2: Configurations

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Configurations/NotificationChannelConfiguration.cs`
- Create: `dotnet/src/WorldMonitor.Data/Configurations/AlertRuleConfiguration.cs`
- Create: `dotnet/src/WorldMonitor.Data/Configurations/TelegramPairingTokenConfiguration.cs`

- [ ] **Step 1: NotificationChannel TPH config**

Create `Configurations/NotificationChannelConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Notifications;

namespace WorldMonitor.Data.Configurations;

public sealed class NotificationChannelConfiguration : IEntityTypeConfiguration<NotificationChannel>
{
    public void Configure(EntityTypeBuilder<NotificationChannel> b)
    {
        b.ToTable("NotificationChannels");
        b.HasKey(c => c.Id);
        b.Property(c => c.UserId).HasMaxLength(128);
        b.Property(c => c.ChannelType).HasMaxLength(16);

        b.HasDiscriminator(c => c.ChannelType)
            .HasValue<TelegramChannel>("telegram")
            .HasValue<SlackChannel>("slack")
            .HasValue<EmailChannel>("email")
            .HasValue<DiscordChannel>("discord")
            .HasValue<WebhookChannel>("webhook")
            .HasValue<WebPushChannel>("web_push");

        // One channel per type per user (legacy `.unique()` on by_user_channel).
        b.HasIndex(c => new { c.UserId, c.ChannelType }).IsUnique().HasDatabaseName("UX_NotificationChannels_User_Channel");

        // Web-push endpoint is globally unique (one browser endpoint ↦ one user) — the hard backstop
        // for the cross-user ownership guard. Filtered so non-web-push rows (Endpoint NULL) are excluded.
        b.Entity<WebPushChannel>().HasIndex(w => w.Endpoint)
            .IsUnique().HasFilter("[Endpoint] IS NOT NULL").HasDatabaseName("UX_NotificationChannels_Endpoint");
    }
}
```
> Note: `b.Entity<WebPushChannel>()` is available because `EntityTypeBuilder<NotificationChannel>` exposes the model builder for derived types via `b.Metadata`. If the compiler rejects `b.Entity<>()` here, configure the WebPush index in `OnModelCreating` instead: `modelBuilder.Entity<WebPushChannel>().HasIndex(...)`. (Functionally identical.)

- [ ] **Step 2: AlertRule config**

Create `Configurations/AlertRuleConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Notifications;

namespace WorldMonitor.Data.Configurations;

public sealed class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> b)
    {
        b.ToTable("AlertRules");
        b.HasKey(r => r.Id);
        b.Property(r => r.UserId).HasMaxLength(128);
        b.Property(r => r.Variant).HasMaxLength(64);
        b.Property(r => r.Sensitivity).HasMaxLength(16);
        b.Property(r => r.QuietHoursOverride).HasMaxLength(32);
        b.Property(r => r.DigestMode).HasMaxLength(16);
        b.Property(r => r.QuietHoursTimezone).HasMaxLength(64);
        b.Property(r => r.DigestTimezone).HasMaxLength(64);
        // EventTypes/Channels/Countries map to JSON columns automatically (EF Core primitive collections).
        b.HasIndex(r => new { r.UserId, r.Variant }).IsUnique().HasDatabaseName("UX_AlertRules_User_Variant");
        b.HasIndex(r => r.Enabled).HasDatabaseName("IX_AlertRules_Enabled");
    }
}
```

- [ ] **Step 3: TelegramPairingToken config**

Create `Configurations/TelegramPairingTokenConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Notifications;

namespace WorldMonitor.Data.Configurations;

public sealed class TelegramPairingTokenConfiguration : IEntityTypeConfiguration<TelegramPairingToken>
{
    public void Configure(EntityTypeBuilder<TelegramPairingToken> b)
    {
        b.ToTable("TelegramPairingTokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.UserId).HasMaxLength(128);
        b.Property(t => t.Token).HasMaxLength(64);
        b.Property(t => t.Variant).HasMaxLength(64);
        b.HasIndex(t => t.Token).IsUnique().HasDatabaseName("UX_TelegramPairingTokens_Token");
        b.HasIndex(t => t.UserId).HasDatabaseName("IX_TelegramPairingTokens_User");
    }
}
```

- [ ] **Step 4: Build + commit**

Run: `dotnet build dotnet/src/WorldMonitor.Data`
Expected: 0 errors.
```bash
git add dotnet/
git commit -m "feat(data): configure notification TPH + unique constraints + alert/token indexes"
```

---

### Task 3: Wire DbSets, migration, round-trip tests

**Files:**
- Modify: `dotnet/src/WorldMonitor.Data/WorldMonitorDbContext.cs`
- Create: migration `AddNotifications`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Integration/NotificationChannelTests.cs`, `dotnet/test/WorldMonitor.Data.Tests/Integration/AlertRuleTests.cs`

- [ ] **Step 1: Add DbSets**

In `WorldMonitorDbContext.cs`, add `using WorldMonitor.Data.Entities.Notifications;` and these properties:
```csharp
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<TelegramPairingToken> TelegramPairingTokens => Set<TelegramPairingToken>();
```

- [ ] **Step 2: Create the migration**

Run:
```bash
dotnet ef migrations add AddNotifications --project dotnet/src/WorldMonitor.Data --startup-project dotnet/src/WorldMonitor.Data
dotnet build dotnet/src/WorldMonitor.Data
```
Expected: a migration creating `NotificationChannels` (with `ChannelType` discriminator, all subclass columns nullable, the `UX_NotificationChannels_User_Channel` unique index, and the filtered `UX_NotificationChannels_Endpoint` unique index), `AlertRules` (with JSON columns for the three list fields), and `TelegramPairingTokens` (unique `Token`). `CacheEntries`/`Users`/`UserPreferences`/`FollowedCountries` untouched.

- [ ] **Step 3: Write the failing tests**

Create `Integration/NotificationChannelTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Notifications;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class NotificationChannelTests(LocalDbFixture fx)
{
    private static string U() => "u_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Each_channel_type_round_trips_via_TPH()
    {
        var u = U();
        await using (var ctx = fx.NewContext())
        {
            ctx.NotificationChannels.Add(new TelegramChannel { UserId = u, ChatId = "123", Verified = true, LinkedAt = DateTime.UtcNow });
            ctx.NotificationChannels.Add(new EmailChannel { UserId = u, Email = "a@b.com", Verified = false, LinkedAt = DateTime.UtcNow });
            ctx.NotificationChannels.Add(new WebPushChannel { UserId = u, Endpoint = "https://push/" + u, P256dh = "k", Auth = "a", Verified = true, LinkedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fx.NewContext())
        {
            Assert.Equal("123", (await ctx.NotificationChannels.OfType<TelegramChannel>().SingleAsync(c => c.UserId == u)).ChatId);
            Assert.Equal("a@b.com", (await ctx.NotificationChannels.OfType<EmailChannel>().SingleAsync(c => c.UserId == u)).Email);
            Assert.Equal(3, await ctx.NotificationChannels.CountAsync(c => c.UserId == u));
        }
    }

    [Fact]
    public async Task Duplicate_channel_type_for_user_violates_unique_index()
    {
        var u = U();
        await using var ctx = fx.NewContext();
        ctx.NotificationChannels.Add(new EmailChannel { UserId = u, Email = "x@y.com", LinkedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.NotificationChannels.Add(new EmailChannel { UserId = u, Email = "z@y.com", LinkedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
```

Create `Integration/AlertRuleTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Notifications;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class AlertRuleTests(LocalDbFixture fx)
{
    private static string U() => "u_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task AlertRule_round_trips_json_arrays_and_enum_strings()
    {
        var u = U();
        await using (var ctx = fx.NewContext())
        {
            ctx.AlertRules.Add(new AlertRule
            {
                UserId = u, Variant = "full", Enabled = true,
                EventTypes = ["earthquake", "cyber"], Sensitivity = "high",
                Channels = ["telegram", "web_push"], Countries = ["US", "GB"],
                DigestMode = "twice_daily", AiDigestEnabled = true, UpdatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fx.NewContext())
        {
            var r = await ctx.AlertRules.SingleAsync(x => x.UserId == u && x.Variant == "full");
            Assert.Equal(["earthquake", "cyber"], r.EventTypes);
            Assert.Equal("high", r.Sensitivity);
            Assert.Equal("twice_daily", r.DigestMode);
            Assert.Equal(["US", "GB"], r.Countries);
        }
    }

    [Fact]
    public async Task Duplicate_user_variant_violates_unique_index()
    {
        var u = U();
        await using var ctx = fx.NewContext();
        ctx.AlertRules.Add(new AlertRule { UserId = u, Variant = "full", UpdatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.AlertRules.Add(new AlertRule { UserId = u, Variant = "full", UpdatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: PASS (the new tests + all prior integration tests; the fixture recreates the DB with the new migration).

- [ ] **Step 5: Commit**

```bash
git add dotnet/
git commit -m "feat(data): wire notification DbSets + AddNotifications migration + round-trip tests"
```

---

### Task 4: `NotificationChannelRepository.SetWebPushAsync` — cross-user ownership transfer

Faithful port of `convex/notificationChannels.ts:77-132`: a re-registered endpoint is **transferred** to the new caller (the prior owner's row is deleted), preventing the cross-account push leak.

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Repositories/NotificationChannelRepository.cs`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Integration/WebPushOwnershipTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Integration/WebPushOwnershipTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Notifications;
using WorldMonitor.Data.Repositories;
using WorldMonitor.Data.Time;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class WebPushOwnershipTests(LocalDbFixture fx)
{
    private NotificationChannelRepository Repo() => new(fx.NewContext(), new SystemClock());
    private static string U() => "u_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Re_registering_same_endpoint_transfers_ownership_to_new_user()
    {
        var endpoint = "https://push.example/" + Guid.NewGuid().ToString("N");
        var userA = U();
        var userB = U();

        Assert.True(await Repo().SetWebPushAsync(userA, endpoint, "kA", "aA", "Chrome"));   // isNew
        Assert.True(await Repo().SetWebPushAsync(userB, endpoint, "kB", "aB", "Firefox"));  // transfer ⇒ isNew for B

        await using var ctx = fx.NewContext();
        // The endpoint now belongs to exactly one row, owned by user B.
        var rows = await ctx.NotificationChannels.OfType<WebPushChannel>().Where(w => w.Endpoint == endpoint).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(userB, rows[0].UserId);
        // User A no longer has a web-push row.
        Assert.Empty(await ctx.NotificationChannels.OfType<WebPushChannel>().Where(w => w.UserId == userA).ToListAsync());
    }

    [Fact]
    public async Task Same_user_re_register_updates_in_place_not_duplicates()
    {
        var u = U();
        var ep1 = "https://push/" + Guid.NewGuid().ToString("N");
        var ep2 = "https://push/" + Guid.NewGuid().ToString("N");
        Assert.True(await Repo().SetWebPushAsync(u, ep1, "k1", "a1", null));   // isNew
        Assert.False(await Repo().SetWebPushAsync(u, ep2, "k2", "a2", null));  // existing user ⇒ not new

        await using var ctx = fx.NewContext();
        var rows = await ctx.NotificationChannels.OfType<WebPushChannel>().Where(w => w.UserId == u).ToListAsync();
        Assert.Single(rows);                 // one row per user (UX_NotificationChannels_User_Channel)
        Assert.Equal(ep2, rows[0].Endpoint); // updated to the latest endpoint
    }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: FAIL — `NotificationChannelRepository` does not exist.

- [ ] **Step 3: Implement**

Create `Repositories/NotificationChannelRepository.cs`:
```csharp
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
```

- [ ] **Step 4: Run, verify pass; commit**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: PASS.
```bash
git add dotnet/
git commit -m "feat(data): NotificationChannelRepository.SetWebPushAsync cross-user ownership transfer"
```

---

### Task 5: Green build, README, PR

**Files:**
- Modify: `dotnet/README.md`

- [ ] **Step 1: Update README**

Add to `dotnet/README.md` (after the P1b-1 entry):
```markdown
- `src/WorldMonitor.Data` (P1b-2) — Notifications: `NotificationChannel` (TPH over 6 channel types) with
  `UNIQUE(UserId,ChannelType)` + a filtered `UNIQUE(Endpoint)` backstop; `NotificationChannelRepository.SetWebPushAsync`
  transfers a re-registered push endpoint to the new owner (cross-account leak guard); `AlertRule` (JSON arrays +
  string enums, `aiDigestEnabled` inert); `TelegramPairingToken` (unique token).
```

- [ ] **Step 2: Full clean build + test**

Run:
```bash
dotnet build dotnet/WorldMonitor.slnx -c Release
dotnet test dotnet/WorldMonitor.slnx -c Release --filter Category!=Integration
dotnet test dotnet/WorldMonitor.slnx -c Release
```
Expected: build 0/0; unit ring unchanged green; full suite green (adds the new Notifications integration tests).

- [ ] **Step 3: Commit, push, open PR**

```bash
git add dotnet/
git commit -m "docs(data): document P1b-2 Notifications entities"
git push -u origin feat/dotnet-p1b2-notifications
gh pr create --base main --title "P1b-2: Notifications entities (TPH channels + web-push guard)" --body "Implements P1b-2: NotificationChannel TPH (telegram/slack/email/discord/webhook/web_push) with UNIQUE(UserId,ChannelType) + filtered UNIQUE(Endpoint); NotificationChannelRepository.SetWebPushAsync (faithful cross-user ownership transfer from convex/notificationChannels.ts); AlertRule (JSON-array fields, string enums, aiDigestEnabled inert) UNIQUE(UserId,Variant); TelegramPairingToken (unique Token). AddNotifications migration; LocalDB integration tests incl. the web-push ownership-transfer guard. Next: P1b-3 (Waitlist/Access), then P1c."
```
Expected: PR URL printed.

---

## Self-review

**Spec coverage (P1b-2 scope, from `convex/schema.ts` + `convex/notificationChannels.ts`):**
- ✅ `NotificationChannel` TPH over all 6 channel types; `UNIQUE(UserId,ChannelType)` (legacy `.unique()` model) — Tasks 1–2.
- ✅ **Web-push cross-user ownership guard** — transactional delete-by-endpoint + upsert, with a filtered `UNIQUE(Endpoint)` backstop (the critic's HIGH) — Tasks 2, 4.
- ✅ `AlertRule` with JSON-array fields + string enums (values from `constants.ts`); `aiDigestEnabled` retained but inert — Tasks 1, 3.
- ✅ `TelegramPairingToken` with unique `Token` — Tasks 1, 3.
- ✅ Migration applies on top of P1b-1's; round-trip + unique-constraint tests — Task 3.
- **Deferred (documented in header):** non-web-push channel CRUD + AlertRule service methods → P2; token claim/expiry → P2/P7; Waitlist/Access → P1b-3; vector/correlation → P1c.

**Placeholder scan:** none — every code/command step is complete.

**Type consistency:** the six channel subclasses extend `NotificationChannel` and are registered as TPH discriminator values matching the wire literals. `NotificationChannelRepository.SetWebPushAsync` (Task 4) uses `Set<WebPushChannel>()` + `IClock` (the P1b-1 convention). `AlertRule`/`TelegramPairingToken` DbSets (Task 3) back their tests. Enum-like fields are wire-literal strings throughout.

**Risk note:** the cross-user transfer is verified sequentially (A then B). Under genuine concurrency, the `Serializable` transaction + the filtered `UNIQUE(Endpoint)` index serialize same-endpoint registrations (the loser blocks then re-transfers, or hits the unique backstop). A multi-connection concurrency test is a reasonable P2 hardening but is out of scope for this entity slice.

**Note for execution:** if EF Core objects to the shared `WebhookEnvelope` column or the `b.Entity<WebPushChannel>()` call inside the configuration, apply the two fallbacks noted inline (explicit `HasColumnName`, or configure the WebPush index in `OnModelCreating`). All new tests are `[Trait("Category","Integration")]`.
```
