## List of unprocessed features

* When logged in as the GM, I'd like a right-bar feature to add GM notes on the Ask response page. This is intended to allow a GM to correct findings when asking questions to ensure proper narrative structure.
* Perform a comprehensive review of the entire repo and identify opportunities to improve the overall system to make it something I can proudly post on my profile rather than having something that looks obviously vibe-coded. This should include enforcing SOLID and Clean Code principles.

---

# Performance & operating-cost remediation plan

Derived from the audit of 2026-07-26. Full findings, with file/line citations, code sketches and
per-item risk notes, live in [`docs/performance-audit/`](performance-audit/README.md) — this is the
execution order, not the evidence.

**Sequencing rule:** phases 0 and 1 are independent of everything below them and can ship
immediately. Do not start phase 3 or later before phase 2, or you will measure query rewrites
through four layers of noise.

**Why the order looks odd.** The expensive findings compound rather than sit side by side: the nav
poll fires → prerendering doubles the load set → the response is uncompressed → and every SQL query
underneath is ingested at 100% sampling. One user action, billed four ways. The trivial config
changes in phase 1 sit underneath everything else and multiply across it, so they come before the
larger query work despite being smaller.

## Phase 0 — Reliability landmines — DONE 2026-07-27

Not performance. Completed as one change; `dotnet build Nornis.sln` clean (0 warnings) and all
2,911 tests green.

**Production was healthy — the audit overstated this.** Read back from Azure before touching
anything: the `library-indexing` queue exists (MaxDeliveryCount 5, LockDuration PT1M, TTL P14D),
both KEDA scale rules exist, `BlobStorage__ConnectionString` and
`APPLICATIONINSIGHTS_CONNECTION_STRING` are both set on the worker, and the worker revision is
Healthy at zero replicas. Nothing was crash-looping. The defect was confined to
`provision-azure.ps1`, which could not reproduce any of that — so the exposure was "the next
re-provision breaks prod", not "prod is broken". The API also runs `minReplicas: maxReplicas: 1`,
which narrows the duplicate-assessment window to rolling deploys rather than steady state.

- [x] Checked live state instead of guessing. Queue, scale rules, env vars, revision health, and
      replica bounds all read back from Azure; findings above.
- [x] `library-indexing` added to `scripts/servicebus-emulator.json` (local dev could not exercise
      the path at all), with `source-extraction` realigned to production's real properties.
      The live queue and scale rule already existed, so no Azure mutation was needed.
- [x] `BackgroundServiceExceptionBehavior.Ignore` set in `src/Nornis.Worker/Program.cs`, so one
      dead queue can no longer stop the other processor.
- [x] `scripts/provision-azure.ps1` reconciled against live. It was worse than the audit found:
      `$ServiceBusRg` pointed at `rg-chronicis-dev` when the namespace lives in `rg-nornis`;
      neither queue was ever created; and `Extraction__AiTimeoutSeconds` is **180** in production
      against an appsettings default of 60 — re-running the script would have silently reverted the
      AI timeout and reintroduced spurious extraction timeouts. Now also sets BlobStorage and
      Application Insights on all three apps.
- [x] Worker blob storage is now a lazily-throwing registration mirroring the API, so missing
      library config degrades indexing instead of preventing startup.
- [x] Separate lock-renewal budgets: `LibraryMaxAutoLockRenewalDuration` (30 min) for indexing,
      5 min unchanged for extraction.
- [x] Continuity-audit claim: additive `World.ContinuityAuditClaimedAt` column, migration
      `20260727161926_AddWorldContinuityAuditClaim`, and
      `IWorldRepository.TryClaimContinuityAuditAsync` — a conditional UPDATE where the predicate
      *is* the lock. Covered by an 8-host concurrency test asserting exactly one winner.
- [x] Migration applied to `nornis-db` on 2026-07-27, ahead of the image deploy as the repo
      convention requires. It was the only pending migration; zero pending afterwards. The column
      is nullable and unread by the current production image, so the old code runs unaffected
      against the new schema until the deploy lands.
- [x] Continuity loop delays before its first tick, and non-positive `TickIntervalHours` now
      disables the trigger rather than producing a delay-free sweep.

**Still open, carried out of Phase 0:**

- [ ] **Run `code-critic` over this change.** It touches a migration and a concurrency primitive,
      so the repo's own rule calls for an independent review. The attempt on 2026-07-27 failed —
      model usage credits exhausted — so this change has *not* had a second pair of eyes.
- [ ] Apply the Auth0 settings the script still cannot reproduce (documented in its header). They
      exist only on the live container apps — not in the repo, not in user-secrets — so a
      re-provision today still yields apps that cannot authenticate.
- [ ] Decide on `WebPush:PublicKey` / `WebPush:PrivateKey`: present in the Api and Worker
      user-secrets stores, set on neither live app, so browser push notifications are inert in
      production. Either wire them up or remove the feature's config.
- [ ] Consider whether `ASPNETCORE_ENVIRONMENT=Development` on the live apps is still wanted. The
      dev-auth bypass cannot engage (it is additionally gated on the placeholder Auth0 domain), but
      the environment name still affects error detail and host defaults. Left unchanged
      deliberately — it deserves its own decision, not a side effect of provisioning.

## Phase 1 — Trivial multipliers (single PR)

All config or a handful of lines. Independent of each other and of everything below.

- [ ] Enable response compression on both `Nornis.Api` and `Nornis.Web`, and set `AutomaticDecompression` on the typed client so it actually sends `Accept-Encoding`.
      Repetitive JSON compresses 80–90%; this multiplies every other API finding.
- [ ] Set `SamplingRatio = 0.10f` on all three `UseAzureMonitor()` calls. Currently unconfigured, so
      every request, every EF query, every blob range GET and every Service Bus receive is an
      ingested record. Update any KQL that counts raw rows to apply `itemCount`.
- [ ] Replace `UseStaticFiles()` with `MapStaticAssets()` and switch the hand-rolled `?v=` cache
      busters to `@Assets[...]`. ~1.03 MB of egress per cold page view. Convert every `<link>`/
      `<script>` in `App.razor` together — `Assets[...]` throws for a path not in the manifest.
- [ ] Turn off prerendering for the authenticated shell. It runs every page's data load twice, and
      the prerendered output for authenticated pages is literally a loading spinner — `MainLayout`
      already gates the body behind `Worlds.Ready`. Consider keeping prerender for the public
      `/w/{slug}` routes, which render real content and want to be indexable.
- [ ] Cache the onboarding DTO in a scoped state holder. `GET /api/onboarding` currently fires four
      times on a full load of `/`. Invalidate on dismissal.
- [ ] Add an index on `SourceReferences(TargetId, TargetType)`. Verified: the table's only index is
      the FK on `SourceId`, so accepting a 50-proposal batch is 50 full scans of the
      highest-cardinality table in the schema.
- [ ] Add an index on `AiUsageRecords(WorldId, CreatedAt)`. `AiBudgetGuard` aggregates this table
      before *every* AI call.
- [ ] Cap per-session text in the Ask prompt and implement `MaxContextTokens`. Verified dead config:
      referenced in exactly one place, its own declaration. There is currently no upper bound on the
      cost of a single question — which is the guarantee the public Ask cap needs to be predictable.
      Truncate on a paragraph boundary and mark the cut so the model knows the record continues.
- [ ] Set a deliberate `MaxOutputTokenCount` at every call site. Output is $15/M — six times the
      input rate — and is the only direction with no guardrail anywhere. Size each from observed p99
      in `AiUsageRecords`; a truncated response surfaces as a parse failure rather than a silent
      overcharge.
- [ ] Cache the `ServiceBusSender` in both queue clients instead of creating and disposing one per
      message. Implement `IAsyncDisposable` so the link closes at shutdown.
- [ ] Drop the pre-flight `ExistsAsync` on blob reads and let the 404 surface. 100% transaction
      overhead on upload-confirm; 200 avoidable HEADs per world export.
- [ ] Change CI to `dotnet format --verify-no-changes`. Bare `dotnet format` rewrites the runner's
      tree and always exits 0, so the step verifies nothing. Expect the first run to fail loudly.

## Phase 2 — Measure

With sampling and compression in place, the numbers mean something. Do this before touching queries.

- [ ] Run `AggregateByOperationTypeAsync` — it already holds real per-operation token counts, and
      one query ranks every AI finding by actual spend.
- [ ] `SELECT AVG(DATALENGTH(Body) + DATALENGTH(DerivedText)), COUNT(*) FROM Sources GROUP BY WorldId`
      — sizes the activity-poll finding precisely, and says whether it is a $10/month or a
      $200/month problem.
- [ ] Read back the live Container App config (replica counts, memory, env vars, scale rules) and
      the prod queue's `MaxDeliveryCount` and `LockDuration`. The retry-loop blast radius is 3× or
      10× depending on the latter.
- [ ] Confirm whether the worker is emitting telemetry at all — all OTel is gated on
      `APPLICATIONINSIGHTS_CONNECTION_STRING`, which provisioning never sets. If it is silent, that
      explains why none of this has been visible.
- [ ] Add a `CachedInputTokens` column to `AiUsageRecord`. Without it the phase 5 prompt-cache
      reordering cannot be measured.

## Phase 3 — The N+1 cluster

All the same shape: a point lookup per item where a batch method already exists on the interface.
Mechanical, low risk, high volume. Good first slice for the `implementer` subagent.

- [ ] `WorldService.ListForUserAsync` — a membership query per world on `GET /api/worlds`, which runs
      on essentially every page load. `IWorldMemberRepository.ListByUserAsync` already exists.
- [ ] `SourceLocationService.BuildLocationsAsync` — ~50 round trips to produce ~4 rows.
- [ ] `ArtifactRemovalService.PreviewAsync` — 30 round trips for a confirmation dialog.
- [ ] `ProposalApplicator` `PartOf` branch — the identical relationship query issued twice.
- [ ] Thread the already-loaded `Source` into `ProposalApplicator.ApplyAsync` and delete the seven
      per-arm re-fetches. Both callers already hold it.
- [ ] `ArtifactService.GetDetailAsync` — one query per cited source *and* one per connected artifact,
      on the most-visited authenticated page, also served anonymously.
- [ ] `SourceKnowledgeService` — a point lookup per provenance row (50–200 per session) plus O(n²)
      dedup scans. Preserve first-reference-wins ordering for the displayed quote.
- [ ] Split `HealthService` into a pure scorer over already-loaded collections plus a thin loader, so
      the continuity audit stops loading the whole world graph twice. Note the two paths apply
      *different* filters — getting that wrong silently changes the published health score.

## Phase 4 — The activity endpoint

The single biggest recurring cost, flagged independently by four auditors.

- [ ] Replace the endpoint body with `GroupBy`/`Count` aggregate queries — no entity materialization.
      The in-memory visibility predicate must move into SQL exactly, or badge counts leak private
      sources. **Write the per-role tests (GM / Player-owner / Player-other / Observer) first.**
- [ ] Add a projection so `ListReviewQueueAsync` stops loading whole sources for an id list, and kill
      the duplicate full-source load in the same request.
- [ ] Give the client a freshness window, back off to 60s+ when nothing is in flight, and stop
      polling when the tab is hidden. `Sources.razor` already has the guard NavMenu lacks.
- [ ] Apply the same treatment to the tutorial-checklist detectors, which re-run full-table scans
      every 15 seconds during onboarding.

## Phase 5 — Authorization-sensitive and correctness-sensitive

Route both through `code-critic` — independent read, no memory of having written it.

- [ ] Cache the `sub → User` mapping in the provisioning middleware with a short, deliberate TTL, and
      thread the already-resolved `WorldMember`/role into the ~20 services that re-query it. Keep the
      role parameter non-nullable so a caller cannot forget it. **Behaviour change worth stating:**
      the filter applies the GM "view as player" downgrade and the services' own lookup does not, so
      moving to the filter's role extends view-as-player to those endpoints — which is the documented
      intent, but is a change.
- [ ] Hoist the reads out of batch accept: load proposals once via a new `ListByIdsAsync`, memoize
      batch and source, and pass the loaded batch into `UpdateBatchLifecycleAsync`. Keep the
      per-proposal transaction boundary and keep the re-read on the retry pass, which exists because
      intra-batch state changes. **The hoisted artifact set must be invalidated after each create or
      the "Salt Factor" dedup bug returns.**

## Phase 6 — AI spend and worker behaviour

- [ ] Invert the extraction user message so the stable world catalog leads and the volatile source
      body trails, and sort artifacts by Id so the prefix is byte-identical more often. Biggest single
      AI saving. **Side-by-side a handful of real sources first** — models weight late context more
      heavily, so this may help extraction focus or may dull duplicate detection.
- [ ] Apply the same prefix ordering to the relationship-backfill sweep, which re-bills an identical
      catalog once per source across the whole sweep.
- [ ] Add a content fingerprint to the continuity audit and skip the run when nothing changed. Hash
      the actual rendered prompt text, not a proxy — hash too coarsely and real changes get skipped,
      the GM stops getting findings, and nobody notices. Also cap the record (relationships and
      timeline sources have no cap at all) and bound the per-tick sweep with jitter.
- [ ] Replace the bare `AbandonMessageAsync` with a delivery-count-proportional backoff via scheduled
      re-enqueue. A 429 is currently answered with an immediate re-request. Carry an explicit attempt
      counter, since re-enqueue resets `DeliveryCount` and would otherwise remove the DLQ backstop.
- [ ] Persist library-indexing chunks per batch and resume from the highest stored `Ord`, so a
      failure stops re-buying the whole document. Key the resume on a content hash so a re-upload
      does not resume onto stale vectors.
- [ ] Only then raise `MaxConcurrentCalls` off 1 — and raise worker memory first. Three concurrent
      extractions each buffering a full PDF into a 0.5 GiB container is a realistic OOM, and more
      concurrency without the backoff fix just manufactures more 429s.
- [ ] Add a shared `TransientFailureClassifier` keyed on typed status codes. Both services currently
      substring-match exception messages, and disagree with each other.
- [ ] Store a content hash on `LibraryDocument` and short-circuit `ReindexAsync` when it matches.
      Always leave a force-reindex escape hatch.
- [ ] Move world-name generation off the premium Ask deployment onto a cheap keyed client, and give
      it an `AiUsageRecord` — it is the only model call in the codebase whose spend is invisible.
- [ ] Re-check the budget guard per chunk in the storyline retrospective, and persist verdicts per
      chunk so a late failure does not discard work already paid for.

## Phase 7 — Endpoint shape and payload

- [ ] Add `limit`/`kind` to `GET /canon`. Its only consumer calls `.Take(3)` twice and renders six
      rows; today it returns the world's entire knowledge graph.
- [ ] Add projections to list-shaped repository methods so list endpoints stop selecting `Body` and
      `DerivedText` for DTOs that read neither.
- [ ] Add keyset paging to the unbounded list endpoints, prioritising sources, artifacts, canon and
      users. Audit each consumer in the same change — `Home.razor` currently assumes it receives
      everything.
- [ ] Require a search term and cap results on `GET /api/users`, which today returns every user in
      the system to any authenticated caller.
- [ ] Add output caching to the anonymous public GETs — the most exposed surface and the one whose
      data changes least often. Never on `Ask`. Tag-evict on the public-access and demo kill switches.
- [ ] Compose the dashboard and source-detail fetches into single endpoints, built by calling the
      same application services so there is one authorization implementation. `Task.WhenAll` on
      `SourceDetail`'s serial waterfall is the cheap interim step.

## Deferred — real but low payoff

Recorded so they are not re-discovered: `Virtualize` on long lists, search debounce intervals,
self-hosting Google Fonts, memoizing `StorylineTimelineChart.Rows`, blob lifecycle/tier policy,
`WebPushClient` HttpClient reuse, central package management, test-suite parallelisation, Docker
buildx bake, NuGet caching in CI, deleting the dead `WorldMemberFilter`, and caching the demo
template zip. Each is written up with a fix sketch in the audit.

**Do not treat as a quick win:** enabling `EnableRetryOnFailure` looks trivial but throws at runtime
on the five explicit-transaction call sites unless each is wrapped in an execution strategy.