# World Monitor — .NET rewrite

Greenfield .NET 10 rewrite (see `docs/superpowers/specs/2026-06-16-blazor-dotnet-rewrite-design.md`).
Coexists with the legacy TypeScript tree during migration.

## Layout
- `src/WorldMonitor.Contracts` — shared DTOs + wire conventions (replaces proto/sebuf codegen).
- `src/WorldMonitor.Data` — EF Core data layer. P1a-1: `CacheEntries` substrate (`ICacheStore` /
  `SqlServerCacheStore`), single-writer `ISeedLock` (`sp_getapplock`), `IClock`. Integration tests
  run against SQL Server LocalDB.
- `src/WorldMonitor.Caching` — `WorldMonitorCache`, the read-through cache over `ICacheStore`.
  Hand-rolled (not FusionCache) to faithfully reproduce legacy `cachedFetchJson`: in-flight
  coalescing, asymmetric negative-sentinel caching (120s on null / 30s on error + re-throw),
  and an isolate-local outage bridge. All tests are DB-free unit tests using an in-memory store
  fake and a virtual `IClock`.
- `src/WorldMonitor.Data` (P1b-1) — first domain entities: `User`, `UserPreference` (client-version
  compare-and-set repository), `FollowedCountry` (`UNIQUE(UserId,Country)` retires the legacy OCC
  scaffolding; follower count via a single floor-applying helper). LocalDB integration tests.

## Database (dev/test)
Integration tests target LocalDB `(localdb)\MSSQLLocalDB` (no Docker). Apply migrations manually with:
```
dotnet ef database update --project dotnet/src/WorldMonitor.Data --startup-project dotnet/src/WorldMonitor.Data
```
Run only fast unit tests (no DB): `dotnet test --filter Category!=Integration`.

## Build & test
```
dotnet build dotnet/WorldMonitor.slnx
dotnet test  dotnet/WorldMonitor.slnx
```
