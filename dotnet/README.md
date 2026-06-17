# World Monitor — .NET rewrite

Greenfield .NET 10 rewrite (see `docs/superpowers/specs/2026-06-16-blazor-dotnet-rewrite-design.md`).
Coexists with the legacy TypeScript tree during migration.

## Layout
- `src/WorldMonitor.Contracts` — shared DTOs + wire conventions (replaces proto/sebuf codegen).

## Build & test
```
dotnet build dotnet/WorldMonitor.sln
dotnet test  dotnet/WorldMonitor.sln
```
