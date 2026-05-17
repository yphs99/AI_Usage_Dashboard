# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Authoritative spec

`SPEC.md` is the canonical system spec (date-stamped, ~440 lines). It documents data model, indexes, API contracts, worker pipeline, and known tech debt. **Read it before designing changes** — and update it when behavior changes. `README.md` is a short user-facing intro and is not authoritative.

## Build / Run / Test

PowerShell (Windows). All commands run from repo root.

```powershell
dotnet restore .\AI_Usage_Dashboard.sln
dotnet build   .\AI_Usage_Dashboard.sln -c Debug
dotnet run --project .\AI_Usage_Dashboard\AI_Usage_Dashboard.csproj   # serves on http://localhost:5088/

# tests (xUnit)
dotnet test .\AI_Usage_Dashboard.sln
dotnet test .\AI_Usage_Dashboard.Tests\AI_Usage_Dashboard.Tests.csproj --filter "FullyQualifiedName~DateRangeHelperTests"
```

Test coverage is currently only `DateRangeHelperTests`. The web project exposes internals to the test project via `InternalsVisibleTo` (see `.csproj`).

Frontend has **no build step** — JSX is compiled in-browser by Babel standalone. Edit files under `AI_Usage_Dashboard/wwwroot/*` and reload. There is no `wwwroot-src` or bundler; do not create one.

## Architecture — the four non-negotiable principles

These are project-level invariants. Every PR must respect them; if a task seems to require breaking one, stop and reconsider the design.

1. **Worker is pure raw sync.** `DataFetchWorker` and its sync services upsert API responses verbatim into `*_raw` collections. No derived fields, no joins, no business logic in the worker path.
2. **Dropdown / distinct values come from raw collections.** Names, model lists, capability options are resolved from `*_raw` — never from hardcoded C# dictionaries or enums.
3. **Frontend only renders.** Filtering, grouping, sorting, summing, enum→label resolution all happen server-side. Frontend state stores backend enums (e.g. `'7d'`, `'__custom__|2026-04-01|2026-04-30'`), not translated strings.
4. **Aggregation lives in MongoDB.** Prefer `$match` / `$group` / `$switch` / `$lookup` pipelines. Post-aggregate C# work is reserved for small-result enrichment (name lookups, token-share cost allocation).

When tempted to add a transform in the worker, `.GroupBy(...).Sum(...)` in C#, or `.reduce(...)` on the frontend — stop and ask if the same effect belongs in a Mongo aggregate or as a new raw upsert.

## Big-picture flow

```
OpenAI Admin API ─┐                ┌─► UsageReadService (Mongo Aggregate
Azure ARM / Cost ─┼─► *_raw upsert │   pipelines: $match/$group/$switch)
                  │   (DataFetch   ├─► Controllers (/v1/*)
                  │    Worker,     │
                  │    every 30m)  └─► wwwroot static SPA (React UMD + Babel JSX)
```

- **Raw collections** (`openai_usage_raw`, `openai_costs_raw`, `azure_metrics_raw`, `azure_cost_raw`, etc.) are append-via-upsert; their unique indexes (defined in `Data/MongoDbContext.EnsureIndexesAsync`) ARE the schema contract. Changing a sync's grouping dimensions almost always means changing the unique key too.
- **Typed collections** are only metadata: `budgets`, `alert_events`, `export_jobs`, `fetch_checkpoints`, `deprecation_catalog`, `system_logs`. Everything else is `BsonDocument`.
- **`fetch_checkpoints`** (id = source name) drives incremental sync. Reset endpoints under `/v1/maintenance/*` manipulate these to force re-fetch.
- **Read path** is centralized in `Services/UsageReadService.cs` — `AggregateOpenAiUsageAsync` and `AggregateAzureUsageAsync` are the two big pipelines. Name enrichment goes through `NameLookupService` (1-minute static cache from `*_raw`). OpenAI cost is allocated to models by token-share post-aggregate (the Costs API has no `group_by=model`).
- **Project namespace is `AI_Usage_Dashboard.*`** — keep namespaces aligned with the project name.

## Gotchas burned in by past bugs

These are real footguns confirmed in `SPEC.md` / memory. Apply them without re-deriving:

- **`endDate` is inclusive on the wire, exclusive internally.** All controllers funnel through `Utils/DateRangeHelper.ResolvePeriod`, which adds one day to convert. New endpoints accepting a date range MUST go through this helper — don't reimplement.
- **`azure_cost_raw.usageDate` is `Int64` (`yyyyMMdd`, e.g. `20260430`).** Compare with a number, never a string — string compare returns zero rows silently. ARM returns it as a number.
- **`azure_cost_raw.costCurrency` is always `"USD"`.** `costUSD` is pre-converted by Azure; the response's `currency` field is the subscription's billing currency, not the currency of `costUSD`. Don't confuse them.
- **Azure metric model fallback** in aggregates: `modelName → deploymentName → "azure-openai"` via `$switch`. Empty/`"__Empty"` values are normalised to `""` during sync.
- **Azure Monitor `metricnames` caps at 20 per call**, and metrics differ in which dimensions they support — `AzureMetricsRawSync` groups requests by `(supportsModelName, supportsModelDeploymentName)`. Don't merge those groups.
- **OpenAI Costs API has no `group_by=model`.** Model attribution is done post-aggregate by token-share allocation in `UsageReadService`.
- **Unique indexes on raw collections include all grouping dimensions.** Inserting a row missing a dimension violates the index. When adding a new dimension to a sync, update both the upsert key and the index in `MongoDbContext`.
- **SVG line charts:** for non-scrolling charts, let `width=100%` fill naturally — do not measure DOM width manually.
- **Flex containers + percent heights:** without an explicit child height, `alignItems: flex-end` collapses percent heights to 0. Set a concrete height on the child.

## Frontend conventions

- Single source of truth: `AI_Usage_Dashboard/wwwroot/*`. No build, no duplicated copy elsewhere.
- React 18 UMD + Babel 7 standalone via CDN. JSX files are loaded with `<script type="text/babel">`.
- `index.html` is the app shell with global state (`page`, `theme`, `lang`, `source`, `org`, `projectId`, `dateRange`, `textScale`, `tweaks`). Page switch is `page` state — no router.
- `window.API` (in `api.js`) is the only fetch wrapper; new endpoints should be added there.
- i18n via `window.TRANSLATIONS[lang]` (`i18n.js`); zh/en only. Components store backend enums in state and translate at render — never the reverse.
- Theme/density/accent come from `tweaks` (TweakPanel, localStorage-backed). Colors are CSS custom properties with `data-theme="dark"` toggle.

## Configuration & secrets

- `AI_Usage_Dashboard/appsettings.json` must contain safe defaults only. Real OpenAI admin keys, MongoDB connection strings, and Azure client secrets must be supplied through environment variables, .NET User Secrets, Key Vault, or the deployment platform's secret manager.
- Any credentials that previously appeared in local config or build outputs must be treated as exposed and rotated/revoked before publishing.
- Azure auth: if `AzureCost:TenantId/ClientId/ClientSecret` are all empty, code falls back to `DefaultAzureCredential` (requires `az login` locally).

## Maintenance endpoints (useful during dev)

| Operation | Endpoint |
|-----------|----------|
| Show sync state | `GET  /v1/maintenance/checkpoints` |
| Re-fetch today | `POST /v1/maintenance/reset-today` |
| Backfill N months | `POST /v1/maintenance/backfill?months=6` |
| Wipe checkpoints (first-run path) | `POST /v1/maintenance/full-reset` |
| Read Warning+ logs | `GET  /v1/maintenance/logs?level=warn` |

`system_logs` has a 30-day TTL; raw usage/cost/metrics collections have no TTL and accumulate indefinitely.
