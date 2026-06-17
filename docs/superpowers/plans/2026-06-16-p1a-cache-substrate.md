# P1a-1 — SQL Cache Substrate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the SQL Server cache substrate that replaces Upstash Redis as the source of truth — a `CacheEntries` table plus a tri-state `ICacheStore` (concurrency-safe atomic upsert, no-resurrect TTL extension, batch read/probe) and an `ISeedLock` (single-writer `sp_getapplock`) — all integration-tested against SQL Server LocalDB.

**Architecture:** New `WorldMonitor.Data` project (EF Core 10 + SQL Server) owns the `WorldMonitorDbContext` (only `CacheEntry` in this slice), the `ICacheStore`/`ISeedLock`/`IClock` abstractions, and their SQL Server implementations. All SQL-Server-specific T-SQL (MERGE upsert with `HOLDLOCK`, `sp_getapplock`) lives **only** in the `Stores/` implementations behind interfaces, so the cache-semantics layer (P1a-2) and all pure logic are unit-testable without a database. Reads are tri-state (`Hit`/`Miss`/`Error`) so a SQL exception can never masquerade as a cache miss.

**Tech Stack:** .NET 10, C# 13, EF Core 10 (`Microsoft.EntityFrameworkCore.SqlServer`), `Microsoft.Data.SqlClient` (raw commands for MERGE/applock), xUnit. Integration tests run against **SQL Server LocalDB** (`(localdb)\MSSQLLocalDB`) — no Docker.

**This is P1a-1 of the program roadmap** (`docs/superpowers/specs/2026-06-16-blazor-dotnet-rewrite-design.md` §18, phase P1). Grounded in the P1 data-layer inventory (8-agent workflow). Builds on P0's `WorldMonitor.Contracts`.

**Explicitly deferred (so reviewers don't flag as gaps):**
- The **FusionCache wrapper** (coalescing, negative-sentinel, fail-safe reconciliation) → **P1a-2**.
- **Domain entities** (Users, AlertRule, FollowedCountry, …) → **P1b**.
- **Vector + correlation/ML state tables** → **P1c**.
- **Edge HTTP cache-tier headers / `TierResolver` / key-prefixing / bootstrap-unprefixed-read** → **P2 (gateway)**. The object cache here takes an explicit per-entry TTL; it is NOT driven by `Contracts.TierHeaders`.

---

## Prerequisites

- .NET 10 SDK (`dotnet --version` ≥ 10.0.100) — verified present.
- SQL Server LocalDB instance `MSSQLLocalDB` — verified present (`sqllocaldb info`).
- Branch: from `feat/dotnet-p0-contracts` (P0 is not yet merged to `main`; P1a-1 references P0's `WorldMonitor.Contracts` and `WorldMonitor.slnx`, so it **stacks** on the P0 branch). Create `feat/dotnet-p1a-cache-substrate` before Task 1.
- The connection string used by tests and design-time: `Server=(localdb)\MSSQLLocalDB;Database=WorldMonitorCacheTests;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False`.

## File structure

```
dotnet/
  src/WorldMonitor.Data/
    WorldMonitor.Data.csproj                 EF Core SqlServer + Design; ref Contracts
    Time/IClock.cs                           UtcNow seam (system + test impls)
    Time/SystemClock.cs
    Entities/CacheEntry.cs                    the CacheEntries row (payload + freshness)
    Configurations/CacheEntryConfiguration.cs Fluent config (PK, computed ByteLength, indexes)
    WorldMonitorDbContext.cs                  DbSet<CacheEntry> only (this slice)
    DesignTimeDbContextFactory.cs            LocalDB factory for `dotnet ef`
    Caching/CacheReadResult.cs                tri-state read result (Hit|Miss|Error)
    Caching/CacheUpsert.cs                    upsert input record
    Caching/CachePresence.cs                  batch probe row (key + byte length + expiry)
    Caching/ICacheStore.cs                    store abstraction
    Caching/SqlExceptionClassifier.cs         transient-vs-permanent SqlException numbers
    Caching/SqlServerCacheStore.cs            MERGE(HOLDLOCK) upsert, tri-state read, extend-ttl, batch
    Locking/ISeedLock.cs                      single-writer lock abstraction (+ handle)
    Locking/SqlServerSeedLock.cs              dedicated-connection sp_getapplock (session scope)
    Locking/InMemorySeedLock.cs               deterministic fake for unit tests
    Migrations/                               EF migration: CacheEntries
  test/WorldMonitor.Data.Tests/
    WorldMonitor.Data.Tests.csproj
    Unit/SqlExceptionClassifierTests.cs
    Unit/InMemorySeedLockTests.cs
    Integration/LocalDbFixture.cs             collection fixture: one migrated LocalDB, GUID keys, drop on dispose
    Integration/CacheStoreTests.cs            upsert/read/expiry/extend-ttl/oversize/batch
    Integration/CacheStoreConcurrencyTests.cs concurrent MERGE same key → no PK violation
    Integration/SeedLockTests.cs              contention→skip, release→reacquire, real-error→Error
```

---

### Task 1: Scaffold `WorldMonitor.Data` + test project

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/WorldMonitor.Data.csproj`
- Create: `dotnet/test/WorldMonitor.Data.Tests/WorldMonitor.Data.Tests.csproj`

- [ ] **Step 1: Branch** (stacked on P0)

Run:
```bash
git checkout feat/dotnet-p0-contracts && git checkout -b feat/dotnet-p1a-cache-substrate
```
Expected: `Switched to a new branch 'feat/dotnet-p1a-cache-substrate'`

- [ ] **Step 2: Create projects, references, packages**

Run:
```bash
dotnet new classlib -n WorldMonitor.Data -o dotnet/src/WorldMonitor.Data -f net10.0
dotnet new xunit -n WorldMonitor.Data.Tests -o dotnet/test/WorldMonitor.Data.Tests -f net10.0
rm dotnet/src/WorldMonitor.Data/Class1.cs dotnet/test/WorldMonitor.Data.Tests/UnitTest1.cs
dotnet sln dotnet/WorldMonitor.slnx add dotnet/src/WorldMonitor.Data dotnet/test/WorldMonitor.Data.Tests
dotnet add dotnet/src/WorldMonitor.Data reference dotnet/src/WorldMonitor.Contracts
dotnet add dotnet/test/WorldMonitor.Data.Tests reference dotnet/src/WorldMonitor.Data
dotnet add dotnet/src/WorldMonitor.Data package Microsoft.EntityFrameworkCore.SqlServer
dotnet add dotnet/src/WorldMonitor.Data package Microsoft.EntityFrameworkCore.Design
```
Expected: all succeed. (`Microsoft.Data.SqlClient` arrives transitively via the SqlServer provider.)

- [ ] **Step 3: Build empty**

Run: `dotnet build dotnet/WorldMonitor.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add dotnet/
git commit -m "chore(data): scaffold WorldMonitor.Data + test project"
```

---

### Task 2: `IClock` time seam

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Time/IClock.cs`
- Create: `dotnet/src/WorldMonitor.Data/Time/SystemClock.cs`
- Test: covered indirectly; no dedicated test (trivial).

- [ ] **Step 1: Implement**

Create `Time/IClock.cs`:
```csharp
namespace WorldMonitor.Data.Time;

/// <summary>Abstracts "now" so freshness logic is deterministic in tests.
/// NOTE: SQL-side timestamps use SYSUTCDATETIME() (server time) to avoid app/DB clock skew;
/// IClock is for app-side logic (P1a-2 wrapper/classifier).</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
```

Create `Time/SystemClock.cs`:
```csharp
namespace WorldMonitor.Data.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build dotnet/src/WorldMonitor.Data`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add dotnet/
git commit -m "feat(data): add IClock time seam"
```

---

### Task 3: `CacheEntry` entity + DbContext + initial migration

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Entities/CacheEntry.cs`
- Create: `dotnet/src/WorldMonitor.Data/Configurations/CacheEntryConfiguration.cs`
- Create: `dotnet/src/WorldMonitor.Data/WorldMonitorDbContext.cs`
- Create: `dotnet/src/WorldMonitor.Data/DesignTimeDbContextFactory.cs`
- Create: `dotnet/src/WorldMonitor.Data/Migrations/` (generated)

- [ ] **Step 1: Entity**

Create `Entities/CacheEntry.cs`:
```csharp
namespace WorldMonitor.Data.Entities;

/// <summary>One cached object — the Redis substrate replacement. Payload + freshness columns
/// live on a single row written in one transaction, eliminating the legacy dual-write
/// (value key + seed-meta key) partial-failure window.</summary>
public sealed class CacheEntry
{
    public required string CacheKey { get; set; }      // final key (prefixing is the caller's concern — see P2)
    public required string Value { get; set; }         // JSON payload or the negative sentinel (P1a-2)
    public long ByteLength { get; private set; }        // computed: CAST(DATALENGTH(Value) AS bigint)
    public DateTime ExpiresAtUtc { get; set; }          // data-liveness flag AND eviction clock
    public DateTime? FetchedAt { get; set; }            // freshness; null ⇒ treated stale
    public int? RecordCount { get; set; }
    public string? State { get; set; }                  // OK|OK_ZERO|RETRY|ERROR
    public string? SourceVersion { get; set; }
    public DateTime? NewestItemAt { get; set; }
    public int? MaxContentAgeMin { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Fluent configuration**

Create `Configurations/CacheEntryConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities;

namespace WorldMonitor.Data.Configurations;

public sealed class CacheEntryConfiguration : IEntityTypeConfiguration<CacheEntry>
{
    public void Configure(EntityTypeBuilder<CacheEntry> b)
    {
        b.ToTable("CacheEntries");
        b.HasKey(e => e.CacheKey);
        b.Property(e => e.CacheKey).HasMaxLength(512);
        b.Property(e => e.Value).HasColumnType("nvarchar(max)");
        b.Property(e => e.ByteLength)
            .HasComputedColumnSql("CAST(DATALENGTH([Value]) AS bigint)", stored: true);
        b.Property(e => e.State).HasMaxLength(16);
        b.Property(e => e.SourceVersion).HasMaxLength(64);
        // Filtered index supports freshness scans without touching the blob.
        b.HasIndex(e => e.FetchedAt).HasDatabaseName("IX_CacheEntries_FetchedAt");
    }
}
```

- [ ] **Step 3: DbContext**

Create `WorldMonitorDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities;

namespace WorldMonitor.Data;

public class WorldMonitorDbContext(DbContextOptions<WorldMonitorDbContext> options) : DbContext(options)
{
    public DbSet<CacheEntry> CacheEntries => Set<CacheEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorldMonitorDbContext).Assembly);
    }
}
```

- [ ] **Step 4: Design-time factory** (so `dotnet ef` can build migrations)

Create `DesignTimeDbContextFactory.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorldMonitor.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WorldMonitorDbContext>
{
    public WorldMonitorDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("WORLDMONITOR_DB")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=WorldMonitorDesign;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<WorldMonitorDbContext>().UseSqlServer(cs).Options;
        return new WorldMonitorDbContext(options);
    }
}
```

- [ ] **Step 5: Create the migration, verify it builds**

Run:
```bash
dotnet ef migrations add InitialCache --project dotnet/src/WorldMonitor.Data --startup-project dotnet/src/WorldMonitor.Data
dotnet build dotnet/src/WorldMonitor.Data
```
Expected: migration files appear under `Migrations/`; build succeeds. Open the generated migration and confirm the `CacheEntries` table includes `ByteLength` as a computed column (`computedColumnSql: "CAST(DATALENGTH([Value]) AS bigint)"`, `stored: true`) and the `IX_CacheEntries_FetchedAt` index.

- [ ] **Step 6: Commit**

```bash
git add dotnet/
git commit -m "feat(data): add CacheEntry entity, DbContext, and InitialCache migration"
```

---

### Task 4: `SqlExceptionClassifier` (transient vs permanent) — unit-tested

SQL upsert retries only transient errors; permanent errors (constraint, permission) must surface. This classifier is pure and unit-testable without a DB.

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Caching/SqlExceptionClassifier.cs`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Unit/SqlExceptionClassifierTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Unit/SqlExceptionClassifierTests.cs`:
```csharp
using WorldMonitor.Data.Caching;
using Xunit;

namespace WorldMonitor.Data.Tests.Unit;

public class SqlExceptionClassifierTests
{
    [Theory]
    [InlineData(-2, true)]    // timeout
    [InlineData(1205, true)]  // deadlock victim
    [InlineData(49918, true)] // not enough resources / throttling
    [InlineData(4060, true)]  // cannot open database (transient on Azure/contended)
    public void Transient_numbers_are_retryable(int number, bool expected)
        => Assert.Equal(expected, SqlExceptionClassifier.IsTransient(number));

    [Theory]
    [InlineData(2627)]  // unique constraint violation
    [InlineData(547)]   // FK/check constraint
    [InlineData(229)]   // permission denied
    public void Permanent_numbers_are_not_retryable(int number)
        => Assert.False(SqlExceptionClassifier.IsTransient(number));
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests`
Expected: FAIL — `SqlExceptionClassifier` does not exist.

- [ ] **Step 3: Implement**

Create `Caching/SqlExceptionClassifier.cs`:
```csharp
using System.Collections.Frozen;
using Microsoft.Data.SqlClient;

namespace WorldMonitor.Data.Caching;

/// <summary>Classifies SQL Server error numbers as transient (retry) vs permanent (surface).
/// Mirrors the legacy nonRetryable / PERMANENT_4XX distinction.</summary>
public static class SqlExceptionClassifier
{
    private static readonly FrozenSet<int> Transient = new[]
    {
        -2,     // command timeout
        1205,   // deadlock victim
        1222,   // lock request timeout
        49918,  // cannot process request — not enough resources
        49919,  // too many operations
        49920,  // too busy
        4060,   // cannot open database (contended)
        40197, 40501, 40613, 10928, 10929, 10053, 10054, 10060, 233, 64, // connectivity/throttle
    }.ToFrozenSet();

    public static bool IsTransient(int number) => Transient.Contains(number);

    /// <summary>True if any error in the SqlException is transient.</summary>
    public static bool IsTransient(SqlException ex)
    {
        foreach (SqlError e in ex.Errors)
            if (Transient.Contains(e.Number)) return true;
        return false;
    }
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests`
Expected: PASS (7 passed).

- [ ] **Step 5: Commit**

```bash
git add dotnet/
git commit -m "feat(data): add SqlExceptionClassifier (transient vs permanent)"
```

---

### Task 5: `ICacheStore` abstraction + result types

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Caching/CacheReadResult.cs`
- Create: `dotnet/src/WorldMonitor.Data/Caching/CacheUpsert.cs`
- Create: `dotnet/src/WorldMonitor.Data/Caching/CachePresence.cs`
- Create: `dotnet/src/WorldMonitor.Data/Caching/ICacheStore.cs`

- [ ] **Step 1: Implement the contracts** (no test — pure declarations exercised in Task 6+)

Create `Caching/CacheReadResult.cs`:
```csharp
namespace WorldMonitor.Data.Caching;

public enum CacheReadStatus
{
    Hit,    // live row found
    Miss,   // no live row (absent or expired) — caller runs the factory
    Error,  // store failure (SqlException) — caller serves last-good (fail-safe), NEVER treated as Miss
}

public sealed record CacheReadResult(
    CacheReadStatus Status,
    string? Value,
    DateTime? ExpiresAtUtc,
    DateTime? FetchedAt,
    int? RecordCount)
{
    public static readonly CacheReadResult Miss = new(CacheReadStatus.Miss, null, null, null, null);
    public static CacheReadResult Error { get; } = new(CacheReadStatus.Error, null, null, null, null);
    public static CacheReadResult Hit(string value, DateTime exp, DateTime? fetched, int? rc)
        => new(CacheReadStatus.Hit, value, exp, fetched, rc);
}
```

Create `Caching/CacheUpsert.cs`:
```csharp
namespace WorldMonitor.Data.Caching;

/// <summary>Input for an atomic upsert. TTL is server-relative (ExpiresAtUtc computed as
/// SYSUTCDATETIME()+Ttl) to avoid app/DB clock skew. FetchedAt defaults to server-now when null.</summary>
public sealed record CacheUpsert(
    string Key,
    string Value,
    TimeSpan Ttl,
    DateTime? FetchedAt = null,
    int? RecordCount = null,
    string? State = null,
    string? SourceVersion = null,
    DateTime? NewestItemAt = null,
    int? MaxContentAgeMin = null);
```

Create `Caching/CachePresence.cs`:
```csharp
namespace WorldMonitor.Data.Caching;

/// <summary>Batch presence/size probe (the STRLEN analog) — lets health check size without loading blobs.</summary>
public sealed record CachePresence(string Key, long ByteLength, DateTime ExpiresAtUtc);
```

Create `Caching/ICacheStore.cs`:
```csharp
namespace WorldMonitor.Data.Caching;

public interface ICacheStore
{
    /// <summary>Tri-state read of a single live entry. Expired/absent ⇒ Miss; store failure ⇒ Error.</summary>
    Task<CacheReadResult> ReadAsync(string key, CancellationToken ct = default);

    /// <summary>Atomic, concurrency-safe upsert (MERGE WITH HOLDLOCK). Retries transient errors;
    /// rejects payloads over <paramref name="maxBytes"/>. Permanent errors throw.</summary>
    Task UpsertAsync(CacheUpsert entry, CancellationToken ct = default);

    /// <summary>Extends a LIVE entry's TTL without touching FetchedAt (last-good preservation on
    /// upstream failure). Returns false when no live row exists (cannot resurrect an expired entry).</summary>
    Task<bool> ExtendTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Batch read of live entries (bootstrap). Expired rows are omitted.</summary>
    Task<IReadOnlyDictionary<string, string>> ReadManyAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);

    /// <summary>Batch presence/size probe (health). Expired rows are omitted.</summary>
    Task<IReadOnlyList<CachePresence>> ProbeAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build dotnet/src/WorldMonitor.Data`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add dotnet/
git commit -m "feat(data): add ICacheStore abstraction + tri-state result types"
```

---

### Task 6: `SqlServerCacheStore` — read + atomic upsert (HOLDLOCK)

The store uses the `WorldMonitorDbContext` connection for raw commands. **`MERGE` carries `WITH (HOLDLOCK)`** so concurrent inserts of the same key serialize (without it, two `NOT MATCHED` branches race to a PK violation — Task 7 proves this).

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Caching/SqlServerCacheStore.cs`
- Create: `dotnet/test/WorldMonitor.Data.Tests/Integration/LocalDbFixture.cs`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Integration/CacheStoreTests.cs`

- [ ] **Step 1: LocalDB collection fixture**

Add the SqlClient package to the test project for forcing errors later:
```bash
dotnet add dotnet/test/WorldMonitor.Data.Tests package Microsoft.Data.SqlClient
```

Create `Integration/LocalDbFixture.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

/// <summary>One migrated LocalDB database for the whole integration collection.
/// Tests use unique (GUID) cache keys so they don't collide. Database is dropped on dispose.</summary>
public sealed class LocalDbFixture : IAsyncLifetime
{
    public string ConnectionString { get; } =
        @"Server=(localdb)\MSSQLLocalDB;Database=WorldMonitorCacheTests;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

    public WorldMonitorDbContext NewContext()
        => new(new DbContextOptionsBuilder<WorldMonitorDbContext>().UseSqlServer(ConnectionString).Options);

    public async ValueTask InitializeAsync()
    {
        await using var ctx = NewContext();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.MigrateAsync(); // proves migrations apply to a real SQL Server engine
    }

    public async ValueTask DisposeAsync()
    {
        await using var ctx = NewContext();
        await ctx.Database.EnsureDeletedAsync();
    }
}

[CollectionDefinition("LocalDb")]
public sealed class LocalDbCollection : ICollectionFixture<LocalDbFixture>;
```

- [ ] **Step 2: Write the failing test**

Create `Integration/CacheStoreTests.cs`:
```csharp
using WorldMonitor.Data.Caching;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class CacheStoreTests(LocalDbFixture fx)
{
    private ICacheStore NewStore() => new SqlServerCacheStore(fx.NewContext());
    private static string Key() => "test:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Upsert_then_read_returns_hit_with_value()
    {
        var store = NewStore();
        var key = Key();
        await store.UpsertAsync(new CacheUpsert(key, "{\"v\":1}", TimeSpan.FromMinutes(5), RecordCount: 3));

        var r = await store.ReadAsync(key);

        Assert.Equal(CacheReadStatus.Hit, r.Status);
        Assert.Equal("{\"v\":1}", r.Value);
        Assert.Equal(3, r.RecordCount);
    }

    [Fact]
    public async Task Read_missing_key_returns_miss()
        => Assert.Equal(CacheReadStatus.Miss, (await NewStore().ReadAsync(Key())).Status);

    [Fact]
    public async Task Expired_entry_reads_as_miss()
    {
        var store = NewStore();
        var key = Key();
        await store.UpsertAsync(new CacheUpsert(key, "x", TimeSpan.FromSeconds(-1))); // already expired
        Assert.Equal(CacheReadStatus.Miss, (await store.ReadAsync(key)).Status);
    }

    [Fact]
    public async Task Upsert_overwrites_existing_value()
    {
        var store = NewStore();
        var key = Key();
        await store.UpsertAsync(new CacheUpsert(key, "a", TimeSpan.FromMinutes(5)));
        await store.UpsertAsync(new CacheUpsert(key, "b", TimeSpan.FromMinutes(5)));
        Assert.Equal("b", (await store.ReadAsync(key)).Value);
    }

    [Fact]
    public async Task Oversized_payload_is_rejected()
    {
        var store = NewStore();
        var big = new string('x', 6 * 1024 * 1024); // 6 MB > 5 MB cap
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.UpsertAsync(new CacheUpsert(Key(), big, TimeSpan.FromMinutes(1))));
    }
}
```

- [ ] **Step 3: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: FAIL — `SqlServerCacheStore` does not exist.

- [ ] **Step 4: Implement read + upsert**

Create `Caching/SqlServerCacheStore.cs`:
```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data;

namespace WorldMonitor.Data.Caching;

public sealed class SqlServerCacheStore(WorldMonitorDbContext db) : ICacheStore
{
    private const int MaxBytes = 5 * 1024 * 1024;
    private const int MaxRetries = 2;

    private SqlConnection Conn => (SqlConnection)db.Database.GetDbConnection();

    private async Task<SqlCommand> CommandAsync(string sql, CancellationToken ct)
    {
        if (Conn.State != System.Data.ConnectionState.Open) await Conn.OpenAsync(ct);
        var cmd = Conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    public async Task<CacheReadResult> ReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await using var cmd = await CommandAsync(
                "SELECT Value, ExpiresAtUtc, FetchedAt, RecordCount FROM CacheEntries " +
                "WHERE CacheKey = @k AND ExpiresAtUtc > SYSUTCDATETIME();", ct);
            cmd.Parameters.Add(new SqlParameter("@k", key));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return CacheReadResult.Miss;
            return CacheReadResult.Hit(
                r.GetString(0), r.GetDateTime(1),
                r.IsDBNull(2) ? null : r.GetDateTime(2),
                r.IsDBNull(3) ? null : r.GetInt32(3));
        }
        catch (SqlException)
        {
            return CacheReadResult.Error; // tri-state: store failure is NEVER a miss
        }
    }

    public async Task UpsertAsync(CacheUpsert e, CancellationToken ct = default)
    {
        var bytes = System.Text.Encoding.Unicode.GetByteCount(e.Value);
        if (bytes > MaxBytes)
            throw new ArgumentOutOfRangeException(nameof(e), $"payload {bytes}B exceeds {MaxBytes}B cap");

        const string sql =
            "MERGE CacheEntries WITH (HOLDLOCK) AS t " +
            "USING (VALUES(@k)) AS s(CacheKey) ON t.CacheKey = s.CacheKey " +
            "WHEN MATCHED THEN UPDATE SET Value=@v, ExpiresAtUtc=DATEADD(second,@ttl,SYSUTCDATETIME()), " +
            "  FetchedAt=COALESCE(@fetched,SYSUTCDATETIME()), RecordCount=@rc, State=@state, " +
            "  SourceVersion=@sv, NewestItemAt=@nia, MaxContentAgeMin=@mca, UpdatedAt=SYSUTCDATETIME() " +
            "WHEN NOT MATCHED THEN INSERT (CacheKey,Value,ExpiresAtUtc,FetchedAt,RecordCount,State,SourceVersion,NewestItemAt,MaxContentAgeMin,UpdatedAt) " +
            "  VALUES (@k,@v,DATEADD(second,@ttl,SYSUTCDATETIME()),COALESCE(@fetched,SYSUTCDATETIME()),@rc,@state,@sv,@nia,@mca,SYSUTCDATETIME());";

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var cmd = await CommandAsync(sql, ct);
                cmd.Parameters.Add(new SqlParameter("@k", e.Key));
                cmd.Parameters.Add(new SqlParameter("@v", e.Value));
                cmd.Parameters.Add(new SqlParameter("@ttl", (long)e.Ttl.TotalSeconds));
                cmd.Parameters.Add(new SqlParameter("@fetched", (object?)e.FetchedAt ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@rc", (object?)e.RecordCount ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@state", (object?)e.State ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@sv", (object?)e.SourceVersion ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@nia", (object?)e.NewestItemAt ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@mca", (object?)e.MaxContentAgeMin ?? DBNull.Value));
                await cmd.ExecuteNonQueryAsync(ct);
                return;
            }
            catch (SqlException ex) when (attempt < MaxRetries && SqlExceptionClassifier.IsTransient(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }
    }

    public async Task<bool> ExtendTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(
            "UPDATE CacheEntries SET ExpiresAtUtc = DATEADD(second,@ttl,SYSUTCDATETIME()) " +
            "WHERE CacheKey = @k AND ExpiresAtUtc > SYSUTCDATETIME();", ct);
        cmd.Parameters.Add(new SqlParameter("@ttl", (long)ttl.TotalSeconds));
        cmd.Parameters.Add(new SqlParameter("@k", key));
        return await cmd.ExecuteNonQueryAsync(ct) > 0; // @@ROWCOUNT == 0 ⇒ no live row to extend
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadManyAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>();
        if (keys.Count == 0) return result;
        var (inClause, ps) = BuildInList(keys);
        await using var cmd = await CommandAsync(
            $"SELECT CacheKey, Value FROM CacheEntries WHERE ExpiresAtUtc > SYSUTCDATETIME() AND CacheKey IN ({inClause});", ct);
        cmd.Parameters.AddRange(ps);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result[r.GetString(0)] = r.GetString(1);
        return result;
    }

    public async Task<IReadOnlyList<CachePresence>> ProbeAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        var result = new List<CachePresence>();
        if (keys.Count == 0) return result;
        var (inClause, ps) = BuildInList(keys);
        await using var cmd = await CommandAsync(
            $"SELECT CacheKey, ByteLength, ExpiresAtUtc FROM CacheEntries WHERE ExpiresAtUtc > SYSUTCDATETIME() AND CacheKey IN ({inClause});", ct);
        cmd.Parameters.AddRange(ps);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(new CachePresence(r.GetString(0), r.GetInt64(1), r.GetDateTime(2)));
        return result;
    }

    private static (string, SqlParameter[]) BuildInList(IReadOnlyCollection<string> keys)
    {
        var ps = keys.Select((k, i) => new SqlParameter($"@k{i}", k)).ToArray();
        var inClause = string.Join(",", ps.Select(p => p.ParameterName));
        return (inClause, ps);
    }
}
```

- [ ] **Step 5: Run, verify pass**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: PASS (5 integration tests). If LocalDB is slow on first connect, re-run.

- [ ] **Step 6: Commit**

```bash
git add dotnet/
git commit -m "feat(data): SqlServerCacheStore read + atomic MERGE(HOLDLOCK) upsert + batch ops"
```

---

### Task 7: Concurrency + extend-TTL behavior (the false-green guards)

Proves the `HOLDLOCK` actually serializes concurrent inserts, and that `ExtendTtl` cannot resurrect an expired entry.

**Files:**
- Test: `dotnet/test/WorldMonitor.Data.Tests/Integration/CacheStoreConcurrencyTests.cs`

- [ ] **Step 1: Write the tests**

Create `Integration/CacheStoreConcurrencyTests.cs`:
```csharp
using WorldMonitor.Data.Caching;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class CacheStoreConcurrencyTests(LocalDbFixture fx)
{
    private static string Key() => "conc:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Concurrent_upserts_of_same_new_key_do_not_throw_pk_violation()
    {
        var key = Key();
        // Each store gets its OWN context/connection ⇒ real concurrent writers.
        var tasks = Enumerable.Range(0, 16).Select(i =>
            new SqlServerCacheStore(fx.NewContext())
                .UpsertAsync(new CacheUpsert(key, $"v{i}", TimeSpan.FromMinutes(5))));

        await Task.WhenAll(tasks); // must NOT throw (HOLDLOCK serializes NOT MATCHED)

        var r = await new SqlServerCacheStore(fx.NewContext()).ReadAsync(key);
        Assert.Equal(CacheReadStatus.Hit, r.Status); // exactly one surviving row
    }

    [Fact]
    public async Task ExtendTtl_returns_true_for_live_entry_and_pushes_expiry()
    {
        var store = new SqlServerCacheStore(fx.NewContext());
        var key = Key();
        await store.UpsertAsync(new CacheUpsert(key, "x", TimeSpan.FromSeconds(2)));
        Assert.True(await store.ExtendTtlAsync(key, TimeSpan.FromMinutes(10)));
        Assert.Equal(CacheReadStatus.Hit, (await store.ReadAsync(key)).Status);
    }

    [Fact]
    public async Task ExtendTtl_returns_false_and_does_not_resurrect_expired_entry()
    {
        var store = new SqlServerCacheStore(fx.NewContext());
        var key = Key();
        await store.UpsertAsync(new CacheUpsert(key, "x", TimeSpan.FromSeconds(-1))); // expired
        Assert.False(await store.ExtendTtlAsync(key, TimeSpan.FromMinutes(10)));       // cannot resurrect
        Assert.Equal(CacheReadStatus.Miss, (await store.ReadAsync(key)).Status);
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: PASS (8 integration tests total). The concurrency test is the one that would FAIL without `WITH (HOLDLOCK)` — if it ever flakes with a PK violation, the HOLDLOCK was dropped.

- [ ] **Step 3: Commit**

```bash
git add dotnet/
git commit -m "test(data): prove HOLDLOCK upsert concurrency + no-resurrect extend-ttl"
```

---

### Task 8: `ISeedLock` + `SqlServerSeedLock` (dedicated-connection app-lock) + fake

`sp_getapplock` must run on a **dedicated, non-pooled connection** owned by the lock handle for the lock's lifetime — a session-scoped applock on a pooled connection could leak to an unrelated operation that later reuses that physical connection. The handle owns and disposes its own `SqlConnection`.

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Locking/ISeedLock.cs`
- Create: `dotnet/src/WorldMonitor.Data/Locking/SqlServerSeedLock.cs`
- Create: `dotnet/src/WorldMonitor.Data/Locking/InMemorySeedLock.cs`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Unit/InMemorySeedLockTests.cs`, `dotnet/test/WorldMonitor.Data.Tests/Integration/SeedLockTests.cs`

- [ ] **Step 1: Abstraction**

Create `Locking/ISeedLock.cs`:
```csharp
namespace WorldMonitor.Data.Locking;

/// <summary>Single-writer lock for seed publishes. TryAcquire returns null immediately on
/// contention (acquire-or-skip, never blocks). Dispose the handle to release.</summary>
public interface ISeedLock
{
    Task<ISeedLockHandle?> TryAcquireAsync(string resource, CancellationToken ct = default);
}

public interface ISeedLockHandle : IAsyncDisposable
{
    string Resource { get; }
}
```

- [ ] **Step 2: In-memory fake + its unit test**

Create `Locking/InMemorySeedLock.cs`:
```csharp
using System.Collections.Concurrent;

namespace WorldMonitor.Data.Locking;

/// <summary>Process-local lock for deterministic unit tests. Acquire-or-skip semantics.</summary>
public sealed class InMemorySeedLock : ISeedLock
{
    private readonly ConcurrentDictionary<string, byte> _held = new();

    public Task<ISeedLockHandle?> TryAcquireAsync(string resource, CancellationToken ct = default)
        => Task.FromResult<ISeedLockHandle?>(
            _held.TryAdd(resource, 0) ? new Handle(this, resource) : null);

    private sealed class Handle(InMemorySeedLock owner, string resource) : ISeedLockHandle
    {
        public string Resource => resource;
        public ValueTask DisposeAsync() { owner._held.TryRemove(resource, out _); return ValueTask.CompletedTask; }
    }
}
```

Create `Unit/InMemorySeedLockTests.cs`:
```csharp
using WorldMonitor.Data.Locking;
using Xunit;

namespace WorldMonitor.Data.Tests.Unit;

public class InMemorySeedLockTests
{
    [Fact]
    public async Task Second_acquire_while_held_returns_null()
    {
        var sut = new InMemorySeedLock();
        await using var first = await sut.TryAcquireAsync("r");
        Assert.NotNull(first);
        Assert.Null(await sut.TryAcquireAsync("r"));
    }

    [Fact]
    public async Task Resource_can_be_reacquired_after_release()
    {
        var sut = new InMemorySeedLock();
        (await sut.TryAcquireAsync("r")).Should();           // acquire + dispose immediately
        await using var again = await sut.TryAcquireAsync("r");
        Assert.NotNull(again);
    }
}

file static class Ext
{
    // dispose the handle right away to model release-after-use
    public static void Should(this ISeedLockHandle? h) { Assert.NotNull(h); h!.DisposeAsync().AsTask().Wait(); }
}
```

- [ ] **Step 3: Run unit tests, verify pass**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category!=Integration`
Expected: PASS (includes the 2 new lock tests + classifier tests).

- [ ] **Step 4: SQL Server implementation**

Create `Locking/SqlServerSeedLock.cs`:
```csharp
using Microsoft.Data.SqlClient;

namespace WorldMonitor.Data.Locking;

/// <summary>sp_getapplock on a DEDICATED connection (not pooled-reused) so the session-scoped
/// lock is owned for exactly the handle's lifetime. @LockTimeout=0 ⇒ acquire-or-skip.</summary>
public sealed class SqlServerSeedLock(string connectionString) : ISeedLock
{
    public async Task<ISeedLockHandle?> TryAcquireAsync(string resource, CancellationToken ct = default)
    {
        // Pooling=false ⇒ this physical connection is never handed to another logical op.
        var csb = new SqlConnectionStringBuilder(connectionString) { Pooling = false };
        var conn = new SqlConnection(csb.ConnectionString);
        try
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource=@res, @LockMode='Exclusive', " +
                "@LockOwner='Session', @LockTimeout=0; SELECT @r;";
            cmd.Parameters.Add(new SqlParameter("@res", resource));
            var rc = (int)(await cmd.ExecuteScalarAsync(ct))!;
            if (rc < 0) { await conn.DisposeAsync(); return null; } // contention ⇒ skip
            return new Handle(conn, resource);
        }
        catch (SqlException)
        {
            await conn.DisposeAsync();
            return null; // store unreachable ⇒ skip this cycle (do not crash the seeder)
        }
    }

    private sealed class Handle(SqlConnection conn, string resource) : ISeedLockHandle
    {
        public string Resource => resource;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "EXEC sp_releaseapplock @Resource=@res, @LockOwner='Session';";
                cmd.Parameters.Add(new SqlParameter("@res", resource));
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException) { /* session drop already released it */ }
            finally { await conn.DisposeAsync(); }
        }
    }
}
```

- [ ] **Step 5: Integration test for the SQL lock + a real-error → Error read**

Create `Integration/SeedLockTests.cs`:
```csharp
using WorldMonitor.Data.Caching;
using WorldMonitor.Data.Locking;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class SeedLockTests(LocalDbFixture fx)
{
    private static string Res() => "lock:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Second_acquire_while_held_returns_null_then_succeeds_after_release()
    {
        var sut = new SqlServerSeedLock(fx.ConnectionString);
        var res = Res();
        var first = await sut.TryAcquireAsync(res);
        Assert.NotNull(first);
        Assert.Null(await sut.TryAcquireAsync(res));   // contention ⇒ skip
        await first!.DisposeAsync();
        await using var again = await sut.TryAcquireAsync(res);
        Assert.NotNull(again);                          // reacquire after release
    }

    [Fact]
    public async Task Read_against_unreachable_server_returns_Error_not_Miss()
    {
        // A bogus instance name forces a real SqlException on open ⇒ tri-state Error.
        var ctx = new WorldMonitorDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<WorldMonitorDbContext>()
                .UseSqlServer(@"Server=(localdb)\WM_DoesNotExist;Database=x;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=2")
                .Options);
        var store = new SqlServerCacheStore(ctx);
        Assert.Equal(CacheReadStatus.Error, (await store.ReadAsync("anything")).Status);
    }
}
```

- [ ] **Step 6: Run all tests**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests`
Expected: PASS (all unit + integration). Confirm the `Error_not_Miss` test proves a connectivity failure maps to `Error`.

- [ ] **Step 7: Commit**

```bash
git add dotnet/
git commit -m "feat(data): add ISeedLock + dedicated-connection SqlServerSeedLock + InMemory fake"
```

---

### Task 9: Green build, README, PR

**Files:**
- Modify: `dotnet/README.md`

- [ ] **Step 1: Update README**

Add a section to `dotnet/README.md` (after the existing layout list):
```markdown
- `src/WorldMonitor.Data` — EF Core data layer. P1a-1: `CacheEntries` substrate (`ICacheStore` /
  `SqlServerCacheStore`), single-writer `ISeedLock` (`sp_getapplock`), `IClock`. Integration tests
  run against SQL Server LocalDB.

## Database (dev/test)
Integration tests target LocalDB `(localdb)\MSSQLLocalDB` (no Docker). Apply migrations manually with:
```
dotnet ef database update --project dotnet/src/WorldMonitor.Data --startup-project dotnet/src/WorldMonitor.Data
```
Run only fast unit tests (no DB): `dotnet test --filter Category!=Integration`.
```

- [ ] **Step 2: Full clean build + test**

Run:
```bash
dotnet build dotnet/WorldMonitor.slnx -c Release
dotnet test dotnet/WorldMonitor.slnx -c Release
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all tests pass (P0's 12 + the new P1a-1 unit + integration tests). If a CI/dev box lacks LocalDB, `--filter Category!=Integration` must still be green on its own.

- [ ] **Step 3: Commit, push, open PR**

```bash
git add dotnet/
git commit -m "docs(data): document WorldMonitor.Data + LocalDB test workflow"
git push -u origin feat/dotnet-p1a-cache-substrate
gh pr create --base feat/dotnet-p0-contracts --title "P1a-1: SQL cache substrate (CacheEntries + store + app-lock)" --body "Implements P1a-1 of the rewrite: SQL Server CacheEntries substrate replacing Redis. Tri-state ICacheStore (HOLDLOCK MERGE upsert, no-resurrect extend-TTL, batch read/probe), dedicated-connection sp_getapplock ISeedLock, IClock. Integration-tested on SQL Server LocalDB (no Docker). Stacked on #1 (P0); retarget to main when P0 merges. FusionCache wrapper, domain entities, and ML state tables follow in P1a-2/P1b/P1c."
```
Expected: PR URL printed.

---

## Self-review

**Spec coverage (P1a-1 scope, from the P1 inventory):**
- ✅ `CacheEntries` table (payload + freshness columns, computed `ByteLength`) — Task 3.
- ✅ Atomic upsert with **`HOLDLOCK`** + concurrency proof — Tasks 6, 7 (addresses the critic's HIGH "MERGE not concurrency-safe / false-green" finding).
- ✅ Extend-TTL with no-resurrect (`WHERE ExpiresAtUtc > now`, `@@ROWCOUNT` semantics) — Tasks 6, 7.
- ✅ Tri-state read (Hit/Miss/Error); SqlException → `Error`, never `Miss` — Tasks 5, 6, 8 (real-error integration test).
- ✅ Batch read + presence/size probe — Task 6.
- ✅ Single-writer `sp_getapplock` on a **dedicated, non-pooled connection** (addresses the critic's session-scope/pooling HIGH) — Task 8.
- ✅ Transient-vs-permanent SQL error classification (unit-tested) — Task 4.
- ✅ LocalDB integration strategy without Docker — Task 6 fixture.
- **Deferred (documented in header):** FusionCache wrapper + storm-guard/fail-safe-vs-sentinel reconciliation → P1a-2; HTTP cache tiers/`TierResolver`/key-prefix/bootstrap-unprefixed → P2; domain entities → P1b; vector/correlation tables → P1c.

**Placeholder scan:** none — every code/command step is complete.

**Type consistency:** `ICacheStore` (Task 5) is implemented by `SqlServerCacheStore` (Task 6) and exercised in Tasks 6–8. `CacheReadStatus`/`CacheReadResult`/`CacheUpsert`/`CachePresence` names are used consistently. `ISeedLock`/`ISeedLockHandle` (Task 8) are implemented by both `SqlServerSeedLock` and `InMemorySeedLock`. `SqlExceptionClassifier.IsTransient` (Task 4) is called in `SqlServerCacheStore.UpsertAsync` (Task 6).

**Note for execution:** Integration tests require LocalDB `(localdb)\MSSQLLocalDB`. If a transient first-connect timeout occurs, re-run. The `[Trait("Category","Integration")]` split lets the unit ring stay green on machines without LocalDB.
```
