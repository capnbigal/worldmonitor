# World Monitor — .NET rewrite

Greenfield .NET 10 rewrite (see `docs/superpowers/specs/2026-06-16-blazor-dotnet-rewrite-design.md`).
Coexists with the legacy TypeScript tree during migration.

## Layout
- `src/WorldMonitor.Contracts` — shared DTOs + wire conventions (replaces proto/sebuf codegen).
- `src/WorldMonitor.Data` — EF Core data layer. P1a-1: `CacheEntries` substrate (`ICacheStore` /
  `SqlServerCacheStore`), single-writer `ISeedLock` (`sp_getapplock`), `IClock`. Integration tests
  run against SQL Server LocalDB.

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
