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
- `src/WorldMonitor.Data` (P1b-2) — Notifications: `NotificationChannel` (TPH over 6 channel types) with
  `UNIQUE(UserId,ChannelType)` + a filtered `UNIQUE(Endpoint)` backstop; `NotificationChannelRepository.SetWebPushAsync`
  transfers a re-registered push endpoint to the new owner (cross-account leak guard); `AlertRule` (JSON arrays +
  string enums, `aiDigestEnabled` inert); `TelegramPairingToken` (unique token).
- `src/WorldMonitor.Data` (P1b-3) — Waitlist/Access: `Registration` (IDENTITY-based waitlist position,
  unique `NormalizedEmail`; broadcast wave fields dropped), `UserReferralCode`/`UserReferralCredit`,
  `ContactMessage`, `UserApiKey` (unique `KeyHash`, no premium gate), `EmailSuppression`.
  `RegistrationRepository.RegisterAsync` is idempotent and reads `EmailSuppression`. **Completes the P1b domain model.**
- `src/WorldMonitor.Data` (P1c) — server-side ML state: `VectorEntry` + `IVectorSearch`/`SqlServerVectorSearch`
  (VARBINARY embeddings, top-K via a C# brute-force cosine fallback — SQL 2019 has no native VECTOR — and
  FIFO prune to 5000); `DedupRepository` (per-signal-type TTL); `TopicVelocityRepository` (7-day baseline);
  `CorrelationStateRepository` (single-row snapshot, cold-start null). **Completes the WorldMonitor.Data data layer.**

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
