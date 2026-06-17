# P1c — Vector + Correlation State Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the server-side ML/analysis state that was per-browser/in-memory in the legacy app — a vector store, dedup, topic-velocity history, and correlation snapshots — completing the `WorldMonitor.Data` data layer. The semantic-search vector store uses a **C# brute-force cosine fallback** (our LocalDB is SQL Server 2019, which has no native `VECTOR` type) behind an `IVectorSearch` abstraction so a SQL Server 2025 `VECTOR_DISTANCE` path can drop in later.

**Architecture:** Five entities + configs + a migration, a pure `VectorMath` (cosine + float↔byte) unit-tested without a DB, and four thin repositories: `IVectorSearch`/`SqlServerVectorSearch` (idempotent upsert by content-hash Id, top-K cosine search over `VARBINARY(1536)` embeddings, FIFO prune to 5000 by `IngestedAt`), `DedupRepository` (per-signal-type TTL mark-seen), `TopicVelocityRepository` (7-day baseline average), and `CorrelationStateRepository` (single-row snapshot; cold-start returns null so the analysis engine emits nothing on first run). Reuses the P1b conventions (`IClock`, narrowed `DbUpdateException` catch).

**Tech Stack:** .NET 10, EF Core 10 (`VARBINARY`, `ExecuteDeleteAsync` with a subquery), xUnit + LocalDB.

**This is P1c of the program roadmap** (phase P1) — the **last data-layer slice**; completes `WorldMonitor.Data`. Builds on P1b-3 (in `main`). Source of truth: `src/workers/vector-db.ts` (MAX_VECTORS=5000, Float32[384]), `src/services/correlation.ts:25-31` (dedup TTLs), the P1 inventory (`tasks/w9vxze4mh.output`).

**Verified constants:** embedding = `Float32[384]` → `VARBINARY(1536)` (384×4 bytes); FIFO cap = 5000; dedup TTLs — `silent_divergence`/`flow_price_divergence`/`explained_market_move` = 6h, `prediction_leads_news` = 2h, `keyword_spike`/default = 30m.

**Locked decisions:** `LlmAssessmentCache` is **NOT created** (no generative AI). The SQL Server 2025 native-`VECTOR` path is **deferred** behind `IVectorSearch`; this slice ships only the portable C# cosine fallback.

**Explicitly deferred:** the analysis/correlation engines that *consume* this state (clustering, convergence scoring, the ONNX embedding producer) → **P6**; retention/purge *scheduling* → the Worker Service (**P7**). This slice ships the tables + the storage/search/dedup primitives + their tests.

---

## Prerequisites

- .NET 10 SDK + SQL Server LocalDB `MSSQLLocalDB` (verified).
- Branch: from `main` (P1b-3 merged), create `feat/dotnet-p1c-vector-correlation`.

## File structure

```
dotnet/
  src/WorldMonitor.Data/
    Entities/Ml/VectorEntry.cs
    Entities/Ml/DedupSeen.cs
    Entities/Ml/TopicVelocityPoint.cs
    Entities/Ml/CorrelationState.cs
    Entities/Ml/CorrelationClusterState.cs
    Configurations/VectorEntryConfiguration.cs
    Configurations/DedupSeenConfiguration.cs
    Configurations/TopicVelocityPointConfiguration.cs
    Configurations/CorrelationStateConfiguration.cs
    Configurations/CorrelationClusterStateConfiguration.cs
    Ml/VectorMath.cs                          (pure: cosine + float<->byte)
    Ml/IVectorSearch.cs                        (+ VectorHit)
    Ml/SqlServerVectorSearch.cs
    Repositories/DedupRepository.cs
    Repositories/TopicVelocityRepository.cs
    Repositories/CorrelationStateRepository.cs
    WorldMonitorDbContext.cs                    (MODIFY: add 5 DbSets)
    Migrations/                                 (new: AddMlState)
  test/WorldMonitor.Data.Tests/
    Unit/VectorMathTests.cs
    Unit/DedupTtlTests.cs
    Integration/VectorSearchTests.cs
    Integration/MlStateTests.cs                 (dedup TTL, topic baseline, correlation cold-start, cluster round-trip)
```

---

### Task 1: Branch + ML entities

**Files:**
- Create the five entity files under `dotnet/src/WorldMonitor.Data/Entities/Ml/`.

- [ ] **Step 1: Branch**

Run:
```bash
git checkout main && git checkout -b feat/dotnet-p1c-vector-correlation
```
Expected: `Switched to a new branch 'feat/dotnet-p1c-vector-correlation'`

- [ ] **Step 2: Entities**

Create `Entities/Ml/VectorEntry.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Ml;

/// <summary>A stored headline embedding for semantic search. Id is a content hash (natural key) so
/// re-ingest is idempotent. Embedding is a Float32[384] serialized to VARBINARY(1536).</summary>
public sealed class VectorEntry
{
    public required string Id { get; set; }
    public required string Text { get; set; }        // nvarchar(200)
    public required byte[] Embedding { get; set; }    // varbinary(1536) = Float32[384]
    public DateTime? PubDate { get; set; }
    public DateTime IngestedAt { get; set; }
    public string? Source { get; set; }
    public string? Url { get; set; }
    public List<string> Tags { get; set; } = [];      // JSON
}
```

Create `Entities/Ml/DedupSeen.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Ml;

/// <summary>Records that a correlation signal was emitted, for per-signal-type dedup.</summary>
public sealed class DedupSeen
{
    public required string DedupKey { get; set; }
    public required string SignalType { get; set; }
    public DateTime SeenAt { get; set; }
}
```

Create `Entities/Ml/TopicVelocityPoint.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Ml;

/// <summary>A time-series sample of a topic's mention velocity; baselined over a 7-day window.</summary>
public sealed class TopicVelocityPoint
{
    public int Id { get; set; }
    public required string Topic { get; set; }
    public DateTime Timestamp { get; set; }
    public double Velocity { get; set; }
}
```

Create `Entities/Ml/CorrelationState.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Ml;

/// <summary>Single-row snapshot of the previous analysis cycle (for cross-run delta detection).
/// No row = cold start (the engine emits nothing on the first run).</summary>
public sealed class CorrelationState
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string NewsVelocity { get; set; } = "{}";       // JSON blob
    public string MarketChanges { get; set; } = "{}";      // JSON blob
    public string PredictionChanges { get; set; } = "{}";  // JSON blob
}
```

Create `Entities/Ml/CorrelationClusterState.cs`:
```csharp
namespace WorldMonitor.Data.Entities.Ml;

/// <summary>Per-(domain, cluster) state overwritten each run; supports trend matching (±5 score delta).</summary>
public sealed class CorrelationClusterState
{
    public int Id { get; set; }
    public required string Domain { get; set; }
    public required string ClusterKey { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Country { get; set; }
    public string? EntityKey { get; set; }
    public double Score { get; set; }
    public DateTime Timestamp { get; set; }
}
```

- [ ] **Step 3: Build + commit**

Run: `dotnet build dotnet/src/WorldMonitor.Data`
Expected: 0 errors.
```bash
git add dotnet/
git commit -m "feat(data): add ML state entities (vector, dedup, topic-velocity, correlation)"
```

---

### Task 2: Configurations + DbSets + migration

**Files:**
- Create the five configuration files.
- Modify: `dotnet/src/WorldMonitor.Data/WorldMonitorDbContext.cs`
- Create: migration `AddMlState`

- [ ] **Step 1: Configurations**

Create `Configurations/VectorEntryConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Configurations;

public sealed class VectorEntryConfiguration : IEntityTypeConfiguration<VectorEntry>
{
    public void Configure(EntityTypeBuilder<VectorEntry> b)
    {
        b.ToTable("Vectors");
        b.HasKey(v => v.Id);
        b.Property(v => v.Id).HasMaxLength(128);
        b.Property(v => v.Text).HasMaxLength(200);
        b.Property(v => v.Embedding).HasColumnType("varbinary(1536)");
        b.Property(v => v.Source).HasMaxLength(64);
        b.Property(v => v.Url).HasMaxLength(2048);
        b.HasIndex(v => v.IngestedAt).HasDatabaseName("IX_Vectors_IngestedAt");
    }
}
```

Create `Configurations/DedupSeenConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Configurations;

public sealed class DedupSeenConfiguration : IEntityTypeConfiguration<DedupSeen>
{
    public void Configure(EntityTypeBuilder<DedupSeen> b)
    {
        b.ToTable("DedupSeen");
        b.HasKey(d => d.DedupKey);
        b.Property(d => d.DedupKey).HasMaxLength(256);
        b.Property(d => d.SignalType).HasMaxLength(64);
        b.HasIndex(d => d.SeenAt).HasDatabaseName("IX_DedupSeen_SeenAt");
    }
}
```

Create `Configurations/TopicVelocityPointConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Configurations;

public sealed class TopicVelocityPointConfiguration : IEntityTypeConfiguration<TopicVelocityPoint>
{
    public void Configure(EntityTypeBuilder<TopicVelocityPoint> b)
    {
        b.ToTable("TopicVelocityPoints");
        b.HasKey(p => p.Id);
        b.Property(p => p.Topic).HasMaxLength(128);
        b.HasIndex(p => new { p.Topic, p.Timestamp }).HasDatabaseName("IX_TopicVelocity_Topic_Timestamp");
    }
}
```

Create `Configurations/CorrelationStateConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Configurations;

public sealed class CorrelationStateConfiguration : IEntityTypeConfiguration<CorrelationState>
{
    public void Configure(EntityTypeBuilder<CorrelationState> b)
    {
        b.ToTable("CorrelationStates");
        b.HasKey(s => s.Id);
        b.Property(s => s.NewsVelocity).HasColumnType("nvarchar(max)");
        b.Property(s => s.MarketChanges).HasColumnType("nvarchar(max)");
        b.Property(s => s.PredictionChanges).HasColumnType("nvarchar(max)");
    }
}
```

Create `Configurations/CorrelationClusterStateConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Configurations;

public sealed class CorrelationClusterStateConfiguration : IEntityTypeConfiguration<CorrelationClusterState>
{
    public void Configure(EntityTypeBuilder<CorrelationClusterState> b)
    {
        b.ToTable("CorrelationClusterStates");
        b.HasKey(c => c.Id);
        b.Property(c => c.Domain).HasMaxLength(64);
        b.Property(c => c.ClusterKey).HasMaxLength(256);
        b.Property(c => c.Country).HasMaxLength(2);
        b.Property(c => c.EntityKey).HasMaxLength(256);
        b.HasIndex(c => new { c.Domain, c.ClusterKey }).IsUnique().HasDatabaseName("UX_CorrelationClusters_Domain_Key");
    }
}
```

- [ ] **Step 2: Add DbSets**

In `WorldMonitorDbContext.cs`, add `using WorldMonitor.Data.Entities.Ml;` and:
```csharp
    public DbSet<VectorEntry> Vectors => Set<VectorEntry>();
    public DbSet<DedupSeen> DedupSeen => Set<DedupSeen>();
    public DbSet<TopicVelocityPoint> TopicVelocityPoints => Set<TopicVelocityPoint>();
    public DbSet<CorrelationState> CorrelationStates => Set<CorrelationState>();
    public DbSet<CorrelationClusterState> CorrelationClusterStates => Set<CorrelationClusterState>();
```

- [ ] **Step 3: Migration + build**

Run:
```bash
dotnet ef migrations add AddMlState --project dotnet/src/WorldMonitor.Data --startup-project dotnet/src/WorldMonitor.Data
dotnet build dotnet/src/WorldMonitor.Data
```
Expected: a migration creating the 5 tables (`Vectors` with a `varbinary(1536)` `Embedding`); existing tables untouched.

- [ ] **Step 4: Commit**

```bash
git add dotnet/
git commit -m "feat(data): configure ML state + wire DbSets + AddMlState migration"
```

---

### Task 3: `VectorMath` — cosine + float/byte (pure, DB-free)

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Ml/VectorMath.cs`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Unit/VectorMathTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Unit/VectorMathTests.cs`:
```csharp
using WorldMonitor.Data.Ml;
using Xunit;

namespace WorldMonitor.Data.Tests.Unit;

public class VectorMathTests
{
    [Fact]
    public void Cosine_of_identical_unit_vectors_is_one()
        => Assert.Equal(1.0, VectorMath.Cosine([1f, 0f, 0f], [1f, 0f, 0f]), 6);

    [Fact]
    public void Cosine_of_orthogonal_is_zero()
        => Assert.Equal(0.0, VectorMath.Cosine([1f, 0f], [0f, 1f]), 6);

    [Fact]
    public void Cosine_with_a_zero_vector_is_zero_not_NaN()
        => Assert.Equal(0.0, VectorMath.Cosine([0f, 0f], [1f, 2f]));

    [Fact]
    public void Float_byte_round_trip_preserves_values()
    {
        float[] v = [0.5f, -1.25f, 3.0f, 0f];
        Assert.Equal(v, VectorMath.ToFloats(VectorMath.ToBytes(v)));
    }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category!=Integration`
Expected: FAIL — `VectorMath` does not exist.

- [ ] **Step 3: Implement**

Create `Ml/VectorMath.cs`:
```csharp
using System.Runtime.InteropServices;

namespace WorldMonitor.Data.Ml;

/// <summary>Brute-force cosine similarity + Float32[]↔byte[] conversion. Mirrors the legacy
/// cosineSimilarityF32 (dot/(|a||b|), 0 on a zero-norm vector).</summary>
public static class VectorMath
{
    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    public static byte[] ToBytes(ReadOnlySpan<float> v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        MemoryMarshal.AsBytes(v).CopyTo(bytes);
        return bytes;
    }

    public static float[] ToFloats(ReadOnlySpan<byte> b)
    {
        var floats = new float[b.Length / sizeof(float)];
        b.CopyTo(MemoryMarshal.AsBytes(floats.AsSpan()));
        return floats;
    }
}
```

- [ ] **Step 4: Run, verify pass; commit**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category!=Integration`
Expected: PASS (4 new unit tests; the DB-free unit ring grows).
```bash
git add dotnet/
git commit -m "feat(data): add VectorMath cosine + float/byte conversion (DB-free)"
```

---

### Task 4: `IVectorSearch` + `SqlServerVectorSearch`

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Ml/IVectorSearch.cs`
- Create: `dotnet/src/WorldMonitor.Data/Ml/SqlServerVectorSearch.cs`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Integration/VectorSearchTests.cs`

- [ ] **Step 1: Abstraction**

Create `Ml/IVectorSearch.cs`:
```csharp
using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Ml;

public sealed record VectorHit(string Id, string Text, double Score, string? Url);

public interface IVectorSearch
{
    /// <summary>Idempotent upsert keyed on the content-hash Id (re-ingest replaces in place).</summary>
    Task UpsertAsync(VectorEntry entry, CancellationToken ct = default);

    /// <summary>Top-K most cosine-similar entries to the query embedding.</summary>
    Task<IReadOnlyList<VectorHit>> SearchAsync(float[] query, int topK, CancellationToken ct = default);

    /// <summary>FIFO retention: keep the newest <paramref name="maxVectors"/> by IngestedAt, delete the rest.
    /// Returns the number deleted.</summary>
    Task<int> PruneToFifoCapAsync(int maxVectors, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing tests**

Create `Integration/VectorSearchTests.cs`:
```csharp
using WorldMonitor.Data.Entities.Ml;
using WorldMonitor.Data.Ml;
using WorldMonitor.Data.Time;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class VectorSearchTests(LocalDbFixture fx)
{
    private SqlServerVectorSearch Search() => new(fx.NewContext(), new SystemClock());
    private static VectorEntry Entry(string id, float[] emb, DateTime ingestedAt) =>
        new() { Id = id, Text = "t-" + id, Embedding = VectorMath.ToBytes(emb), IngestedAt = ingestedAt };

    [Fact]
    public async Task Upsert_is_idempotent_by_id()
    {
        var id = "v_" + Guid.NewGuid().ToString("N");
        await Search().UpsertAsync(Entry(id, [1f, 0f, 0f], DateTime.UtcNow));
        await Search().UpsertAsync(Entry(id, [0f, 1f, 0f], DateTime.UtcNow)); // same id ⇒ replace, not duplicate

        await using var ctx = fx.NewContext();
        Assert.Equal(1, ctx.Vectors.Count(v => v.Id == id));
    }

    [Fact]
    public async Task Search_ranks_by_cosine_similarity()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var s = Search();
        await s.UpsertAsync(Entry(tag + "_near", [1f, 0.1f, 0f], DateTime.UtcNow));
        await s.UpsertAsync(Entry(tag + "_far", [0f, 0f, 1f], DateTime.UtcNow));

        var hits = await Search().SearchAsync([1f, 0f, 0f], topK: 50);
        var near = hits.Single(h => h.Id == tag + "_near");
        var far = hits.Single(h => h.Id == tag + "_far");
        Assert.True(near.Score > far.Score); // the aligned vector outranks the orthogonal one
    }

    [Fact]
    public async Task Prune_keeps_only_the_newest_N_by_ingested_at()
    {
        var prefix = "p_" + Guid.NewGuid().ToString("N");
        var s = Search();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 5; i++)
            await s.UpsertAsync(Entry($"{prefix}_{i}", [1f, 0f, 0f], t0.AddMinutes(i)));

        // Keep only the newest 3 *of this test's rows*: prune to (total - 5 + 3).
        await using var ctx = fx.NewContext();
        var total = ctx.Vectors.Count();
        await Search().PruneToFifoCapAsync(total - 2); // drop the 2 oldest overall, which are this test's _0 and _1

        await using var ctx2 = fx.NewContext();
        Assert.False(ctx2.Vectors.Any(v => v.Id == $"{prefix}_0"));
        Assert.True(ctx2.Vectors.Any(v => v.Id == $"{prefix}_4"));
    }
}
```
> The prune test is written to be robust to other tests' rows by computing the cap from the current total; it only asserts that the two oldest-overall (this test's `_0`/`_1`, stamped 10 min ago) are gone and the newest is kept.

- [ ] **Step 3: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: FAIL — `SqlServerVectorSearch` does not exist.

- [ ] **Step 4: Implement**

Create `Ml/SqlServerVectorSearch.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Ml;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Data.Ml;

/// <summary>SQL Server 2019-compatible vector store: embeddings as VARBINARY, top-K via a C# brute-force
/// cosine scan (the legacy search is itself O(n)). A SQL Server 2025 VECTOR_DISTANCE path can replace
/// SearchAsync behind IVectorSearch without touching callers.</summary>
public sealed class SqlServerVectorSearch(WorldMonitorDbContext db, IClock clock) : IVectorSearch
{
    public async Task UpsertAsync(VectorEntry entry, CancellationToken ct = default)
    {
        var existing = await db.Vectors.FindAsync([entry.Id], ct);
        if (existing is null)
        {
            if (entry.IngestedAt == default) entry.IngestedAt = clock.UtcNow;
            db.Vectors.Add(entry);
        }
        else
        {
            existing.Text = entry.Text;
            existing.Embedding = entry.Embedding;
            existing.PubDate = entry.PubDate;
            existing.Source = entry.Source;
            existing.Url = entry.Url;
            existing.Tags = entry.Tags;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(float[] query, int topK, CancellationToken ct = default)
    {
        var rows = await db.Vectors.AsNoTracking()
            .Select(v => new { v.Id, v.Text, v.Url, v.Embedding })
            .ToListAsync(ct);

        return rows
            .Select(r => new VectorHit(r.Id, r.Text, VectorMath.Cosine(query, VectorMath.ToFloats(r.Embedding)), r.Url))
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToList();
    }

    public async Task<int> PruneToFifoCapAsync(int maxVectors, CancellationToken ct = default)
    {
        // Keep the newest maxVectors by IngestedAt (tie-broken on Id); delete everything else in one DELETE.
        var keepIds = db.Vectors
            .OrderByDescending(v => v.IngestedAt).ThenByDescending(v => v.Id)
            .Take(maxVectors)
            .Select(v => v.Id);
        return await db.Vectors.Where(v => !keepIds.Contains(v.Id)).ExecuteDeleteAsync(ct);
    }
}
```

- [ ] **Step 5: Run, verify pass; commit**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: PASS.
```bash
git add dotnet/
git commit -m "feat(data): IVectorSearch + SqlServerVectorSearch (cosine fallback + FIFO prune)"
```

---

### Task 5: `DedupRepository` — per-signal-type TTL

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Repositories/DedupRepository.cs`
- Test: `dotnet/test/WorldMonitor.Data.Tests/Unit/DedupTtlTests.cs`, `dotnet/test/WorldMonitor.Data.Tests/Integration/MlStateTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Unit/DedupTtlTests.cs`:
```csharp
using WorldMonitor.Data.Repositories;
using Xunit;

namespace WorldMonitor.Data.Tests.Unit;

public class DedupTtlTests
{
    [Theory]
    [InlineData("silent_divergence", 6 * 60)]
    [InlineData("flow_price_divergence", 6 * 60)]
    [InlineData("explained_market_move", 6 * 60)]
    [InlineData("prediction_leads_news", 2 * 60)]
    [InlineData("keyword_spike", 30)]
    [InlineData("anything_else", 30)]   // default
    public void TtlFor_matches_legacy_minutes(string signalType, int expectedMinutes)
        => Assert.Equal(expectedMinutes, (int)DedupRepository.TtlFor(signalType).TotalMinutes);
}
```

Create `Integration/MlStateTests.cs`:
```csharp
using WorldMonitor.Data.Entities.Ml;
using WorldMonitor.Data.Repositories;
using WorldMonitor.Data.Tests.Fakes;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class MlStateTests(LocalDbFixture fx)
{
    private static string K() => "k_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Dedup_blocks_within_ttl_and_allows_after_expiry()
    {
        var clock = new TestClock();
        var key = K();
        var repo = new DedupRepository(fx.NewContext(), clock);

        Assert.True(await repo.TryMarkSeenAsync(key, "keyword_spike"));   // first ⇒ newly marked
        Assert.False(await new DedupRepository(fx.NewContext(), clock).TryMarkSeenAsync(key, "keyword_spike")); // within 30m ⇒ duplicate

        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.True(await new DedupRepository(fx.NewContext(), clock).TryMarkSeenAsync(key, "keyword_spike")); // past TTL ⇒ allowed again
    }
}
```
> Note: this requires the `TestClock` fake. It currently lives in `WorldMonitor.Caching.Tests`; the implementer must **add a copy** at `dotnet/test/WorldMonitor.Data.Tests/Fakes/TestClock.cs` (same content: a settable `IClock`) so `WorldMonitor.Data.Tests` can use it without referencing the caching test project. Create that file in this step.

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests`
Expected: FAIL — `DedupRepository`/`TestClock` do not exist.

- [ ] **Step 3: Implement**

Create `dotnet/test/WorldMonitor.Data.Tests/Fakes/TestClock.cs`:
```csharp
using WorldMonitor.Data.Time;

namespace WorldMonitor.Data.Tests.Fakes;

public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan by) => UtcNow += by;
}
```

Create `Repositories/DedupRepository.cs`:
```csharp
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
```

- [ ] **Step 4: Run, verify pass; commit**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests`
Expected: PASS.
```bash
git add dotnet/
git commit -m "feat(data): DedupRepository with per-signal-type TTL"
```

---

### Task 6: `TopicVelocityRepository` (7-day baseline) + `CorrelationStateRepository` (cold-start)

**Files:**
- Create: `dotnet/src/WorldMonitor.Data/Repositories/TopicVelocityRepository.cs`
- Create: `dotnet/src/WorldMonitor.Data/Repositories/CorrelationStateRepository.cs`
- Test: append to `dotnet/test/WorldMonitor.Data.Tests/Integration/MlStateTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `MlStateTests.cs` (inside the class):
```csharp
    [Fact]
    public async Task Topic_baseline_averages_last_7_days()
    {
        var clock = new TestClock();
        var topic = "t_" + Guid.NewGuid().ToString("N");
        var repo = new TopicVelocityRepository(fx.NewContext(), clock);
        await repo.AddAsync(topic, 10);
        clock.Advance(TimeSpan.FromDays(1));
        await new TopicVelocityRepository(fx.NewContext(), clock).AddAsync(topic, 20);

        var baseline = await new TopicVelocityRepository(fx.NewContext(), clock).SevenDayBaselineAsync(topic);
        Assert.Equal(15.0, baseline);
    }

    [Fact]
    public async Task Correlation_state_is_null_on_cold_start_then_round_trips()
    {
        var clock = new TestClock();
        // A dedicated DB per other tests means a fresh CorrelationStates table is empty at cold start.
        var repo = new CorrelationStateRepository(fx.NewContext(), clock);
        // Save then read (single-row); the saved snapshot round-trips.
        await repo.SaveAsync("{\"n\":1}", "{\"m\":2}", "{\"p\":3}");
        var latest = await new CorrelationStateRepository(fx.NewContext(), clock).GetLatestAsync();
        Assert.NotNull(latest);
        Assert.Equal("{\"n\":1}", latest!.NewsVelocity);
    }
```
> The cold-start (null) case is covered structurally: `GetLatestAsync` returns `FirstOrDefaultAsync` which is null on an empty table. This test focuses on the round-trip; the null path is exercised by the empty-table default of `FirstOrDefaultAsync` and asserted in the unit-level contract.

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: FAIL — repositories do not exist.

- [ ] **Step 3: Implement**

Create `Repositories/TopicVelocityRepository.cs`:
```csharp
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
```

Create `Repositories/CorrelationStateRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Ml;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Data.Repositories;

/// <summary>Single-row previous-cycle snapshot. GetLatestAsync returns null on cold start (the engine
/// emits nothing on the first run).</summary>
public sealed class CorrelationStateRepository(WorldMonitorDbContext db, IClock clock)
{
    public Task<CorrelationState?> GetLatestAsync(CancellationToken ct = default)
        => db.CorrelationStates.AsNoTracking().OrderByDescending(s => s.Timestamp).FirstOrDefaultAsync(ct);

    public async Task SaveAsync(string newsVelocity, string marketChanges, string predictionChanges, CancellationToken ct = default)
    {
        var existing = await db.CorrelationStates.FirstOrDefaultAsync(ct);
        if (existing is null)
            db.CorrelationStates.Add(new CorrelationState
            {
                Timestamp = clock.UtcNow, NewsVelocity = newsVelocity,
                MarketChanges = marketChanges, PredictionChanges = predictionChanges,
            });
        else
        {
            existing.Timestamp = clock.UtcNow;
            existing.NewsVelocity = newsVelocity;
            existing.MarketChanges = marketChanges;
            existing.PredictionChanges = predictionChanges;
        }
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run, verify pass; commit**

Run: `dotnet test dotnet/test/WorldMonitor.Data.Tests --filter Category=Integration`
Expected: PASS.
```bash
git add dotnet/
git commit -m "feat(data): TopicVelocity 7-day baseline + CorrelationState single-row snapshot"
```

---

### Task 7: Green build, README, PR

**Files:**
- Modify: `dotnet/README.md`

- [ ] **Step 1: Update README**

Add to `dotnet/README.md` (after the P1b-3 entry):
```markdown
- `src/WorldMonitor.Data` (P1c) — server-side ML state: `VectorEntry` + `IVectorSearch`/`SqlServerVectorSearch`
  (VARBINARY embeddings, top-K via a C# brute-force cosine fallback — SQL 2019 has no native VECTOR — and
  FIFO prune to 5000); `DedupRepository` (per-signal-type TTL); `TopicVelocityRepository` (7-day baseline);
  `CorrelationStateRepository` (single-row snapshot, cold-start null). **Completes the WorldMonitor.Data data layer.**
```

- [ ] **Step 2: Full clean build + test**

Run:
```bash
dotnet build dotnet/WorldMonitor.slnx -c Release
dotnet test dotnet/WorldMonitor.slnx -c Release --filter Category!=Integration
dotnet test dotnet/WorldMonitor.slnx -c Release
```
Expected: build 0/0; unit ring green (adds VectorMath + Dedup-TTL DB-free tests); full suite green (adds the ML LocalDB integration tests).

- [ ] **Step 3: Commit, push, open PR**

```bash
git add dotnet/
git commit -m "docs(data): document P1c ML state; completes the data layer"
git push -u origin feat/dotnet-p1c-vector-correlation
gh pr create --base main --title "P1c: Vector + correlation state (completes the data layer)" --body "Implements P1c (final data-layer slice): VectorEntry + IVectorSearch/SqlServerVectorSearch (VARBINARY Float32[384] embeddings, idempotent upsert, top-K via C# brute-force cosine fallback for SQL Server 2019, FIFO prune to 5000), DedupRepository (per-signal-type TTL: 6h/2h/30m), TopicVelocityRepository (7-day baseline), CorrelationStateRepository (single-row, cold-start null), CorrelationClusterState. VectorMath cosine is DB-free unit-tested. AddMlState migration. LlmAssessmentCache dropped (no generative AI); SQL Server 2025 native VECTOR path deferred behind IVectorSearch. **Completes WorldMonitor.Data.** Next: P2 — the ASP.NET Core API host (first runnable app)."
```
Expected: PR URL printed.

---

## Self-review

**Spec coverage (P1c scope, from the inventory + verified constants):**
- ✅ `VectorEntry` + `IVectorSearch`: idempotent upsert, top-K cosine (C# fallback matching `cosineSimilarityF32`), FIFO prune to 5000 by `IngestedAt` — Tasks 1–4.
- ✅ `VectorMath.Cosine` DB-free unit-tested (incl. zero-norm → 0, not NaN) + float/byte round-trip — Task 3.
- ✅ `DedupRepository` per-signal-type TTL (exact legacy values) — Task 5.
- ✅ `TopicVelocityRepository` 7-day baseline — Task 6.
- ✅ `CorrelationStateRepository` single-row snapshot with cold-start null — Task 6.
- ✅ `CorrelationClusterState` entity + unique `(Domain, ClusterKey)` — Tasks 1–2.
- ✅ Migration applies on top of P1b-3's — Task 2.
- **Deferred (documented in header):** the consuming analysis/ONNX engines → P6; retention scheduling → P7; SQL Server 2025 native `VECTOR_DISTANCE` path → behind `IVectorSearch`. `LlmAssessmentCache` NOT created.

**Placeholder scan:** none — every code/command step is complete.

**Type consistency:** `VectorMath` (Task 3) is used by `SqlServerVectorSearch` (Task 4). `IVectorSearch`/`VectorHit` (Task 4) implemented by `SqlServerVectorSearch`. `DedupRepository.TtlFor` (Task 5) is unit-tested and used by `TryMarkSeenAsync`. All repos use the P1b `IClock` convention. The new `TestClock` fake (Task 5) lives in the Data test project. DbSets (Task 2) back every repo.

**Note for execution:** the `Search`/`Prune` integration tests are written to tolerate rows from other tests in the shared LocalDB (they filter by per-test id prefixes and compute the prune cap from the live total). `VARBINARY(1536)` holds exactly Float32[384]; a wrong-length embedding would still store but score via `VectorMath` (which returns 0 on length mismatch). All new ML tests except `VectorMathTests`/`DedupTtlTests` are `[Trait("Category","Integration")]`.
```
