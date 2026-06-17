# P0 — Contracts & Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the .NET solution skeleton and the `WorldMonitor.Contracts` library — the shared, codegen-free DTO + wire-convention foundation that every later phase depends on — proven end-to-end with one worked domain (Seismology) and byte-level wire-JSON parity tests.

**Architecture:** A single .NET solution under `dotnet/` (coexisting with the legacy TS tree during migration). `WorldMonitor.Contracts` is a dependency-free class library holding C# `record` DTOs that serialize byte-compatible with the existing sebuf-generated wire JSON (camelCase properties, per-field int64 encoding, snake_case query-param names, omit-when-null optionals). It **replaces** the `proto/` + `buf` + `src/generated/` codegen pipeline: because both client and server are now C#, the DTOs are shared directly. Remaining domains' DTOs are added per-domain during their P8 fan-out; runtime constants (refresh intervals, storage keys, CII config) are ported alongside their consuming phases — P0 owns only the wire-contract foundation.

**Tech Stack:** .NET 10 (LTS), C# 13, `System.Text.Json` (web defaults + camelCase + `WhenWritingNull`), xUnit. No third-party packages in `Contracts`.

**This plan is P0 of the program roadmap in `docs/superpowers/specs/2026-06-16-blazor-dotnet-rewrite-design.md` §18.** Each later phase (P1…P10) gets its own plan.

---

## Prerequisites

- .NET 10 SDK installed (`dotnet --version` ≥ `10.0.100`). If absent, install before starting.
- Working directory: repo root `C:\Users\capnb\source\repos\worldmonitor`.
- Branch: create `feat/dotnet-p0-contracts` off `main` before Task 1.

## File structure

```
dotnet/
  WorldMonitor.slnx
  Directory.Build.props                         shared TFM/nullable/langversion for all projects
  src/WorldMonitor.Contracts/
    WorldMonitor.Contracts.csproj
    Json/WmJson.cs                              canonical JsonSerializerOptions (wire format)
    Core/GeoCoordinates.cs                      shared geo DTO (lat/lng)
    Core/PaginationResponse.cs                  shared pagination DTO (nextCursor/totalCount)
    Core/FieldViolation.cs                      validation-error wire shape
    Http/CacheTier.cs                           enum of cache tiers
    Http/TierHeaders.cs                         exact Cache-Control / CDN-Cache-Control strings per tier
    Seismology/Earthquake.cs                    worked-domain DTO
    Seismology/ListEarthquakesRequest.cs        worked-domain request (with query-param name map)
    Seismology/ListEarthquakesResponse.cs       worked-domain response
  test/WorldMonitor.Contracts.Tests/
    WorldMonitor.Contracts.Tests.csproj
    JsonConventionsTests.cs
    CoreDtoParityTests.cs
    CacheTierTests.cs
    SeismologyWireParityTests.cs
    Fixtures/seismology-list-earthquakes.json   golden wire-JSON sample
```

---

### Task 1: Scaffold the solution and projects

**Files:**
- Create: `dotnet/WorldMonitor.slnx`
- Create: `dotnet/Directory.Build.props`
- Create: `dotnet/src/WorldMonitor.Contracts/WorldMonitor.Contracts.csproj`
- Create: `dotnet/test/WorldMonitor.Contracts.Tests/WorldMonitor.Contracts.Tests.csproj`

- [ ] **Step 1: Create the branch**

Run:
```bash
git checkout main && git checkout -b feat/dotnet-p0-contracts
```
Expected: `Switched to a new branch 'feat/dotnet-p0-contracts'`

- [ ] **Step 2: Create solution + projects**

Run:
```bash
dotnet new sln -n WorldMonitor -o dotnet
dotnet new classlib -n WorldMonitor.Contracts -o dotnet/src/WorldMonitor.Contracts -f net10.0
dotnet new xunit -n WorldMonitor.Contracts.Tests -o dotnet/test/WorldMonitor.Contracts.Tests -f net10.0
rm dotnet/src/WorldMonitor.Contracts/Class1.cs dotnet/test/WorldMonitor.Contracts.Tests/UnitTest1.cs
dotnet sln dotnet/WorldMonitor.slnx add dotnet/src/WorldMonitor.Contracts dotnet/test/WorldMonitor.Contracts.Tests
dotnet add dotnet/test/WorldMonitor.Contracts.Tests reference dotnet/src/WorldMonitor.Contracts
```
Expected: each command prints success; final `dotnet sln list` (optional) shows both projects.

- [ ] **Step 3: Add shared build props**

Create `dotnet/Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Verify the empty solution builds**

Run: `dotnet build dotnet/WorldMonitor.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add dotnet/
git commit -m "chore(dotnet): scaffold WorldMonitor solution + Contracts + test projects"
```

---

### Task 2: Canonical JSON conventions (`WmJson`)

The wire format is **camelCase properties** with **null optionals omitted**. This single options object is the source of truth used by the API host, the WASM client, and all parity tests.

**Files:**
- Create: `dotnet/src/WorldMonitor.Contracts/Json/WmJson.cs`
- Test: `dotnet/test/WorldMonitor.Contracts.Tests/JsonConventionsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `JsonConventionsTests.cs`:
```csharp
using System.Text.Json;
using WorldMonitor.Contracts.Json;
using Xunit;

namespace WorldMonitor.Contracts.Tests;

public class JsonConventionsTests
{
    private sealed record Sample
    {
        public string FirstName { get; init; } = "";
        public int? OptionalCount { get; init; }
    }

    [Fact]
    public void Serializes_camelCase_and_omits_null_optionals()
    {
        var json = JsonSerializer.Serialize(new Sample { FirstName = "ada" }, WmJson.Options);

        Assert.Contains("\"firstName\":\"ada\"", json);
        Assert.DoesNotContain("optionalCount", json);
        Assert.DoesNotContain("FirstName", json);
    }

    [Fact]
    public void Deserializes_case_insensitively()
    {
        var s = JsonSerializer.Deserialize<Sample>("{\"FirstName\":\"ada\"}", WmJson.Options);
        Assert.Equal("ada", s!.FirstName);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test dotnet/WorldMonitor.slnx`
Expected: FAIL — compile error, `WmJson` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Json/WmJson.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldMonitor.Contracts.Json;

/// <summary>Canonical wire-format serializer options for all World Monitor DTOs.</summary>
public static class WmJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.Strict,
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test dotnet/WorldMonitor.slnx`
Expected: PASS (2 passed).

- [ ] **Step 5: Commit**

```bash
git add dotnet/
git commit -m "feat(contracts): add canonical WmJson wire-format options"
```

---

### Task 3: Core shared DTOs (`GeoCoordinates`, `PaginationResponse`, `FieldViolation`)

These three appear across every domain (proto package `worldmonitor.core.v1`). Wire shape from `src/generated/client/worldmonitor/seismology/v1/service_client.ts`: `GeoCoordinates { latitude, longitude }`, `PaginationResponse { nextCursor, totalCount }`, `FieldViolation { field, description }`.

**Files:**
- Create: `dotnet/src/WorldMonitor.Contracts/Core/GeoCoordinates.cs`
- Create: `dotnet/src/WorldMonitor.Contracts/Core/PaginationResponse.cs`
- Create: `dotnet/src/WorldMonitor.Contracts/Core/FieldViolation.cs`
- Test: `dotnet/test/WorldMonitor.Contracts.Tests/CoreDtoParityTests.cs`

- [ ] **Step 1: Write the failing test**

Create `CoreDtoParityTests.cs`:
```csharp
using System.Text.Json;
using WorldMonitor.Contracts.Core;
using WorldMonitor.Contracts.Json;
using Xunit;

namespace WorldMonitor.Contracts.Tests;

public class CoreDtoParityTests
{
    [Fact]
    public void GeoCoordinates_wire_shape()
    {
        var json = JsonSerializer.Serialize(new GeoCoordinates { Latitude = 61.12, Longitude = -149.9 }, WmJson.Options);
        Assert.Equal("{\"latitude\":61.12,\"longitude\":-149.9}", json);
    }

    [Fact]
    public void PaginationResponse_wire_shape()
    {
        var json = JsonSerializer.Serialize(new PaginationResponse { NextCursor = "abc", TotalCount = 7 }, WmJson.Options);
        Assert.Equal("{\"nextCursor\":\"abc\",\"totalCount\":7}", json);
    }

    [Fact]
    public void FieldViolation_roundtrips()
    {
        var parsed = JsonSerializer.Deserialize<FieldViolation>(
            "{\"field\":\"id\",\"description\":\"required\"}", WmJson.Options);
        Assert.Equal("id", parsed!.Field);
        Assert.Equal("required", parsed.Description);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test dotnet/WorldMonitor.slnx`
Expected: FAIL — `GeoCoordinates`/`PaginationResponse`/`FieldViolation` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Core/GeoCoordinates.cs`:
```csharp
namespace WorldMonitor.Contracts.Core;

public sealed record GeoCoordinates
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
```

Create `Core/PaginationResponse.cs`:
```csharp
namespace WorldMonitor.Contracts.Core;

public sealed record PaginationResponse
{
    public string NextCursor { get; init; } = "";
    public int TotalCount { get; init; }
}
```

Create `Core/FieldViolation.cs`:
```csharp
namespace WorldMonitor.Contracts.Core;

public sealed record FieldViolation
{
    public string Field { get; init; } = "";
    public string Description { get; init; } = "";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test dotnet/WorldMonitor.slnx`
Expected: PASS (5 passed total).

- [ ] **Step 5: Commit**

```bash
git add dotnet/
git commit -m "feat(contracts): add core GeoCoordinates/PaginationResponse/FieldViolation DTOs"
```

---

### Task 4: Cache-tier constants (`CacheTier`, `TierHeaders`)

Exact header strings copied verbatim from `server/gateway.ts:89-112` — all **8** tiers in `TIER_HEADERS`/`TIER_CDN_CACHE`, including `no-store` (whose CDN value is `null`, so `CdnCacheControl` is typed `string?`). These drive the API host's `OutputCache`/response-header policies in P2; defining them now keeps the wire contract in one shared place.

**Files:**
- Create: `dotnet/src/WorldMonitor.Contracts/Http/CacheTier.cs`
- Create: `dotnet/src/WorldMonitor.Contracts/Http/TierHeaders.cs`
- Test: `dotnet/test/WorldMonitor.Contracts.Tests/CacheTierTests.cs`

- [ ] **Step 1: Write the failing test**

Create `CacheTierTests.cs`:
```csharp
using WorldMonitor.Contracts.Http;
using Xunit;

namespace WorldMonitor.Contracts.Tests;

public class CacheTierTests
{
    [Fact]
    public void Fast_tier_cache_control_matches_legacy_gateway()
    {
        Assert.Equal(
            "public, max-age=60, s-maxage=300, stale-while-revalidate=60, stale-if-error=600",
            TierHeaders.CacheControl[CacheTier.Fast]);
    }

    [Fact]
    public void Daily_tier_cdn_cache_control_matches_legacy_gateway()
    {
        Assert.Equal(
            "public, s-maxage=86400, stale-while-revalidate=14400, stale-if-error=172800",
            TierHeaders.CdnCacheControl[CacheTier.Daily]);
    }

    [Fact]
    public void NoStore_tier_matches_legacy_gateway()
    {
        Assert.Equal("no-store", TierHeaders.CacheControl[CacheTier.NoStore]);
        Assert.Null(TierHeaders.CdnCacheControl[CacheTier.NoStore]);
    }

    [Fact]
    public void Every_tier_is_present_in_both_maps()
    {
        // Every tier must have a key in both maps. CdnCacheControl[NoStore] is intentionally
        // null (no CDN-Cache-Control header for no-store) — see NoStore_tier_matches_legacy_gateway.
        foreach (var tier in Enum.GetValues<CacheTier>())
        {
            Assert.True(TierHeaders.CacheControl.ContainsKey(tier), $"missing Cache-Control for {tier}");
            Assert.True(TierHeaders.CdnCacheControl.ContainsKey(tier), $"missing CDN-Cache-Control for {tier}");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test dotnet/WorldMonitor.slnx`
Expected: FAIL — `CacheTier`/`TierHeaders` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Http/CacheTier.cs`:
```csharp
namespace WorldMonitor.Contracts.Http;

public enum CacheTier
{
    Fast,
    Medium,
    Slow,
    SlowBrowser,
    Static,
    Daily,
    NoStore,
    Live,
}
```

Create `Http/TierHeaders.cs` (values verbatim from `server/gateway.ts:89-112`):
```csharp
using System.Collections.Frozen;

namespace WorldMonitor.Contracts.Http;

/// <summary>Cache-Control + CDN-Cache-Control strings per tier, mirrored from the legacy gateway.</summary>
public static class TierHeaders
{
    public static readonly FrozenDictionary<CacheTier, string> CacheControl = new Dictionary<CacheTier, string>
    {
        [CacheTier.Fast]        = "public, max-age=60, s-maxage=300, stale-while-revalidate=60, stale-if-error=600",
        [CacheTier.Medium]      = "public, max-age=120, s-maxage=600, stale-while-revalidate=120, stale-if-error=900",
        [CacheTier.Slow]        = "public, max-age=300, s-maxage=1800, stale-while-revalidate=300, stale-if-error=3600",
        [CacheTier.SlowBrowser] = "max-age=300, stale-while-revalidate=60, stale-if-error=1800",
        [CacheTier.Static]      = "public, max-age=600, s-maxage=3600, stale-while-revalidate=600, stale-if-error=14400",
        [CacheTier.Daily]       = "public, max-age=3600, s-maxage=14400, stale-while-revalidate=7200, stale-if-error=172800",
        [CacheTier.NoStore]     = "no-store",
        [CacheTier.Live]        = "public, max-age=30, s-maxage=60, stale-while-revalidate=60, stale-if-error=300",
    }.ToFrozenDictionary();

    // CdnCacheControl[NoStore] is null (no CDN-Cache-Control header for no-store), mirroring TIER_CDN_CACHE.
    public static readonly FrozenDictionary<CacheTier, string?> CdnCacheControl = new Dictionary<CacheTier, string?>
    {
        [CacheTier.Fast]        = "public, s-maxage=600, stale-while-revalidate=300, stale-if-error=1200",
        [CacheTier.Medium]      = "public, s-maxage=1200, stale-while-revalidate=600, stale-if-error=1800",
        [CacheTier.Slow]        = "public, s-maxage=3600, stale-while-revalidate=900, stale-if-error=7200",
        [CacheTier.SlowBrowser] = "public, s-maxage=900, stale-while-revalidate=60, stale-if-error=1800",
        [CacheTier.Static]      = "public, s-maxage=14400, stale-while-revalidate=3600, stale-if-error=28800",
        [CacheTier.Daily]       = "public, s-maxage=86400, stale-while-revalidate=14400, stale-if-error=172800",
        [CacheTier.NoStore]     = null,
        [CacheTier.Live]        = "public, s-maxage=60, stale-while-revalidate=60, stale-if-error=300",
    }.ToFrozenDictionary();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test dotnet/WorldMonitor.slnx`
Expected: PASS (9 passed total).

- [ ] **Step 5: Commit**

```bash
git add dotnet/
git commit -m "feat(contracts): add CacheTier + TierHeaders mirrored from legacy gateway"
```

---

### Task 5: Seismology worked domain + golden wire-parity test

Proves the full DTO pattern with the trickiest wire details, sourced from `proto/worldmonitor/seismology/v1/*.proto` and `src/generated/client/worldmonitor/seismology/v1/service_client.ts`:
- camelCase property names (`depthKm`, `occurredAt`, `nearTestSite`, `sourceUrl`).
- `occurredAt` is `int64` annotated `INT64_ENCODING_NUMBER` → serialized as a **JSON number**, not a string (this is per-field; other domains may use string-encoded int64 with a converter — out of scope here).
- `optional` proto fields (`nearTestSite`, `testSiteName`, `concernScore`, `concernLevel`) → nullable C#, omitted when null.
- GET request query-param names are **snake_case** (`page_size`, `min_magnitude`) even though the request DTO properties are camelCase — captured as a name map for the P2 router.

**Files:**
- Create: `dotnet/src/WorldMonitor.Contracts/Seismology/Earthquake.cs`
- Create: `dotnet/src/WorldMonitor.Contracts/Seismology/ListEarthquakesRequest.cs`
- Create: `dotnet/src/WorldMonitor.Contracts/Seismology/ListEarthquakesResponse.cs`
- Create: `dotnet/test/WorldMonitor.Contracts.Tests/Fixtures/seismology-list-earthquakes.json`
- Test: `dotnet/test/WorldMonitor.Contracts.Tests/SeismologyWireParityTests.cs`

- [ ] **Step 1: Add the golden fixture**

Create `Fixtures/seismology-list-earthquakes.json` (representative wire JSON; **at execution time, refresh this from a real local `/api/seismology/v1/list-earthquakes` response once P2 exists, then re-run the test**):
```json
{
  "earthquakes": [
    {
      "id": "us7000abcd",
      "place": "10 km SW of Anchorage, Alaska",
      "magnitude": 5.2,
      "depthKm": 34.1,
      "location": { "latitude": 61.12, "longitude": -149.9 },
      "occurredAt": 1718530000000,
      "sourceUrl": "https://earthquake.usgs.gov/earthquakes/eventpage/us7000abcd",
      "nearTestSite": false,
      "concernScore": 42.5,
      "concernLevel": "moderate"
    }
  ],
  "pagination": { "nextCursor": "", "totalCount": 1 }
}
```

In `WorldMonitor.Contracts.Tests.csproj`, ensure the fixture copies to output by adding inside an `<ItemGroup>`:
```xml
<None Update="Fixtures/seismology-list-earthquakes.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 2: Write the failing test**

Create `SeismologyWireParityTests.cs`:
```csharp
using System.Text.Json;
using WorldMonitor.Contracts.Json;
using WorldMonitor.Contracts.Seismology;
using Xunit;

namespace WorldMonitor.Contracts.Tests;

public class SeismologyWireParityTests
{
    private static string FixtureJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "seismology-list-earthquakes.json"));

    [Fact]
    public void Deserializes_golden_fixture()
    {
        var resp = JsonSerializer.Deserialize<ListEarthquakesResponse>(FixtureJson(), WmJson.Options)!;

        var eq = Assert.Single(resp.Earthquakes);
        Assert.Equal("us7000abcd", eq.Id);
        Assert.Equal(34.1, eq.DepthKm);
        Assert.Equal(1718530000000L, eq.OccurredAt);
        Assert.False(eq.NearTestSite);
        Assert.Null(eq.TestSiteName);
        Assert.Equal(1, resp.Pagination!.TotalCount);
    }

    [Fact]
    public void Serializes_with_camelCase_number_int64_and_omitted_nulls()
    {
        var eq = new Earthquake
        {
            Id = "us7000abcd",
            Place = "10 km SW of Anchorage, Alaska",
            Magnitude = 5.2,
            DepthKm = 34.1,
            OccurredAt = 1718530000000L,
            SourceUrl = "https://example/us7000abcd",
            NearTestSite = false,
            ConcernScore = 42.5,
            ConcernLevel = "moderate",
        };

        var json = JsonSerializer.Serialize(eq, WmJson.Options);

        Assert.Contains("\"depthKm\":34.1", json);
        Assert.Contains("\"occurredAt\":1718530000000", json);   // number, no quotes
        Assert.Contains("\"nearTestSite\":false", json);
        Assert.DoesNotContain("testSiteName", json);             // null omitted
        Assert.DoesNotContain("depth_km", json);                 // not snake_case
    }

    [Fact]
    public void Query_param_name_map_is_snake_case()
    {
        Assert.Equal("page_size", ListEarthquakesRequest.QueryNames[nameof(ListEarthquakesRequest.PageSize)]);
        Assert.Equal("min_magnitude", ListEarthquakesRequest.QueryNames[nameof(ListEarthquakesRequest.MinMagnitude)]);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test dotnet/WorldMonitor.slnx`
Expected: FAIL — seismology types do not exist.

- [ ] **Step 4: Write minimal implementation**

Create `Seismology/Earthquake.cs`:
```csharp
using WorldMonitor.Contracts.Core;

namespace WorldMonitor.Contracts.Seismology;

public sealed record Earthquake
{
    public required string Id { get; init; }
    public string Place { get; init; } = "";
    public double Magnitude { get; init; }
    public double DepthKm { get; init; }
    public GeoCoordinates? Location { get; init; }
    public long OccurredAt { get; init; }              // INT64_ENCODING_NUMBER → JSON number
    public string SourceUrl { get; init; } = "";
    public bool? NearTestSite { get; init; }
    public string? TestSiteName { get; init; }
    public double? ConcernScore { get; init; }
    public string? ConcernLevel { get; init; }
}
```

Create `Seismology/ListEarthquakesResponse.cs`:
```csharp
using WorldMonitor.Contracts.Core;

namespace WorldMonitor.Contracts.Seismology;

public sealed record ListEarthquakesResponse
{
    public IReadOnlyList<Earthquake> Earthquakes { get; init; } = [];
    public PaginationResponse? Pagination { get; init; }
}
```

Create `Seismology/ListEarthquakesRequest.cs` (the `QueryNames` map captures the proto `(sebuf.http.query)` names for the P2 GET router):
```csharp
using System.Collections.Frozen;

namespace WorldMonitor.Contracts.Seismology;

public sealed record ListEarthquakesRequest
{
    public long Start { get; init; }
    public long End { get; init; }
    public int PageSize { get; init; }
    public string Cursor { get; init; } = "";
    public double MinMagnitude { get; init; }

    /// <summary>C# property name → wire query-param name, from the proto (sebuf.http.query) annotations.</summary>
    public static readonly FrozenDictionary<string, string> QueryNames = new Dictionary<string, string>
    {
        [nameof(Start)]        = "start",
        [nameof(End)]          = "end",
        [nameof(PageSize)]     = "page_size",
        [nameof(Cursor)]       = "cursor",
        [nameof(MinMagnitude)] = "min_magnitude",
    }.ToFrozenDictionary();
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test dotnet/WorldMonitor.slnx`
Expected: PASS (12 passed total).

- [ ] **Step 6: Commit**

```bash
git add dotnet/
git commit -m "feat(contracts): add seismology DTOs with golden wire-parity tests"
```

---

### Task 6: Solution-wide green build + README + open PR

**Files:**
- Create: `dotnet/README.md`

- [ ] **Step 1: Add a short README**

Create `dotnet/README.md`:
```markdown
# World Monitor — .NET rewrite

Greenfield .NET 10 rewrite (see `docs/superpowers/specs/2026-06-16-blazor-dotnet-rewrite-design.md`).
Coexists with the legacy TypeScript tree during migration.

## Layout
- `src/WorldMonitor.Contracts` — shared DTOs + wire conventions (replaces proto/sebuf codegen).

## Build & test
```
dotnet build dotnet/WorldMonitor.slnx
dotnet test  dotnet/WorldMonitor.slnx
```
```

- [ ] **Step 2: Full clean build + test**

Run:
```bash
dotnet build dotnet/WorldMonitor.slnx -c Release
dotnet test dotnet/WorldMonitor.slnx -c Release
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` and `Passed! - Failed: 0, Passed: 12`.

- [ ] **Step 3: Commit + push + open PR**

```bash
git add dotnet/README.md
git commit -m "docs(dotnet): add solution README"
git push -u origin feat/dotnet-p0-contracts
gh pr create --base main --title "P0: .NET Contracts foundation" \
  --body "Implements P0 of the Blazor/.NET rewrite (docs/superpowers/specs/2026-06-16-blazor-dotnet-rewrite-design.md). Solution scaffold + WorldMonitor.Contracts (wire conventions, core DTOs, cache tiers, seismology worked domain) with 12 passing wire-parity tests."
```
Expected: PR URL printed.

---

## Self-review

**Spec coverage (P0 scope only):**
- ✅ Solution scaffold — Task 1.
- ✅ `WorldMonitor.Contracts` codegen-free DTO library — Tasks 2–5.
- ✅ Wire-JSON parity (camelCase, per-field int64-as-number, omit-null, snake_case query names) — Tasks 2, 5.
- ✅ Cache-tier constants mirrored from the gateway — Task 4.
- ✅ Worked domain proving the repeatable pattern for the remaining 33 domains — Task 5.
- **Deliberately deferred (documented in the header):** remaining domains' DTOs → their P8 fan-out; runtime constants (refresh intervals, storage keys, CII config) → their consuming phases; string-encoded `int64` converter → first domain that needs it.

**Placeholder scan:** none — every code/command step shows complete content.

**Type consistency:** `WmJson.Options` (Task 2) is the single options object used in Tasks 3 & 5. `GeoCoordinates`/`PaginationResponse` (Task 3) are reused by the seismology DTOs (Task 5). `CacheTier` enum names (Task 4) match the legacy tier keys. `ListEarthquakesRequest.QueryNames` keys use `nameof(...)` so they cannot drift from the property names.

**Note for execution:** the golden fixture in Task 5 is a faithful hand-built sample of the known wire shape (the live API is bot-blocked). Once P2 serves the endpoint locally, refresh the fixture from a real response and re-run `SeismologyWireParityTests` to lock byte-level parity.
```
