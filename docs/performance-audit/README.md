# Nornis performance & operating-cost audit — 2026-07-26

A read-only sweep of the whole repository by seven parallel auditors, one per layer. No source
files were changed. Every finding was verified against the code; anything that could not be
confirmed from source is quarantined in each report's "Unverified" section.

| Report | Scope | H | M | L |
|---|---|---|---|---|
| [01](audit-01-persistence.md) | EF Core, repositories, migrations, indexes | 4 | 4 | 1 |
| [02](audit-02-application.md) | Application services, authorization, knowledge | 7 | 7 | 2 |
| [03](audit-03-ai.md) | Azure OpenAI — prompts, caching, model tiering | 5 | 7 | 4 |
| [04](audit-04-api.md) | Controllers, middleware, filters, background services | 5 | 6 | 5 |
| [05](audit-05-web.md) | Blazor Server client, render behaviour, assets | 5 | 5 | 2 |
| [06](audit-06-azure.md) | Blob, Service Bus, notifications, telemetry | 3 | 8 | 5 |
| [07](audit-07-worker-build.md) | Worker runtime, Docker, CI, test suite | 8 | 8 | 7 |
| **Total** | | **37** | **45** | **26** |

108 findings, roughly 90 unique once cross-reported items are merged.

---

## The one thing to take away

**The expensive findings compound.** They are not a list of independent problems — they are one
problem billed four times.

Trace a single idle browser tab:

```
NavMenu polls /sources/activity every 15s
        │
        ├─► the endpoint loads every Source row in the world, twice,
        │   including Body and DerivedText (nvarchar(max) transcripts)
        │   …to return six integers
        │
        ├─► prerendering runs the whole load set a second time on every full page load
        │
        ├─► the response travels uncompressed (nothing enables compression, either end)
        │
        └─► every request, every SQL query, every dependency call is ingested
            into App Insights at 100% sampling
```

One user action, four multipliers. This is why the fix order below is not simply "highest
severity first": a handful of trivial config changes sit underneath everything else and multiply
across every other finding. Land those before the medium-effort query rewrites, or you will
measure the query rewrites through four layers of noise.

Four independent auditors flagged the activity poll without seeing each other's work. It is the
single most-confirmed finding in the audit.

---

## Tier 0 — Reliability landmines (not performance; fix regardless)

These surfaced during the sweep and outrank the cost work.

### 0.1 The `library-indexing` queue is never provisioned, and one dead queue kills the whole worker
`scripts/provision-azure.ps1:127-141` creates and watches only `source-extraction`. The worker
registers a second processor on `library-indexing` (`src/Nornis.Worker/Program.cs:183-196`).
If that queue does not exist, `StartProcessingAsync` throws `MessagingEntityNotFound` out of
`LibraryIndexingWorker.ExecuteAsync`, and the default `BackgroundServiceExceptionBehavior.StopHost`
takes the **entire worker process** down — extraction included. On a `--min-replicas 0` app that
means every queue wake is an image pull, a crash, and a backoff, billed, with no work done.

Even if the queue was hand-created, the KEDA scale rule does not watch it: an uploaded PDF sits
in the queue until an unrelated extraction message happens to wake the worker.

**Check first:** worker logs for `MessagingEntityNotFound`.
**Fix:** create the queue, add a second scale rule, and set
`BackgroundServiceExceptionBehavior.Ignore` so one dead queue cannot kill the other worker.

### 0.2 `provision-azure.ps1` can no longer reproduce production
`src/Nornis.Worker/Program.cs:156-159` throws at startup when `BlobStorage:ConnectionString` is
empty. The script sets four env vars and that is not one of them. Prod has evidently been patched
by hand — so re-running the script is a landmine, not a recovery path.

**Fix:** reconcile the script against `az containerapp show -n ca-nornis-worker` and add the
missing secrets. Consider making blob storage lazily-failing rather than startup-fatal, so missing
library config degrades indexing instead of killing extraction.

### 0.3 Lock renewal is shorter than a long indexing run
`MaxAutoLockRenewalDuration` is 5 minutes (`WorkerOptions.cs:13`) and is shared by both processors.
A book-sized PDF is dozens of serial embedding round trips. Past 5 minutes the lock expires while
the handler is still running, Service Bus redelivers, and a second full indexing run starts **in
parallel with the first** — double-paying every embedding. `document.Status != Indexing` is not a
guard, because the status stays `Indexing` for the whole run.

**Fix:** per-queue renewal budgets. Extraction can stay at 5 minutes; indexing needs 30+. The cap
is a ceiling, not a cost, so raising it is free when runs are short.

### 0.4 The continuity-audit background service has no replica guard
`ContinuityAuditBackgroundService.cs:33-62` picks eligible worlds with a plain read-then-act — no
lock, no lease, no claim. Two API replicas ticking in the same window both call
`RunAssessmentAsync`, which is a **paid Azure OpenAI call**. Container Apps scales the API on HTTP
load, and even at `minReplicas: 1` a rolling deploy briefly runs two revisions. It also runs a
tick before the first delay, so every deploy triggers a sweep, and
`TimeSpan.FromHours(Math.Max(0.0, TickIntervalHours))` floors at zero — setting the interval to
`0` intending "off" produces a hot loop instead.

**Fix:** claim the work with a conditional UPDATE before running it; delay before the first tick;
floor the interval at a positive minimum. Treat non-positive as disabled, matching the convention
`AiBudgetOptions` already documents.

---

## Tier 1 — Trivial changes with a multiplier (do these first)

Ordered by payoff-per-hour. Every one is config or a handful of lines.

| # | Change | Where | Effect |
|---|---|---|---|
| 1.1 | Enable response compression, both ends | `Api/Program.cs`, `Web/Program.cs:118` | Repetitive JSON compresses 80–90%. Multiplies every other API finding. |
| 1.2 | Set App Insights `SamplingRatio = 0.10f` | all three `Program.cs` | Currently 100%: one ingested record per request, per SQL query, per blob GET, per Service Bus receive. |
| 1.3 | `MapStaticAssets()` instead of `UseStaticFiles()` | `Web/Program.cs:151` | ~1.03 MB of egress per cold page view. Brings precompression, ETags, immutable caching. |
| 1.4 | Turn off prerender for the authenticated shell | `Web/App.razor:20` | Halves the API load on every full page load. The prerendered output for authenticated pages is *literally a loading spinner* — `MainLayout.razor:38-47` gates the body behind it. |
| 1.5 | Cache the onboarding DTO in a scoped state holder | `TutorialChecklist.razor:124`, `OnboardingPrompt.razor:67` | `GET /api/onboarding` currently fires **four times** on a full load of `/`. |
| 1.6 | Add index on `SourceReferences(TargetId, TargetType)` | new migration | Confirmed: the table's only index is the FK on `SourceId`. Accepting a 50-proposal batch = 50 full scans of the highest-cardinality table in the schema. |
| 1.7 | Add index on `AiUsageRecords(WorldId, CreatedAt)` | new migration | `AiBudgetGuard` aggregates this table **before every AI call**. |
| 1.8 | Cap Ask session text; implement `MaxContextTokens` | `KeywordKnowledgeRetriever.cs:188`, `LoremasterService.cs:431` | Verified: `MaxContextTokens` is referenced in exactly one place — its own declaration. Nothing bounds the cost of a single question. Worst case ~75k input tokens ≈ $0.19/ask. |
| 1.9 | Set `MaxOutputTokenCount` per call site | `Infrastructure/Ai/*` | Output is $15/M — 6× the input rate — and is the *only* direction with no guardrail anywhere. |
| 1.10 | Cache the `ServiceBusSender` | both queue clients | An AMQP link attach/detach before every enqueue, in the user's request path. |
| 1.11 | Drop the pre-flight `ExistsAsync` on blob reads | `AzureBlobStorageService.cs:68,87` | 100% transaction overhead on upload-confirm; 200 wasted HEADs per world export. |
| 1.12 | `dotnet format --verify-no-changes` in CI | `.github/workflows/ci.yml` | Bare `dotnet format` rewrites the runner's tree and always exits 0 — the step verifies nothing. |

---

## Tier 2 — The real work, ranked

### 2.1 `/sources/activity` — the single biggest recurring cost *(medium)*
Flagged independently by four auditors. Every 15s, from every page, for every circuit,
unconditionally — with none of the "is anything actually processing" guard that `Sources.razor:180`
has. Each call loads every source row in the world **twice** (including `nvarchar(max)`
transcripts), plus 200 proposals, all batches, all artifacts, and — when an `UpdateFact` proposal
is pending — all facts at `int.MaxValue`. Then discards it and returns six integers.

Fix in two halves: replace the endpoint body with `GroupBy`/`Count` aggregate queries (the
visibility predicate has to move into SQL — cover it with a test per role first), and give the
client a backoff so an idle tab polls at 60s+ and a hidden tab not at all.

### 2.2 Auth plumbing costs 2–3 DB round trips per request *(small → medium)*
`UserProvisioningMiddleware` queries the user table on every authenticated request;
`WorldMemberActionFilter` queries membership; then ~20 sites in the application services
re-resolve the *same* membership a third time, though the filter already put it in
`HttpContext.Items`. Because every repository read is `AsNoTracking`, there is no identity map to
absorb the duplicate — each one is a real round trip.

Cache the `sub → User` mapping with a short TTL; thread the already-resolved role into the
commands that re-query it. **Route through `code-critic` — it touches authorization**, and note
that moving to the filter's role also applies the GM "view as player" downgrade to those
endpoints, which is the intended semantics but is a behaviour change.

### 2.3 The N+1 cluster *(mostly trivial, high volume)*
All the same shape — a point lookup per item where a batch method already exists on the
repository interface:

- `WorldService.ListForUserAsync:201` — a membership query per world on `GET /api/worlds`, which
  runs on essentially every page load. `IWorldMemberRepository.ListByUserAsync` already exists. **Trivial.**
- `ArtifactService.cs:190` and `:610` — one query per cited source *and* one per connected
  artifact, on the most-visited authenticated page, also served anonymously.
- `SourceKnowledgeService.cs:65-117` — a point lookup per provenance row (50–200 per session),
  plus O(n²) dedup scans over the accumulating lists.
- `SourceLocationService.cs:142` — ~50 round trips to produce ~4 rows. **Trivial.**
- `ArtifactRemovalService.cs:63` — 30 round trips for a confirmation dialog. **Trivial.**
- `ProposalApplicator.cs:605/635` — the same relationship query issued twice. **Trivial.**

### 2.4 Batch accept re-reads everything 3–4× *(medium, correctness-sensitive)*
One "accept all" click on a 50-proposal batch costs ~200 SQL round trips where ~55 would do, and
each apply reloads the entire world artifact table. Review is the core loop of the product.

Load once, pass down. **Do not hoist the artifact set without invalidating it after each create** —
the "Salt Factor" dedup bug documented at `ArtifactRepository.cs:99-101` returns if you get this
wrong. Route through `code-critic`.

### 2.5 Extraction's prompt is ordered backwards for caching *(small)*
Prompt caching matches on longest common prefix. The user message currently leads with the source
body (volatile, unique per call) and trails with the ~22,500-token world artifact catalog (stable
across every source in the world). So the catalog is re-billed at full rate on every extraction —
roughly $0.056 each, ~$11 across a 200-note import.

Invert the section order. Nothing in the system prompt depends on position; it refers to sections
by name. Also stabilise artifact ordering (sort by Id) so the prefix is byte-identical more often.

**Caveat worth respecting:** models weight late context more heavily, so this may *help*
extraction focus — or may slightly reduce duplicate-detection sensitivity. Side-by-side a handful
of real sources before rolling out. And note you cannot currently *measure* the win:
`AiUsageRecord` records `InputTokens`/`OutputTokens` only, with no cached/uncached split. Add that
column first.

### 2.6 Continuity audit is unattended recurring spend *(medium)*
Runs hourly across every world that has ever had an accepted proposal, sends the entire world
record uncapped (relationships and timeline sources have **no cap at all**), and has no
content-hash guard — so it re-analyses unchanged records roughly daily, forever, per world.
Estimated $0.10–0.15/world/day. It also silently eats the $2.00 daily world budget before the GM's
own extractions and asks get a chance at it.

The fingerprint is the high-value half and the delicate one: hash the actual rendered prompt text,
not a proxy. Hash too coarsely and real changes get skipped — the GM stops getting findings and
never notices.

### 2.7 Worker: no backoff, no checkpoint *(small → medium)*
A 429 from Azure OpenAI is answered with an *immediate* re-request — the textbook way to extend a
throttle window. Each redelivery re-runs the whole pipeline: blob reads, vision call, chat
completion, times `MaxDeliveryCount`. Separately, library indexing accumulates every embedding in
memory and persists only after the last batch, so any failure re-buys the whole document from
chunk zero.

Fix the backoff **before** raising `MaxConcurrentCalls` off 1 — otherwise more concurrency just
manufactures more 429s, and under the current retry behaviour that costs money rather than saving
time. Also raise worker memory before raising concurrency: three concurrent extractions each
buffering a full PDF into a 0.5 GiB container is a realistic OOM.

### 2.8 Over-fetching endpoints *(small → medium)*
- `GET /canon` returns the world's entire knowledge graph; its only consumer calls `.Take(3)`
  twice and renders six rows.
- 60 of 61 GET endpoints have no page limit. `GET /api/users` returns every user in the system to
  any authenticated caller.
- List queries materialize full entities: `SourceRepository.ListByWorldAsync` selects `Body` and
  `DerivedText` for a list DTO that reads neither.

---

## What is already right

Recorded so a later pass does not "fix" it:

- **The worker's core is sound** — genuinely event-driven off Service Bus, no polling, no idle
  burn; DI scope per message; poison messages completed rather than abandoned, so no infinite
  poison loop; extraction idempotency is careful and well-commented, and expensive derived text is
  persisted before extraction so a redelivery never re-buys the vision call.
- **The public Ask spend cap works.** Verified: the gate precedes the spend, a non-positive cap
  doubles as the feature switch, metering is sound, and there are two independent backstops (a
  daily world budget inside `AskAsync` and a 5/min/IP limiter). Only weakness is a check-then-act
  race, bounded by the limiter.
- **Cost telemetry is comprehensive** — 12 operation types, per-call token and USD capture, a
  budget guard on every path but one. Better than most codebases, and it is what made the AI audit
  possible.
- **No memory leaks in the web client.** Every `DotNetObjectReference`, JS instance, state-container
  subscription and `LocationChanged` handler is disposed, with `JSDisconnectedException` catches.
  The JS side matches.
- **Embeddings and vision reads are properly batched.** No one-at-a-time embedding anywhere.
- **JWT validation is local and cached** — no per-request network hop to Auth0.
- **`JsonSerializerOptions` are static everywhere**; no per-call `new Regex`; no `async void`; no
  blocking `.Result`/`.Wait()` on request threads; no string interpolation in log calls.
- **`WorldRepository.DeleteWorldGraphAsync`** is a model set-based cascade with a correct InMemory
  fallback — the pattern the other cascade paths should copy.

---

## Verify with live data before sizing

Most dollar estimates are parameterised on world size. Cheapest ways to turn them into
measurements:

1. **`AggregateByOperationTypeAsync` already holds real per-operation token counts.** One query
   ranks the AI findings against each other by actual spend. Do this before acting on 2.5 or 2.6.
2. `SELECT AVG(DATALENGTH(Body) + DATALENGTH(DerivedText)), COUNT(*) FROM Sources GROUP BY WorldId`
   — sizes the activity-poll finding precisely, and tells you whether it is a $10/month problem or
   a $200/month one.
3. **API replica count** — `az containerapp show -g rg-nornis -n ca-nornis-api --query
   properties.template.scale`. Finding 0.4's duplicate-LLM-spend risk depends on it.
4. **Prod queue properties** — `MaxDeliveryCount` and `LockDuration` on `source-extraction`. The
   blast radius of the no-backoff retry loop is 3× or 10× depending on this.
5. **Whether `library-indexing` exists in prod at all** (finding 0.1).
6. **Whether the worker is emitting telemetry.** `Program.cs:30` gates all OTel on
   `APPLICATIONINSIGHTS_CONNECTION_STRING`, which provisioning never sets — the worker may be
   silent, which would explain why none of this has been visible.

---

## Suggested sequence

1. **Tier 0** — reliability, in order. 0.1 first; it may be actively down.
2. **Tier 1** — one PR of trivial multipliers. Compression, sampling, static assets, prerender,
   onboarding cache, two indexes, the AI ceilings. Cheap, independent, low-risk.
3. **Measure.** With sampling and compression in place, re-read the numbers before touching queries.
4. **Tier 2.3** — the N+1 cluster. Mostly trivial, mechanical, and the batch methods already exist.
5. **Tier 2.1** — the activity endpoint. Highest single payoff of the medium-effort work.
6. **Tier 2.2, 2.4** — auth threading and batch accept, both through `code-critic`. These touch
   authorization and the apply path.
7. **Tier 2.5–2.8** — AI prompt ordering, the audit fingerprint, worker backoff, endpoint limits.

Nothing in Tier 1 depends on anything in Tier 2, so the first PR can go out immediately.

---

> Produced by seven parallel AI auditors reading the source, with findings cross-checked against
> each other and the headline claims re-verified by hand. Each finding cites file and line — verify
> against a profiler or query log before optimising, and re-run the suite: several fixes touch
> authorization and visibility logic.
