# World Monitor → .NET / Blazor / SQL Server Rewrite — Design Spec

> Status: **Draft for review** · Date: 2026-06-16 · Owner: capnbigal
> Grounded in a 15-agent inventory of the existing system (see `docs/superpowers/specs/` siblings and `ARCHITECTURE.md`).

## 1. Goal

Rewrite World Monitor — today a ~400K+ LOC TypeScript/JavaScript single-page app plus a Vercel edge API, a Railway relay, Convex, Upstash Redis, and a Tauri desktop shell — onto a **single .NET stack**, retaining functionality completely while eliminating as much JavaScript as physically possible.

The product becomes **100% free and open-source, fully self-hostable, with no paywalls, no premium tier, no paid SaaS dependencies, and no per-use API costs.**

## 2. Locked decisions (decision log)

| # | Decision | Choice |
|---|---|---|
| D1 | Primary driver | Consolidate the entire stack on .NET/C# |
| D2 | Client model | **Blazor WebAssembly** (gated by a performance spike — see §13) |
| D3 | Scope | **Full feature parity**, big-bang (ship at parity, internally phased) |
| D4 | Irreducible JS | Thin, bounded C# **JS-interop** shims for rendering/media/platform libs; ML moves server-side |
| D5 | Hosting target | **Self-hosted Docker** (docker-compose) |
| D6 | SaaS posture | **Maximal .NET / free-OSS**: replace Clerk, Convex, Upstash; **remove payments entirely** |
| D7 | Desktop | **Deferred** to a later phase (Blazor Hybrid candidate) |
| D8 | Payments / premium | **Removed.** No billing, no entitlement gating — every feature free for all |
| D9 | Generative AI | **Dropped** (ChatAnalyst, Deduction, AI briefs, correlation LLM assessment). Free local ONNX analysis retained |
| D10 | Accounts / auth | **Optional self-hosted accounts** via ASP.NET Core Identity + OpenIddict (anonymous use still fully works) |
| D11 | Cache | SQL Server `CacheEntries` table + **FusionCache** (fail-safe / stampede protection) — replaces Redis |
| D12 | RPC contracts | Shared C# **Contracts** DTO library — **proto/sebuf deleted**, not ported |
| D13 | Real-time | **SignalR** for AIS vessel push (server-side flow control) |

## 3. Scope & parity inventory

**Verified counts** (from the inventory): **~120 panels**, **34 API domains**, **65 map layers**, **62 upstream providers**, **25 Convex tables**, **6 variants**, **24 locales (~2,443 i18n keys)**.

### Retained (must reach parity)
All map layers and renderers; all data domains and upstream providers (free + optional-key); all non-generative panels; the correlation/CII/cross-module/signal-aggregator analytics engines; news clustering & semantic search; local ONNX ML (embeddings, sentiment, summarization, NER); the smart-poll refresh model; variants, i18n, theming; PWA; optional accounts + watchlists + cloud-prefs sync; live media (HLS/YouTube/webcams).

### Removed (by decision)
- **Billing subsystem** — Dodo checkout, customer portal, webhooks, subscriptions, the `@dodopayments/convex` component, identity-HMAC bridge.
- **Premium/entitlement gating** — `IEntitlementService`, `PREMIUM_RPC_PATHS`, plan/role claims, per-panel/per-layer premium flags. Everything unlocked.
- **Generative-LLM features** — `ChatAnalystPanel`, `DeductionPanel`, generative AI-brief paths, the correlation-engine "LLM assessment queue" (correlations still computed & scored algorithmically), the `@anthropic-ai/sdk` dependency. *Note:* `CustomWidgetPanel`/`McpDataPanel` survive for non-AI/MCP/user widgets; the widget-sanitizer security boundary stays.
- **Paid SaaS** — Sentry → Serilog + OpenTelemetry; Vercel Analytics → dropped (optional self-hosted Umami/Plausible); AWS S3 → local filesystem or MinIO.

### Deferred
- Tauri desktop app and its Node sidecar (revisit as Blazor Hybrid).

## 4. Target architecture

```
WorldMonitor.sln
├─ WorldMonitor.Contracts/    Shared DTO records + constants — REPLACES proto/sebuf (no codegen)
├─ WorldMonitor.Domain/       Entities, value objects, pure analytics engines (correlation, CII, cascade…)
├─ WorldMonitor.Data/         EF Core DbContext + migrations (SQL Server Express); CacheEntries; vector storage
├─ WorldMonitor.Caching/      FusionCache wrapper: fail-safe, stampede protection, freshness/seed-meta semantics
├─ WorldMonitor.Providers/    Typed HttpClients (IHttpClientFactory) for ~62 upstreams + Polly circuit breakers
├─ WorldMonitor.Ml/           ONNX Runtime for .NET: embeddings, sentiment, NER, summarization, clustering
├─ WorldMonitor.Api/          ASP.NET Core host: per-domain controllers, gateway middleware, Identity/OpenIddict,
│                             rate-limit, OutputCache tiers, ETag/304, SignalR hubs; serves the WASM app
├─ WorldMonitor.Workers/      Worker Service: seed jobs (Quartz/Cronos), AIS relay→SignalR, OREF/RSS/Telegram, health
├─ WorldMonitor.Client/       Blazor WASM: MudBlazor shell, panel framework, ~115 panels, variants, i18n, state containers
│   └─ wwwroot/js/            The irreducible JS island (see §6): map-interop, charts, hls, sanitizer, indexeddb shims
├─ WorldMonitor.Client.Maps/  Razor components wrapping the map JS-interop boundary
├─ test/                      xUnit + bUnit (components) + Playwright (e2e, retained) + golden-master fixtures
└─ deploy/                    docker-compose: reverse-proxy (YARP/Caddy) + api + worker + sqlserver
```

**Architectural pillars:**
- **Contracts over codegen.** Both ends are C#; a shared DTO record library gives type-safe client↔server with byte-compatible wire-JSON (preserve field names, `int64`-as-string) so any lingering external clients keep working. OpenAPI via Swashbuckle from the records.
- **State containers, not a God-object.** The single mutable `AppContext` becomes focused observable DI singletons (`AppStateContainer`, `NewsCache`, `MarketsCache`, `IntelligenceCache`, `PanelSettingsService`, `MapLayersService`, `InFlightTracker`) raising `OnChange` events; components subscribe and gate `StateHasChanged`.
- **One host, not 90 edge bundles.** The per-file edge-function self-containment constraint disappears; it's one ASP.NET Core app with a middleware pipeline.

## 5. Current → .NET subsystem mapping (condensed)

| Current subsystem | → .NET target | Risk |
|---|---|---|
| ~120 panels (vanilla TS + Panel.ts) | Blazor components (1 `.razor` each), `DynamicComponent` in `PanelGrid`, MudBlazor + SVG charts | High |
| `App.ts` 8-phase init | Ordered DI `BootstrapService` (`IAsyncInitializable` steps), root render gated on `IsBootstrapped` | High |
| `AppContext` mutable bag | Decomposed observable state-container singletons | High |
| Panel layout / drag / resize / tabs | `Dashboard.razor` + `PanelGrid`; SortableJS/pointer-capture interop; `PanelLayoutStore` (Blazored.LocalStorage) | High |
| Smart-poll scheduler (~60 tasks) | `RefreshScheduler` + `SmartPollLoop` (`PeriodicTimer`); IntersectionObserver/visibility interop; `InFlightTracker` | High |
| Map 3-renderer dispatcher | C# `MapDispatcher` (cache + replay) over a thin JS-interop module | Med |
| deck.gl / MapLibre / globe.gl / d3 renderers | **Irreducible JS** behind typed interop; C# pushes data, receives picked-object events | High |
| Map layer registry + variant gating | C# `LayerRegistry` (records + predicates) — source of truth | Low |
| Domain RPC services (~25) | `RpcServiceBase<T>` + Polly + `IMemoryCache`/`IBootstrapCache` | Low |
| Correlation engine + adapters, CII, cross-module, signal-aggregator | Pure C# engines (`ICorrelationEngine`, `IDomainAdapter`…) ported with **golden-master tests** | High |
| Browser ML worker | Server-side ONNX Runtime for .NET (`EmbedTexts`/`ClassifySentiment`/`ExtractEntities`/summarize) | High |
| RAG vector store (IndexedDB) | SQL Server `VECTOR(384)` + `VECTOR_DISTANCE('cosine')` (C# cosine fallback for older SQL) | Med |
| API gateway pipeline | ASP.NET Core middleware (CORS, auth, rate-limit, OutputCache tiers, ETag) | High |
| Redis cache (Upstash) | SQL `CacheEntries` (atomic UPSERT) + **FusionCache** (fail-safe + stampede) | Med→High |
| proto/sebuf codegen | **Dropped**; shared `Contracts` records | Med |
| 34 API domains | One controller per domain; typed `HttpClient` per provider | High |
| Seed runner (~149 seeders) | Worker Service `SeedJob<T>` + Quartz/Cronos | High |
| AIS WebSocket relay | `BackgroundService` (`ClientWebSocket` + bounded `Channel<T>` DropOldest) + SignalR Hub | High |
| Relay loops (OREF, RSS, Telegram, warm-pings) | Worker scheduler jobs / `BackgroundService`s | Med |
| Bootstrap + health | Minimal-API `/bootstrap` (batched SELECT) + `HealthClassifierService` + `IHealthCheck` | Med |
| Convex 25 tables | EF Core entities (UNIQUE constraints, SERIALIZABLE/UPDLOCK txns, JSON columns); SignalR/polling for reactivity | High |
| Convex crons | `IHostedService`/Quartz; OCC-scaffolding crons dropped | High |
| Clerk auth | ASP.NET Core Identity + OpenIddict | High |
| Anonymous `wm-session` HMAC | ASP.NET endpoint issuing `IDataProtector`/HMAC token in HttpOnly cookie + RateLimiter | Low |
| OAuth/MCP Pro grant server | OpenIddict (auth-code + PKCE, refresh rotation, DCR, EF grant store) | High |
| Variant system (6) | `IVariantContext` + `VariantCatalog` (single resolver) | High |
| i18n (24 langs) | `Microsoft.Extensions.Localization` + custom **JSON** `IStringLocalizer` over existing `src/locales/*.json` (not resx) | Med |
| Config + theming | `WorldMonitor.Config` static records; `MudThemeProvider` (2 themes); `data-variant` CSS vars | High |
| PWA / service worker | Blazor WASM PWA template service worker; WebPush via free .NET lib in worker | Low |
| CSP / security headers | Single source in reverse proxy + middleware (ends dual-maintenance) | Med |
| Build / chunking | `dotnet publish` → wwwroot; esbuild/Vite/buf/convex tooling **dropped** | Med |
| Deploy topology | Self-hosted docker-compose (proxy + api + worker + SQL Server) | High |
| CI gates | `dotnet build/test` (xUnit) + Playwright variant smoke + image publish | Med |

## 6. The irreducible-JS boundary

After deleting the ~400K-LOC TS app, an estimated **~26 bounded JS-interop shims remain** — mostly third-party rendering/media/platform libraries with no .NET-in-browser equivalent. Each is wrapped by a C# service via `IJSObjectReference`; **C# owns data and state, JS owns rendering/effects.**

- **Rendering:** deck.gl (+`/layers`,`/geo-layers`,`/aggregation-layers`,`/extensions`,`/mapbox`), MapLibre + luma.gl, globe.gl + three.js + OrbitControls, d3/d3-geo (mobile SVG fallback).
- **Charts:** d3 area/arc/stacked-bar in 5 panels (ProgressCharts, RenewableEnergy, SpeciesComeback, ThreatTimeline, SupplyChain TransitChart).
- **Geospatial compute:** supercluster (4 client cluster indexes), h3-js, pmtiles + @protomaps/basemaps. **satellite.js** SGP4 — *recommended to port to a C# SGP4 with parity tests and push positions server-side* (removes the dependency); kept as JS-interop only if the port is deferred.
- **Live media:** hls.js, YouTube IFrame Player API, Windy webcam embeds, the desktop sidecar `postMessage` bridge — all irreducible browser JS the panels wrap.
- **Security boundaries (must stay JS):** marked + DOMPurify (Markdown render/sanitize in remaining panels), the widget-sanitizer (`wrapWidgetHtml`/`wrapProWidgetHtml`) sandboxing arbitrary user/MCP HTML — Blazor's `MarkupString` does **not** sanitize.
- **Browser platform:** Service Worker/PWA + push-handler, IndexedDB (vector snapshots, news/flight/vessel history, persistent-cache envelopes, RSS cache), IntersectionObserver + visibilitychange + requestIdleCallback (viewport-gating, eco-mode, animated counters), storage-event (cross-tab sync), Canvas 2D share-card rendering, the bespoke `VirtualList` windowed scroller (NewsPanel), WebMCP (`navigator.modelContext`) registered synchronously pre-await, the `fetch` 401-refresh interceptor.

This is the honest realization of "remove as much JS as possible": **~85–90% of JS eliminated**, the remainder a curated interop layer.

## 7. Data layer (SQL Server + EF Core)

- **Free edition.** SQL Server **Express** (production-allowed, 10 GB/db). The `VECTOR(384)` type may be version/edition-gated → ship a **C# brute-force cosine fallback** so semantic search never depends on a paid feature.
- **Schema** = 21 retained Convex tables grouped into bounded contexts (identity, notifications, watchlist, growth, broadcast — *billing/subscription tables dropped*), plus:
  - `CacheEntries` (Key, Value, ExpiresAt, FetchedAt, RecordCount, staging columns) — atomic UPSERT in a transaction; **TTL-expiry is data-liveness** for the health classifier.
  - Durable **correlation state** tables (previousSnapshot, 7-day topic-velocity history, 30-min dedup TTL) — required by server-side ML (§8); *these were missing from the naive schema and are now first-class.*
  - Vector table/column for embeddings.
- **Concurrency** via real UNIQUE constraints, `rowversion` CAS for prefs, SERIALIZABLE/UPDLOCK where the Convex code used OCC; `sp_getapplock` single-writer for seed swaps.
- **Caching** via FusionCache (L1 memory + L2 SQL): native **fail-safe (last-good-on-failure)**, soft/hard timeouts, adaptive TTL, stampede protection. Negative-sentinel caching and isolate-local fallback caches are ported explicitly as tested behaviors (§12), not assumed.

## 8. Server-side ML (ONNX Runtime for .NET)

- Embeddings (MiniLM-L6, 384-d), sentiment (DistilBERT-SST2), NER (BERT-NER) with **C# WordPiece tokenization + entity-group aggregation** (`Microsoft.ML.Tokenizers`); summarization stays **local** (ONNX), since the cloud/generative path is removed (D9).
- Jaccard news clustering + cross-domain correlation ported with **golden-master parity tests** vs current browser output.
- **Capacity reality:** inference becomes always-on for every client (no more mobile/no-WebGL no-op gating) → the ML phase includes a batching + result-caching budget so server load is bounded.
- **Privacy note:** the per-browser IndexedDB headline vector store becomes server-side (per-user or global) — a deliberate, documented posture change.

## 9. Real-time & background workers

- **Worker Service** hosts `SeedJob<T>` (template-method) for all ~149 seeders + the ~25 relay-resident loops + the Convex crons, scheduled via Quartz/Cronos; each writes `CacheEntries` with freshness; bundle skip-gates and `dependsOn` ordering preserved.
- **AIS relay** = `BackgroundService` holding one `ClientWebSocket` to the upstream firehose, a bounded `Channel<T>` (DropOldest) for backpressure, a `ConcurrentDictionary` vessel/density store, and a **SignalR Hub** for browser push with **per-connection throttling + lazy upstream connect** (only connect when clients are present). Client per-MMSI dedup/LRU re-binds to hub-stream subscriptions; the "every-50th-message / 1 MB buffer" flow-control gets a documented SignalR-side equivalent.
- **OREF / RSS proxy / Telegram / warm-pings** become services/jobs; RSS uses `System.ServiceModel.Syndication` + Polly + an allowlist (no third-party proxy).

## 10. Auth & accounts (optional, free)

- **ASP.NET Core Identity** (free) for sign-in/up; **OpenIddict** (free OSS) provides the OIDC server *and* replaces the custom OAuth/MCP-grant server (auth-code + PKCE, refresh rotation, dynamic client registration, EF-backed grant store).
- **Anonymous-first:** `wm-session` HMAC reimplemented with `IDataProtector` in an HttpOnly cookie + RateLimiter; the full dashboard works with no account.
- **No premium gating** (D8) — `[Authorize]` is used only to scope a user's *own* synced data (watchlists, prefs, widgets), never to lock features.
- **Legacy data preservation:** keep the old `clerkUserId` as a column/claim and link at first login by verified email, so existing users' synced data survives (credentials themselves require re-establishment — passwords aren't exportable; social re-link supported).

## 11. Removed subsystems (explicit, so parity reviewers don't flag them as gaps)

Billing/checkout/portal/webhooks; subscriptions & entitlement tables; `IEntitlementService` + premium path gating; `ChatAnalystPanel`, `DeductionPanel`, generative AI-brief generation, correlation LLM-assessment; `@anthropic-ai/sdk`, `@dodopayments/*`, `@clerk/*`, `convex`, `@upstash/*`, `@sentry/*`, `@vercel/analytics`, `@aws-sdk/client-s3`; proto/sebuf/buf toolchain; esbuild edge-bundling; Vite/Workbox (replaced by Blazor PWA).

## 12. Security invariants as acceptance criteria

These were load-bearing but buried in prose; in the rewrite each is a **named, tested exit criterion**:

1. **Entitlement/identity-partitioned cache keys** — any cache key for user-varying data must include the discriminator (the old "two `TradeServiceClient` instances" rule generalized). Test: two users never observe each other's cached payloads.
2. **Auth-generation guard** — async results captured under a userId are dropped if the active userId changed (cross-user leak on fast sign-in/out). Test: rapid switch yields no stale-user data.
3. **Request-varying cache key completeness** — every shared-cache RPC includes all request-varying params. Test: param permutations don't collide.
4. **Widget/Markdown sanitization** — all AI/user/MCP HTML passes the sanitizer shim before render. Test: injection payloads are neutralized.
5. **Anonymous-session scoping** — `wms_` tokens cannot reach owner-only data. Test: anonymous requests to owned resources are rejected.

## 13. Performance strategy (the WASM gate)

Blazor WASM is the #1 risk for this workload. Mitigations, with the **golden vertical slice (P4) as a hard go/no-go gate**:

- **Perf budget** measured on the slice: interop round-trip latency for map `setX` pushes; large-payload (news/AIS/cluster) JSON deserialization time in the WASM runtime; `StateHasChanged` fan-out cost across many panels.
- **Mitigations baked in:** batch/coalesce interop calls (push arrays, not per-item); `ShouldRender` gating + targeted `StateHasChanged`; `Virtualize` (or keep the JS `VirtualList`) for long lists; payload diffing so unchanged data doesn't re-marshal; AOT/trimming; source-generated JSON serialization.
- **Fallback if the gate fails:** revisit **Blazor Web App "Auto"** (server-interactive render to avoid client-side marshalling/deser cost) *before* committing 115 more panels — cheaper to pivot at the slice than at parity.

## 14. Variants, i18n, theming

- `IVariantContext` (scoped) + `VariantCatalog` (`IOptions`) with a **single resolver** consolidating today's triple-duplicated host-prefix/config/persisted-pref detection; strongly-typed `VariantDefinition` records (default panels, layers, intervals, theme, text).
- i18n: custom **JSON** `IStringLocalizer` over the existing `src/locales/*.json` (flattened keys), lazy-loaded per locale, RTL for `ar`; the `sync-locale-keys` CI gate is preserved.
- Theming: `MudThemeProvider` with two `MudTheme`s (shared monitor + happy) + CSS custom properties (consumed by the map/canvas renderers, so they stay).

## 15. Build, deploy, CI

- **docker-compose**: reverse proxy (YARP/Caddy/nginx) + `WorldMonitor.Api` + `WorldMonitor.Workers` + SQL Server (Express). CSP/security headers **single-sourced** in the proxy/middleware (ends the current 3-way CSP sync).
- **Build** = `dotnet publish` (WASM AOT/trimmed) → `wwwroot`; PWA service worker from the Blazor template; WebPush from a free .NET lib in the worker.
- **CI** = `dotnet build` + `dotnet test` (xUnit) + `dotnet format`/analyzers; **Playwright variant smoke retained**; multi-arch container publish. Dropped: buf/proto-check, convex-deploy, cloudflare-worker, edge-bundle checks.

## 16. Testing strategy

- **xUnit** for services, providers, gateway middleware, cache/freshness semantics, security invariants (§12).
- **Golden-master** fixtures captured from the current TS output for every pure engine (correlation, CII, cascade, clustering, SGP4 if ported, URL-state parser, health classifier) — the safety net for behavioral parity.
- **bUnit** for panel components.
- **Playwright** e2e + visual-regression **retained** (per-variant golden screenshots) as the cross-stack parity check.

## 17. Risk register

| Risk | Severity | Mitigation |
|---|---|---|
| Blazor WASM perf (120 panels, 60 loops, interop marshalling, StateHasChanged fan-out) | **High** | P4 perf gate + budget; batching, `ShouldRender`, virtualization, diffing; Auto fallback (§13) |
| SQL-as-Redis freshness/coalescing semantics (fail-safe, negative-sentinel, TTL-as-liveness, single-writer) | **High** | FusionCache fail-safe; port behaviors as tested exit criteria; health-classifier parity tests |
| Live media + AIS real-time (hls.js/YT/postMessage; SignalR backpressure) | **High** | Media stays JS-interop; bounded `Channel` + per-connection throttle + lazy connect; client dedup server-side |
| Security invariants silently dropped in re-impl | **High** | §12 named acceptance tests |
| Server ML always-on cost + durable correlation state | **Med** | Batching + result cache budget; durable state tables in P1 schema |
| Legacy user data linking (Clerk non-exportable) | **Med** | Keep `clerkUserId` FK; email-match at first login; document credential re-establishment |
| Satellite SGP4 fidelity if ported | **Med** | Golden-master parity tests vs satellite.js; keep JS-interop as fallback |

## 18. Phased build sequence

> Big-bang ships only at full parity, but is internally sequenced to surface the hard bets first.

- **P0 — Contracts & constants.** `WorldMonitor.Contracts` DTO records (34 domains) with wire-JSON parity; shared constants (CII config, refresh intervals, storage keys, cache tiers). *Exit:* serializes byte-compatible; OpenAPI generates.
- **P1 — Data layer & cache substrate.** EF Core schema (21 contexts), `CacheEntries`, durable correlation-state & vector tables, FusionCache, `InFlightTracker`, single-writer lock. *Exit:* atomic UPSERT; stampede protection; vector top-K (or C# fallback).
- **P2 — API host & gateway.** Middleware pipeline (CORS, rate-limit, OutputCache tiers, ETag/304); SPA serving; `/bootstrap` + `/health` matching legacy verdicts. *Exit:* fail-closed gateway; health parity.
- **P3 — Auth & identity (no billing).** Identity + OpenIddict; JWT/cookie; `wm-session`; legacy account-linking; prefs CAS. *Exit:* sign-in/refresh; anonymous works; owned-data scoping; existing users link by email. *(Premium gating removed.)*
- **P4 — Golden vertical slice + PERF GATE.** One live-proxy domain (market) end-to-end → 2–3 panels with refresh + persisted layout. *Exit:* meets the perf budget (§13) — **go/no-go for WASM** — and establishes the panel/state/refresh template.
- **P5 — Map interop layer.** `MapDispatcher` + JS module over the 3 renderers (~50 `setX` + ~12 camera + highlight + inbound events); C# `LayerRegistry`; self-fetch behaviors lifted into C#. *Exit:* renderer swap with cache-replay; per-variant layers; events reach C#; URL round-trip.
- **P6 — Server-side ML.** ONNX embeddings/sentiment/NER + C# tokenization; clustering + correlation with durable state; vector ingest/search. *Exit:* golden-output parity; bounded batched inference.
- **P7 — Workers & data pipeline.** `SeedJob<T>` + scheduler (all seeders/loops/crons); AIS `BackgroundService` + SignalR; OREF/RSS/Telegram. *Exit:* seeders write fresh cache; AIS pushes without unbounded memory; lazy upstream connect; health green.
- **P8 — Panel & domain fan-out.** Remaining ~115 panels + 33 domains + ~25 RPC services + pure analytics engines (golden-master TDD); news pipeline; D3/markdown panels via interop shims. *Exit:* all panels parity; all domains served; engines pass golden-master.
- **P9 — Variants, i18n, theming, shell.** `IVariantContext` + catalog; JSON localizer (24 locales, RTL); MudBlazor themes; full bootstrap coordinator; layout drag/resize/tabs; localStorage + cloud-prefs + URL sync; CMD+K. *Exit:* all 6 variants + 24 locales + themes + persistence correct.
- **P10 — Infra, Docker, CI cutover.** docker-compose stack; `dotnet publish` → wwwroot; single-sourced CSP; PWA + WebPush; CI re-tooled. *Exit:* full stack in compose; all variants reachable; PWA installs + push; security headers parity; pipeline green; images published.

## 19. Open / deferred items

- **Desktop** (Tauri) — deferred; revisit as Blazor Hybrid (.NET MAUI/Photino) reusing the same components.
- **satellite.js → C# SGP4** — recommended port (removes a JS dep); confirm at P5/P6.
- **Optional self-hosted telemetry** (Umami/Plausible/GlitchTip) — out of scope unless desired.
- **MinIO vs local filesystem** for imagery/object storage — default local FS; MinIO if S3 API parity is wanted.
