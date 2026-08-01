# System status

> Part of the Nornis backlog. This file is a spec, not authorization: execute only
> through the Execution order in `docs/future-features.md`, which holds sequencing,
> completion status, and the Opus/Fable gate.

2026-07-31. Companion to the test-quality plan above — same dashboard, different
signal: is the system up, and are its dependencies healthy. Current state: the API
already runs Microsoft.Extensions.Diagnostics.HealthChecks with exactly one check
(`PendingMigrationsHealthCheck`) mapped anonymously at `/health` and watched by the
availability alert; the Worker is a generic host with no HTTP surface at all; Web has
no health endpoint.

Two decisions frame everything below:

- **`/health` does not change meaning.** The availability alert and the post-deploy
  migration window both read a `/health` failure as "the deploy is broken." Dependency
  probes live on a separate endpoint so a transient Service Bus blip can never
  impersonate a missed migration.
- **The ops surface is called "status," never "health."** In Nornis vocabulary,
  "health" already means *continuity* health of a world (`HealthController`, the GM
  assessment UI). Endpoint, page, and nav all say "status"; the product concept keeps
  its word.

## Endpoints

- `/health` (API): as today — pending-migrations, tagged `live`. Untouched.
- `GET /status` (API, new): runs the checks tagged `deps` below. Anonymous — the
  steering doc already names "approved `/status`" in its anonymous carve-out. Custom
  response writer emits aggregate + per-check `{name, status, durationMs}` **only** —
  never exception text, connection strings, or hostnames; the payload is public.
  Default status-code mapping stays (Unhealthy → 503); the page reads the JSON body
  either way, and a future alert can use the code.
- CORS: allow the dashboard origin, for `/status` only. Confirm the rate limiter
  bucket tolerates one fetch per page view.
- `/health` (Web, new): trivial anonymous liveness — no dependency checks; Web is a
  UI shell over the API. Gives App Service something to ping.

## Checks (all tagged `deps`)

- **sql** — `AspNetCore.HealthChecks.SqlServer`. Pending-migrations already implies
  connectivity, but a separate row makes "DB down" and "migration missed" read as
  different failures on the page.
- **blob-storage** — `AspNetCore.HealthChecks.Azure.Storage.Blobs`.
- **service-bus** — `AspNetCore.HealthChecks.AzureServiceBus`.
- **azure-openai** — passive, never an active probe: a probe is a paid call, and a
  scrape cadence would buy nothing. An in-process recorder notes each AI call's
  outcome at the same seam where the usage ledger already writes; the check reports
  Healthy on recent success, Degraded when the last N calls all failed, and
  Healthy-idle when there's been no recent traffic.
- **worker-heartbeat** — the highest-value check in the set, and the one that needs a
  schema touch. The Worker has no HTTP, so it writes a heartbeat instead: a one-row
  table (additive migration) updated every ~60s by a hosted service; the API-side
  check reads freshness — Degraded past ~2 minutes, Unhealthy past ~5. Today a dead
  Worker means sources sit "Queued" silently; this check is what makes that visible,
  and later alertable. One process-level heartbeat covers both hosted services
  (extraction, library indexing) unless they ever ship separately.

## The page

- "System status" on the dashboard site from test-quality phase 2: client-side fetch
  of `/status`, one tile per check plus an aggregate banner.
- **Unreachable is a state.** A failed fetch renders "API unreachable" as the loudest
  tile — that is the most important thing the page can ever say, not an empty screen.
- **Hosted off Azure — GitHub Pages, decided 2026-08-01.** This is the requirement that
  settles the test-quality plan's hosting choice for the whole dashboard. The page's
  entire value in a real outage is being reachable when the thing it reports on is not,
  and the `$web` container shares a storage account with the application: one regional
  failure and the status page dies alongside the system it was meant to explain.
  Note precisely what this buys and what it does not — during an Azure outage the page
  still cannot reach `/status`, so it renders "API unreachable" and nothing more
  detailed. That is the point. It loads from somewhere that is not broken and says so.
- Live-only, no history: App Insights availability tests already record uptime over
  time. Link out to that; do not rebuild it on the history branch. The link is *not* an
  outage fallback — App Insights is Azure, and is dark in the same failure.
- Sequencing: the endpoint work is independent and useful bare (curl-able) — it can
  land before the dashboard exists. The page needs phase 2's site.

## What does not change

- `/health` semantics, the availability alert watching it, and the post-deploy
  migration health window stay exactly as documented.
- No auth model changes: `/status` joins `/health` in the anonymous carve-out the
  steering doc already defines. Everything else on the API stays authenticated.
