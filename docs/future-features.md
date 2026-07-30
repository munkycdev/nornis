## List of unprocessed features

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

- [x] **Run `code-critic` over this change.** It touches a migration and a concurrency primitive,
      so the repo's own rule calls for an independent review. The attempt on 2026-07-27 failed —
      model usage credits exhausted — so this change has *not* had a second pair of eyes.
- [x] ~~Apply the Auth0 settings the script still cannot reproduce~~ — **DONE 2026-07-29.** The
      premise needed one correction: the live web app already held the client secret as a proper
      container-app secret (`auth0-client-secret`) — the values were only missing from the *repo's*
      sourcing. The script now takes the four non-secret values (domain, audience, claims
      namespace, client id) as parameters defaulting to the live tenant, and reads the client
      secret from a new `Nornis.Web` user-secrets store (`UserSecretsId: nornis-web`), which was
      seeded from the live app. The API gets `Auth0__Domain/Audience/ClaimsNamespace`; the web app
      gets the full OIDC set with the secret as a secretref under the same name the live app uses,
      so a re-provision converges instead of forking. No Azure mutation was needed — live state
      was already correct; only the script could not reproduce it.
- [x] ~~Decide on `WebPush:PublicKey` / `WebPush:PrivateKey`~~ — **Decided: removed, 2026-07-29.**
      The whole feature, not just the config: controller, sender, options, `ExtractionNotifier`
      chain, `PushSubscription` entity/repository/table, client subscribe UI, `push.js`,
      `service-worker.js`, the `WebPush` package, and the user-secrets keys on both stores.
      Migration `20260730023525_RemovePushSubscriptions` drops the table — **apply it AFTER the
      images deploy, not before**: the repo's usual pre-deploy order exists for additive changes,
      and a drop inverts it (the old image serves `/api/notifications` until the deploy lands).
      If push ever comes back, the notification itself is ~200 lines against this commit; the
      volume policy in `ExtractionNotifier`'s doc comment is the part worth re-reading first.
- [x] ~~Consider whether `ASPNETCORE_ENVIRONMENT=Development` on the live apps is still wanted.~~
      **This premise was wrong.** Verified 2026-07-27: both `ca-nornis-api` and `ca-nornis-web`
      already run `Production`, and the worker never set the variable at all. In Phase 0 I listed
      the env var *names* and inferred the value from the stale provisioning script instead of
      reading it — an assumption that then propagated into two later conclusions.
      The script itself was the real defect and is now fixed: it would have downgraded a running
      deployment to Development on the next provision, re-exposing Swagger on the public API and
      turning off HSTS, the custom error page, and immutable asset caching.

## Phase 1 — Trivial multipliers — DONE 2026-07-27 (11 of 12)

`dotnet build Nornis.sln` clean (0 warnings), all 2,922 tests green, and the Web app smoke-tested
in a browser: page renders, fingerprinted asset URLs resolve, zero console errors.

- [x] Response compression on `Nornis.Api` and `Nornis.Web`, plus `AutomaticDecompression` on the
      typed client — without which the API's compression would have been dead weight on that leg,
      since the default handler never sends `Accept-Encoding`.
- [x] Telemetry sampling. **Deviation, deliberate:** API and Web are at 0.10, the worker is left at
      1.0. The two web hosts' volume scales with open browser tabs; the worker's scales with queue
      depth, which is bounded and low, and its traces are the most diagnostically valuable in the
      system — one record per extraction, covering a paid AI call. Sampling it would have saved
      almost nothing and cost real debuggability. All three read `Telemetry:SamplingRatio` so the
      decision is revisitable without a code change.
- [x] `MapStaticAssets()` with `@Assets[...]` throughout `App.razor`. All 15 asset paths verified
      against the generated endpoint manifest before shipping, since an unknown key throws at
      render time and would 500 every page. Build manifest confirms gzip variants (app.css
      90,956 → 16,880 bytes).
- [x] Prerender waste eliminated **without** disabling prerendering. Guarded the two loads that
      fire during the throwaway pass (`NavMenu`, `TutorialChecklist`) on `RendererInfo.IsInteractive`
      — already the house pattern, see `SessionWrapUpCard`. Turning prerender off wholesale would
      have cost SEO and link-unfurling on the public `/w/{slug}` pages, which use `PublicLayout`
      and render real content; the authenticated shell renders only a boot spinner, which is why
      its prerender pass is pure waste.
- [x] `OnboardingState` scoped cache shared by `TutorialChecklist` and `OnboardingPrompt`, with
      invalidation on dismissal and on marking the prompt seen. Failures are deliberately not
      cached — a transient error would otherwise hide the tutorial for the whole circuit.
- [x] Index on `SourceReferences(TargetId)`. **Narrowed from the plan's `(TargetId, TargetType)`:**
      `TargetType` is an enum stored as string with no declared length, so it is `nvarchar(max)`,
      and including it forced EF to scaffold an `ALTER COLUMN` to `nvarchar(450)` — a blocking
      table rewrite, and this repo requires additive migrations because they run against the live
      database before the new images deploy. `TargetId` is a Guid identifying one fact,
      relationship or proposal, so a seek returns a handful of rows and the type filter is free.
- [x] Index on `AiUsageRecords(WorldId, CreatedAt)`. The scaffolded migration was hand-edited to
      create the composite *before* dropping the now-redundant `WorldId` index; EF emits the drop
      first, which would leave the budget guard's lookups unindexed for however long the composite
      takes to build on a live table.
- [x] Ask prompt is now bounded. `MaxSessionChars` (4,000) truncates each session record on a
      paragraph or word boundary with an explicit marker, and `MaxContextTokens` is finally
      enforced — sections are emitted in descending value and stop at the budget, so quotes and
      library passages are the first to go. Both parameters default to unlimited so the existing
      formatting tests stay meaningful.
- [x] ~~`MaxOutputTokenCount` on all eleven model call sites~~ — **REVERTED 2026-07-27, this
      change broke production.** `Azure.AI.OpenAI` 2.1.0 (still the latest release) serialises the
      property as `max_tokens`, and the gpt-5.4 deployments reject it:
      `HTTP 400 (invalid_request_error: unsupported_parameter) Parameter: max_tokens —
      'max_tokens' is not supported with this model. Use 'max_completion_tokens' instead.`

      It fails before any tokens are spent, so there is no degraded mode: every AI feature except
      embeddings stopped working from the deploy until the revert. There is no SDK version that
      emits the correct parameter, so an output ceiling is simply not expressible today.

      The same 400 had been failing world-name generation silently since 2026-07-26 — that call
      already set `MaxOutputTokenCount = 20` and swallows failures into a static-name fallback,
      which is why it went unnoticed and why those fallbacks were misattributed to timeouts. Its
      `Temperature = 1.2f` was removed at the same time; this model family rejects a non-default
      value the same way, so it would have been the next 400.

      Guarded by `UnsupportedChatParameterTests`, which fails if either parameter is re-added.
      A source scan on purpose: the failure only reproduces against a live deployment, so no
      ordinary unit test would catch the assignment.

- [ ] Restore an output ceiling once the SDK emits `max_completion_tokens`. Output is still
      billed at six times the input rate with no guardrail. Verify against a real deployment
      before shipping — that is the step this incident skipped.
- [x] `ServiceBusSender` cached per queue client via `Lazy<>` with `IAsyncDisposable`, replacing an
      AMQP link attach/detach on every enqueue.
- [x] Pre-flight `ExistsAsync` removed from blob metadata and read paths — it issued its own Get
      Blob Properties request, so the pair cost two billed transactions to answer one question.
      A 404 is now distinguished from a real fault, so a 403 or a throttle no longer masquerades
      as "the upload never arrived".

**Not done — `dotnet format --verify-no-changes` in CI.** Attempted and reverted. The repo has
never satisfied the analyzers: `--verify-no-changes` reports ~37,000 findings across 897 files
(mostly IDE0161 file-scoped namespaces, plus CHARSET/BOM issues that even `dotnet format
whitespace` trips on). Flipping the flag would fail every PR immediately. `ci.yml` now carries a
comment stating plainly that the step is a no-op and why, instead of looking like a real check.

- [ ] Land the formatting check as its own commit: run `dotnet format` across the tree, commit the
      churn alone so it stays reviewable, then switch `ci.yml` to `--verify-no-changes
      --no-restore`. Doing it inside a feature branch would bury the real diff — and with several
      agents sharing this working directory, an 897-file reformat needs a quiet moment.

**Correction (2026-07-27), previously recorded here as a caveat:** I wrote that the static-asset
win was half-inert because the live apps ran `ASPNETCORE_ENVIRONMENT=Development`. Both parts of
that were wrong. The apps run `Production`, and the `Cache-Control: no-cache` I measured was on
`/app.css` — the *non-fingerprinted alias*, which is supposed to be uncacheable because its
content changes under a fixed URL. The page loads the fingerprinted asset, and that one returns
`max-age=31536000, immutable` with brotli, verified against production:

```
app.mxl21wfaft.css  cache-control: max-age=31536000, immutable  content-encoding: br  13,831 bytes
/app.css            cache-control: no-cache
```

Phase 1's static-asset work is delivering in full — compression *and* immutable caching.

## Phase 2 — Measure — DONE 2026-07-27

> **Read this before starting Phase 3.** The measurements re-rank everything below them, and the
> headline is uncomfortable: at current scale the remaining phases buy *latency and headroom*,
> not money. The money findings were telemetry ingestion and egress, and both already shipped.

### Scale of the database

| | |
|---|---|
| Worlds | 6 |
| Sources | 124 |
| Artifacts | 613 |
| SourceReferences | 4,722 |
| AiUsageRecords | 232 |
| Total source text (`Body`+`DerivedText`) | 1,575 KB |

Source text is overwhelmingly concentrated in one world:

| World | Sources | Total | Avg | Max |
|---|---|---|---|---|
| Symbaroum | 89 | 1,482 KB | 16 KB | 185 KB |
| The Veiled Vale | 11 | 32 KB | 2 KB | 5 KB |
| Stormlight Archive | 7 | 29 KB | 4 KB | 6 KB |
| Vespergale Reach | 10 | 26 KB | 2 KB | 4 KB |

**What this does to the activity-poll finding (Phase 4).** It is real but local: on Symbaroum the
endpoint drags ~1.48 MB twice per poll, four polls a minute, so one open tab costs on the order of
**700 MB/hour of pure waste** — and nothing on any other world. It is a DTU and latency problem
concentrated in the one world that matters, not a bill.

### AI spend — $11.06 total, over 25 days (2026-07-02 → 07-27)

| Operation | Calls | Avg in | Avg out | Max out | Cost |
|---|---|---|---|---|---|
| SourceExtraction | 110 | 16,634 | 3,109 | 6,547 | **$9.69** |
| AskLoremaster | 19 | 13,795 | 315 | 839 | $0.69 |
| ContinuityAudit | 10 | 18,561 | 936 | 2,237 | $0.54 |
| MapExtraction | 2 | 8,728 | 2,049 | 3,011 | $0.11 |
| ContinuityFix | 3 | 1,723 | 234 | 365 | $0.02 |
| Embedding | 86 | 728 | — | — | $0.0013 |

Four failures in 232 calls. **Extraction is 88% of all AI spend**, which is what makes the
prompt-cache reordering the right remaining AI target — but the whole AI bill is **~$13/month**,
so that work is worth perhaps $1–2/month today. Its value is that it scales with usage, not that
it pays now. Embeddings are free in practice, exactly as the audit predicted.

**Phase 1's output ceilings are correctly sized** — every observed maximum sits well under its
cap (extraction 6,547 vs 16,000; Ask 839 vs 1,500; audit 2,237 vs 4,000; map 3,011 vs 4,000). No
legitimate response is at risk of being clipped.

**Phase 1's session cap will do real work.** Ask averaged 13,795 input tokens, and Symbaroum's
16 KB average source explains almost all of it: three recent sessions at ~4,000 tokens each is
~12,000 of that 13,795. `MaxSessionChars = 4,000` cuts each to ~1,000, so Ask input should fall
roughly 65%, and the 8,000-token context ceiling then sits comfortably above normal usage rather
than biting. Worth watching for answer-quality complaints on "what happened last session?" —
4,000 characters keeps roughly the first quarter of a long Symbaroum note.

### Phase 1 verified in production

- **Compression is live.** `/welcome` 32,257 → **10,926 bytes** brotli (66%); `app.css`
  90,956 → **13,831 bytes** (85%); the API compresses too.
- **Sampling is live and exact.** API dependencies report `avgItemCount = 10.0`, Web requests
  `9.9`. The lower figures on some streams (5.1, 6.4) are failed requests being retained at 100%,
  which is the behaviour we wanted.
- **Immutable caching *is* live** — `app.mxl21wfaft.css` returns
  `Cache-Control: max-age=31536000, immutable` with an ETag. (Recorded here originally as "not
  live": I had measured the non-fingerprinted `/app.css` alias, which is deliberately
  `no-cache`, and attributed it to an environment setting that was never actually Development.
  See the correction under Phase 1.)

### What telemetry sampling was actually worth

Measured over a quiet 23-hour pre-deploy window: **163,187 ingested records**, with
`sum(itemCount) == count()` confirming nothing was being sampled. That is ~170k records/day,
roughly **6 GB/month** at typical record size — just over the 5 GB free grant, so about
**$2–3/month** of overage today.

The saving is therefore small right now and that is the honest number. What matters is the slope:
that volume came from essentially one active user, and it scales with open tabs. At five
concurrent users the same pattern is ~30 GB/month ≈ **$58/month**. Sampling turned a bill that
grows with usage into one that does not. (An earlier estimate of ~$58/month *today* was wrong —
it extrapolated from a window that included this session's own load testing.)

### Cached-token visibility

- [x] `AiUsageRecord.CachedInputTokens` added (nullable) plus migration
      `20260727…_AddCachedInputTokens`, and populated on the extraction path — the 88% case, and
      the one whose prompt carries a large world-stable prefix. Nullable on purpose: "this path
      does not report cache hits" and "the provider reported none" call for opposite responses.
      Confirmed the Azure OpenAI SDK exposes it as `usage.InputTokenDetails.CachedTokenCount`.

### Re-ranking for Phase 3 and beyond

1. **Do the cheap latency work** (Phase 3's N+1 cluster). 613 artifacts and 4,722 references make
   these round-trip problems, not cost problems — but the fixes are trivial and the batch methods
   already exist.
2. **Phase 4 (activity endpoint) is still worth it**, scoped honestly: it is one world's problem
   today, and it is about DTU and page latency rather than dollars.
3. **Defer the expensive AI work** (Phase 6 prompt reordering, continuity fingerprint) until
   either spend or world count grows. It is correct work with a real payoff curve; it just is not
   worth much at $13/month.
4. ~~**Flip `ASPNETCORE_ENVIRONMENT` to Production**~~ — nothing to do; it already is. See the
   correction under Phase 1. The provisioning script was the only thing still saying otherwise,
   and it has been fixed.

### Original checklist

- [x] Ran the per-operation aggregation — table above.
- [x] Measured source payload per world — table above.
- [x] Read back the live Container App config and queue properties (done in Phase 0). API is
      `minReplicas: maxReplicas: 1`; worker is 0→1 at 0.25 vCPU / 0.5 GiB; both queues are
      `MaxDeliveryCount 5`, `LockDuration PT1M`, `TTL P14D`. **So the retry-loop blast radius is
      5×**, not the 3× (emulator) or 10× (Azure default) the audit had to guess between.
- [x] Worker telemetry confirmed: `APPLICATIONINSIGHTS_CONNECTION_STRING` is set on
      `ca-nornis-worker`, so it does emit. It shows no records in short windows simply because it
      is scaled to zero with an empty queue — which is the correct, cheap state.
- [x] `CachedInputTokens` column added — see above.

## Monitoring — DONE 2026-07-27 (after the AI outage)

The `max_tokens` incident was found because a user noticed an extraction had failed. Nothing was
watching, and the same rejection had already been failing world-name generation for two days.
Five alert rules existed on `appi-nornis`; none covered AI calls.

- [x] `nornis-ai-call-failures` — severity 1, 15-minute window, fires on any AI failure at all
      (threshold > 0). Validated before creating: the query returns 3 over the incident window and
      0 over the fifteen minutes after the fix.
- [x] Added to `provision-azure.ps1`, so unlike the other five rules it survives a re-provision.
- [x] **Keys on exception and trace text, not dependency success** — Azure OpenAI calls turn out
      not to be captured as dependencies by the OTel distro at all. Worth knowing: no
      dependency-based alert would ever have caught this. The trace clause is what covers the
      silent case, where a caller swallows the failure into a fallback.

**Also surfaced while looking:** ~403 `401`s on `GET .../sources/activity` over three hours —
a nav poll retrying against an expired token. Unrelated to this work and not yet diagnosed.

- [x] ~~Investigate the activity-endpoint 401 storm~~ — **DIAGNOSED AND FIXED 2026-07-29.** The
      silent-refresh work of 2026-07-16 already handled the happy path; the storm is its failure
      path. When a refresh fails or the cookie has no refresh token, `Auth0TokenRefresher`
      deliberately returns the stale token "so the API's 401 surfaces normally" — but nothing was
      looking at that 401. Every poller discarded the failed `ApiResult` and kept its cadence;
      with work in flight the nav polled every 15 seconds, forever, and the circuit can neither
      rewrite its cookie nor challenge on its own.

      The fix has one new seam: a scoped `AuthSessionState`, flipped by `NornisApiClient` on any
      401 and cleared on any success — so a transient refresh failure (an Auth0 outage) heals
      itself. 403/5xx deliberately touch nothing: they describe the request, not the caller.
      The nav poll drops to a 5-minute probe (`NavActivityCadence.Expired`) rather than stopping —
      each probe re-attempts the refresh, which is the recovery path — and shows a "session
      expired — sign in again" banner whose link forces a full document load, running the cookie
      path that refreshes or cleanly re-challenges. The four page pollers (SourceDetail and
      Sources at 4s, Import at 2s, TutorialChecklist at 15s) stand down entirely; the nav owns
      the probe and the message. Covered at three levels: the state rules
      (`AuthSessionStateTests`), the cadence truth table (`NavActivityCadenceTests`), and the
      banner wiring end-to-end through a stubbed 401 (`NavMenuSessionExpiryTests`).

## Independent review of Phases 3–4 — DONE 2026-07-27

`code-critic` finally ran (on Opus — it exhausted the Fable tier twice; equal to what wrote the
code, so within the repo rule, but not the stronger-model read the rule prefers).

**The high-stakes claim held.** It rebuilt the pre-change predicate from `9463cd2` and ran it
against the new EF expression over a full matrix — every scope × draft state × ownership, for GM,
owning Player, other Player, Observer and anonymous — with **zero mismatches**. It also confirmed
`relationshipsForA` has no mutate-then-fall-through path, all five `ApplyAsync` call sites pass a
source matching `batch.SourceId`, and the fail-closed and first-reference-wins semantics survived.
Nothing required a fix that night.

Six real defects, all now fixed:

- [x] **`HasWorkInFlight` counted a review backlog as work in flight**, so any GM with an
      unreviewed queue pinned *every open tab* to the 15-second cadence — the exact load Phase 4
      set out to shed. Now only `Queued`/`Processing`/`Ready`. New proposals arrive when an
      extraction finishes, which those states already cover.
- [x] **The freshness window was keyed on world alone, not (world, effective role).** Toggling
      "view as player" within 3 seconds of a poll kept the GM's counts on screen for up to 90
      seconds beside a preview claiming to show a player's view. The API genuinely answers
      differently per view-as.
- [x] **A dropped concurrent refresh lingered up to 90 seconds** instead of 15. Triggers turned
      away while a fetch is in flight now record that they wanted one and get served afterwards,
      and a response for a world/role the user has since left is discarded rather than painted.
- [x] **`CountOpenForReviewerAsync` had no test** — the one new query deciding per-role scope.
      Seven relational tests added, each asserting the count *agrees with what
      `ListReviewQueueAsync` would return*, since a badge promising work the review page cannot
      list is the failure that matters. Its two scope divergences from the queue (source-exists,
      world match) are closed rather than left fail-safe-by-accident.
- [x] **`CanSeeSource` compiled a fresh expression per call** at the one call site whose own doc
      comment forbids it. Memoised on the scoped service instead.
      **Note:** the first attempt hand-wrote a plain-boolean twin of the rule — which would have
      reintroduced exactly the drift risk `SourceVisibilityRule` exists to prevent, trading a
      leak risk for tens of microseconds. Reverted; the class now says so explicitly.
- [x] **Dead members removed** — `ProposalApplicator._sourceRepository` (all seven re-fetches
      gone) and the unreachable `CanSeeSource(Source, …)` overload in `ArtifactService`.

## Phase 3 — The N+1 cluster — DONE 2026-07-27 (7 of 8)

Clean build (0 warnings), all **2,928 tests green**. No migration.

- [x] `WorldService.ListForUserAsync` — two queries regardless of world count, via the existing
      `IWorldMemberRepository.ListByUserAsync`. Membership is still required rather than assumed,
      so a world with no membership row still drops out exactly as before.
- [x] `SourceLocationService.BuildLocationsAsync` — one batched fetch; predicate untouched.
- [x] `ArtifactRemovalService.PreviewAsync` — one fetch for all counterparts. `"(unknown)"` is
      preserved for an id that no longer resolves; the dialog should still mention an edge it is
      about to delete.
- [x] `ProposalApplicator` `PartOf` branch — the relationship list is fetched once and reused by
      both the parent-move branch and the duplicate-edge check below it. Safe because the branch
      only falls through when it found nothing, so it cannot have mutated the list it reuses.
- [x] `Source` threaded into `ProposalApplicator.ApplyAsync`; all seven per-arm re-fetches gone.
      This turned out to be **five** call sites, not the two the audit found — `ArtifactMergeService`
      and `StorylineWrapUpService` also apply proposals, and `RevealService` needed its private
      helper widened. All five already had the source in hand.
- [x] `ArtifactService.GetDetailAsync` — both loops batched. The cited-source loop now uses a new
      `SourceAttribution` projection rather than whole rows: a source carries `Body` and
      `DerivedText`, so reading a title off a dozen citations was pulling transcripts across the
      wire on the most-visited authenticated page. `CanSeeSource` was refactored to one
      implementation over the two fields it actually reads, so the entity and projection paths
      cannot drift — this decides whether a Private note leaks.
- [x] `SourceKnowledgeService` — three batched loads (facts, relationships, then every artifact
      anyone needs) replacing a point lookup per provenance row, and `HashSet` dedup replacing the
      linear scans. Iteration is still driven by reference order, so the displayed quote is still
      the one from an item's *first* reference.

**Deferred — the `HealthService` split.** This is the one item Phase 2's numbers argue against
doing now, so it is left undone rather than done carelessly.

The duplicate load happens only in `ContinuityAuditService.RunAssessmentAsync`, which runs at most
once per world per day and is immediately followed by a 10–30 second paid model call. Against
that: the two callers differ in three dimensions — health includes Archived artifacts and the
audit excludes them; health loads facts at `int.MaxValue` and the audit caps at 25 per artifact;
and the two pass different artifact-id sets when fetching relationships. Getting any of those
wrong silently changes a published, user-visible health score.

I did confirm the merge is *feasible*: `ArtifactFactRepository.ListByArtifactIdsAsync` applies its
per-artifact cap in memory after materializing, ordered by `UpdatedAt` descending, so loading the
superset once and narrowing in memory is exactly equivalent. The seam is real; it just is not
worth spending a scoring regression on to save ~100 ms from a daily, already-20-second operation
on a 613-artifact database.

- [ ] Revisit when world count or artifact volume grows: extract a pure
      `Score(artifacts, facts, relationships, sourcedIds)` from `HealthService`, load the superset
      once in `RunAssessmentAsync`, score the unfiltered set for the heuristic, and narrow in
      memory for the prompt. Cover the archived/cap/endpoint differences with tests *first* —
      the score is published, so a silent shift is worse than the duplicate query.

## Phase 4 — The activity endpoint — DONE 2026-07-27

Clean build (0 warnings), **2,960 tests green**. No migration.

`GET /worlds/{id}/sources/activity` is now two aggregate queries. It previously loaded every
source row in the world *twice* — `Body` and `DerivedText` included — plus up to 200 proposals,
every review batch, every artifact, and sometimes every fact, then threw all of it away to return
six integers. On Symbaroum (1,482 KB of source text, per Phase 2) that was roughly 700 MB/hour
per open tab.

- [x] Endpoint replaced with `CountByStatusAsync` + `CountOpenForReviewerAsync`, both aggregating
      in SQL. No entity materialisation on this path at all.
- [x] **The visibility rule now has exactly one definition.** Rather than hand-writing a SQL
      predicate beside the existing in-memory one, the rule moved to
      `SourceVisibilityRule.CanSee(userId, role)` as an `Expression`, used directly by EF and
      compiled once per call site for in-memory filtering. A second copy is precisely how a badge
      count starts disagreeing with the list it summarises, and the direction it fails is a
      Private note appearing in someone else's total.
- [x] Per-role tests written first, at two levels: nine in `Nornis.Domain.Tests` pinning the rule
      itself (expectations derived from the *original* predicate, so a drifting translation is
      caught), and eight in `Nornis.Infrastructure.Tests` running the real query against a
      relational provider — an in-memory fake could agree with the C# while the generated SQL
      leaked.
- [x] Review scoping kept deliberately separate from source visibility. They are **not** the same
      rule: a Player may read a party-visible source they did not write, but may only review
      proposals on sources they authored. Conflating them would have silently widened the review
      queue.
- [x] Client polling: a 3-second freshness window collapses trigger bursts (boot raises
      `Worlds.Changed` three times; a write raises `ActivitySignal` and is usually followed by a
      navigation), and the idle cadence drops from 15s to 90s. Writes bypass the window via
      `force: true` so a badge never looks like it lost an update, and the window is keyed on the
      world the counts belong to so a world switch always refetches.
- [x] Tutorial checklist detectors pushed into SQL as existence checks. Four booleans, polled
      every 15 seconds through a new user's first session, previously answered by loading four
      tables in full — two of them carrying session bodies.

**Two things the tests corrected, worth recording:**

1. The Private gate is "GM **or author**", with no further role test — so an Observer who authored
   a Private source still sees it. My first test asserted the intuitive "Observers see only
   PartyVisible" and failed against correct code. The rule as written is now pinned explicitly so
   any future tightening is a deliberate decision.
2. A truly unattributable source cannot exist: `CreatedByUserId` is a real foreign key to `Users`,
   so the `Guid.Empty` ownership guard is defence in depth rather than a live case. Trying to seed
   one fails on the constraint.

**Not done — stopping the poll when the tab is hidden.** It needs a JS interop listener on
`visibilitychange` plus disposal, which is more surface than the rest of this phase combined, and
the 90-second idle cadence already removes most of what it would save. Left as its own item rather
than bundled in here.

- [x] ~~Suspend the nav poll when the tab is hidden~~ — **DONE 2026-07-27, below.**

### Suspending the nav poll in a background tab — DONE 2026-07-27

`nornis-nav.js` gained a `visibilitychange` subscription; `NavMenu` stands the poll down while the
tab is in the background and forces a refresh on the way back. A browser left open overnight is the
normal case, not the exceptional one, and every one of those tabs was spending a request and a
query every 90 seconds — enough to hold a scaled-to-zero container awake.

The loop moved out of the component into `NavActivityCadence.RunAsync`, taking its wait as a
delegate. That was not tidying: with the loop inside `NavMenu` the only way to observe a hidden
tick spending nothing was to wait 90 real seconds, so the behaviour the whole change exists for had
no test. `code-critic` made that point after the first cut — deleting the skip left every test
green — along with two others:

- **The cadence stayed parked.** Returning to the tab flipped the flag, but the loop was already
  inside a wait sized for a hidden tab, and only read the new interval on the *next* iteration. A
  user coming back to a running extraction would watch the badge crawl for up to 90 more seconds —
  worse than the always-poll it replaced, in exactly the case the fast cadence exists for. The wait
  is now cancellable and returning to the tab cuts it short.
- **The component tests booted hidden.** bUnit's loose JS interop answers `isTabVisible()` with
  `default(bool)`, so every test ran against a component that believed it was already backgrounded,
  and "going to the background" was asserting a hidden→hidden no-op. The stub now says foreground
  explicitly, and a separate test covers booting hidden.

The JS itself was checked by running the real file under Node against a minimal DOM stub — bUnit
stubs interop out entirely, so nothing in the .NET suite would notice if `watchVisibility` broke or
the callback name drifted from the `[JSInvokable]`. That name match is the likeliest silent break
and the check fails when it is changed.

**Known uncovered:** the single `_pollWake?.Cancel()` line in `NavMenu` that wires the wake up.
`RunAsync` handles a woken wait under test, and the JS side is verified, but the one line joining
them is not — every seam has a last untested line and contorting the design to reach this one was
not worth it.

## Phase 5 — Authorization-sensitive and correctness-sensitive — DONE 2026-07-27

Reviewed by `code-critic` **before** the push this time, not after. It found a blocker; see below.

- [x] **`sub → User` cached** in `UserProvisioningMiddleware` (10-minute TTL). This runs on every
      authenticated request and exists solely to turn a JWT subject into a Guid — verified that
      `user.Id` is read 118 times across the API, no other field is read anywhere, and nothing
      mutates the instance. Cached as an immutable snapshot with a fresh `User` rebuilt per
      request, so no entity is ever shared between concurrent requests.
- [x] **Batch-accept reads hoisted.** `OrderCreatesFirstAsync` went from one query per id to one
      via a new `ListByIdsAsync`; batch and source are memoised across the batch. For a
      50-proposal accept that is roughly 200 round trips down to ~53. The per-proposal proposal
      re-read is deliberately kept — the retry pass depends on seeing updated `Status` to take
      the idempotent path.

**The blocker `code-critic` caught, pre-push:** the cache-hit path called `await _next(context)`
*inside* the middleware's `try`, which put the entire application inside its catch clauses. On a
warm cache — i.e. nearly all traffic — a controller's `DbUpdateException` would have been caught
here, the user re-resolved, and the request re-executed from routing with every side effect run
twice; and any downstream 500 would have surfaced as a 503 logged as a user-provisioning failure,
filing real controller bugs under the wrong cause and misleading the new alert rule. `_next` is
now invoked exactly once, outside the try, with a comment saying why. Two regression tests cover
a downstream failure and a repeated write on a warm cache.

**Descoped, with reasons:**

- [ ] The ~13 membership re-queries. I expected to find an authorization hole here — the plan
      suggested services bypass the "view as player" downgrade by re-querying. **That premise is
      wrong:** the controllers already gate on the filter's role, so view-as *is* enforced, and
      `CharacterService` re-queries for the member's `Id`, not its role, which is a genuine need.
      What remains is ~13 round trips on world-settings and member-management endpoints, which are
      rare. Not worth the authorization-surface churn at the scale Phase 2 measured.
- [x] ~~The artifact-candidate-set hoist~~ — **DONE 2026-07-27, see below.** It was descoped here
      as the single riskiest remaining change, to be done on its own with tests first. It was, and
      the risk was not theoretical: two review passes found three ways it reintroduced the very bug
      it was warned about.

## The artifact-candidate-set hoist — DONE 2026-07-27

Accepting a review batch ran, *per `CreateArtifact` proposal*, a full `ListByTypeAsync` (every
non-archived artifact of that type in the world) plus a `GetByWorldAndUserAsync` to build the
source author's visibility filter. A 50-proposal batch was ~100 queries. Both are now memoised on
the applicator, which is `AddScoped` — one instance per request. Ten creates of one type: 1 listing
and 1 membership query, down from 10 and 10.

The plan's standing warning was *"the hoisted artifact set must be invalidated after each create or
the 'Salt Factor' dedup bug returns"* — two proposals naming the same new artifact both miss the
dedup and both insert, stranding half the batch's facts on a duplicate. That was the easy half.
`code-critic` found two more routes to the same failure that the warning did not anticipate, and a
third on the re-review:

1. **Cross-author staleness.** The first cut keyed the cache by (world, author, type) — the two
   things the query varies on — and appended a new artifact only to its own author's entry. But one
   accept spans every source in the world, so consecutive proposals routinely have different
   authors, and another author's entry warmed earlier in the request went stale. No rename or merge
   anywhere near it. Fixed by keying on (world, type) and loading unfiltered, running both
   visibility gates in memory instead: keying on what the row *is* rather than on who is looking
   makes the failure unrepresentable.
2. **Rolled-back creates became ghost matches.** Each proposal applies in its own transaction and
   the accept loop carries on afterwards *on the same applicator*. A create that failed after its
   insert — a bad map-pin block, a transient error committing — stayed in the cache forever. The
   next proposal naming it would be reported "bound to existing", commit provenance against an id
   no row carries, and leave every fact on that name unresolvable with the create already Accepted
   and unable to be reopened.
3. **Concurrent writes from other requests.** The fix for (2) re-read only ids created *this*
   request, and I wrote a comment asserting that rows read from the database needed no re-check.
   **That claim was wrong.** The old code re-listed before every create, so it saw other requests'
   writes for free; a snapshot gives that up. Another GM archiving, renaming, or narrowing the
   visibility of an artifact mid-accept would leave this request binding to it. The visibility case
   is the quiet one: a player's note filed as provenance on canon that has since been hidden from
   them.

(2) and (3) are now one rule rather than two with a carve-out: the chosen match is re-read and
re-gated against the fresh row before it is used, always. That costs one primary-key lookup per
successful dedup — against the full listing per create that this change removed — and closes the
window to the same width the old code had, read-then-write with no lock, rather than to zero.

16 tests, each mutation-checked: neutering the fresh-row gates kills exactly the four concurrency
tests, neutering the existence check kills exactly the rollback test, and all three of the tests
written for the review findings fail against the version the review rejected. The test fake's
`ListByTypeAsync` now returns detached copies, mirroring `AsNoTracking()` — without that, two of
the invalidation tests passed vacuously, because the fake handed back the same object the
applicator had cached and a rename appeared to reach into the cache by itself.

**Known and accepted:** a single-proposal interactive accept now loads every artifact of that type
in the world rather than the author-filtered subset, so in a world with a lot of GM-only content it
carries more payload for the same one query. A projection to the columns dedup actually reads would
remove it; not worth it at current scale.

## Phase 6 — worker reliability half — DONE 2026-07-27

Phase 2 measured the AI *cost* items at $1–2/month, so these two were taken first: they are about
the system not amplifying a failure. Clean build, 3,028 tests green, no migration. Reviewed before
the push, which caught a silent regression (below).

- [x] **One typed transient-failure classifier.** Extraction and library indexing each decided
      retryability by substring-matching exception messages for `"429"`, `"503"`, `"rate limit"`,
      and had already drifted — library treated a timeout as transient and extraction did not, so
      the same outage retried one path and wrote off the other. A third copy of the
      permanent-failure half was living in relationship backfill. Now one definition on typed
      status codes, with no dependency on the Azure SDK from the application layer.
- [x] **Backoff before redelivery.** Abandoning made a message available immediately, so a
      throttle was answered with an instant re-request while each redelivery re-ran context
      assembly, blob reads and a fresh model call. Now 5s → 10s → 20s → 40s by delivery count.
      **Deliberately not the scheduled-re-enqueue design:** that resets `DeliveryCount`, losing the
      queue's dead-letter backstop unless replaced by a hand-rolled attempt counter, and the
      namespace is Basic tier where scheduling was not something to assume.
- [x] Abandon now uses `CancellationToken.None` — during a deploy drain the processor's token is
      already cancelled, so abandoning with it threw and left the message locked until expiry.

**What the pre-push review caught.** My premise that "the infrastructure clients already translate
SDK exceptions to `HttpRequestException`" was true for extraction and **false for the library
path**: the blob service translated only 404, and the embedding client did not translate at all.
So an Azure Storage 503 — previously transient because its text contains "503" — would have become
permanent, failing a GM's library document with no retry. Both now translate at the boundary like
their siblings. That is the exact direction of error the classifier's own doc comment warns about,
introduced while writing it.

Also from the review: the final delivery no longer waits (it dead-letters rather than retrying, so
the wait was pure idle worker time during an outage); a lost message lock on abandon is tolerated
rather than escaping into the caller's catch, which would have triggered a second full backoff;
and the doc comments that overstated what changed were corrected.

- [x] **The backoff is now tested, and the test double no longer lies.** The wait was split out as
      `RedeliveryBackoff.WaitAsync` so it can be exercised without Service Bus: which delay is
      chosen, that a cancelled token cuts the wait short rather than holding a deploy open, and
      that cancellation interrupts a wait already in progress. `TestableExtractionWorker` — a
      hand-copied mirror of the handler, because `ProcessMessageEventArgs` cannot be constructed
      in a test — had fallen behind and was abandoning instantly while production backed off, so
      two worker tests passed while asserting on a shape that no longer ran. It mirrors production
      again, and carries a comment saying plainly that it drifts silently and the compiler will
      not help.

      Both new assertions were mutation-checked: removing the backoff from the double makes them
      fail. Worth doing — the first mutation attempt did not actually match the code, reported
      green, and would have left me believing a vacuous test was real.

## Phase 6 — remaining AI spend items

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
- [x] ~~Replace the bare `AbandonMessageAsync` with a backoff via scheduled re-enqueue~~ —
      **superseded by the worker-reliability half above (2026-07-27):** the backoff shipped as
      `RedeliveryBackoff` (in-process wait before abandoning), and the scheduled-re-enqueue design
      this item describes was *deliberately rejected* there — it resets `DeliveryCount`, losing
      the DLQ backstop, and the namespace is Basic tier. Do not implement as written.
- [ ] Persist library-indexing chunks per batch and resume from the highest stored `Ord`, so a
      failure stops re-buying the whole document. Key the resume on a content hash so a re-upload
      does not resume onto stale vectors.
- [ ] Only then raise `MaxConcurrentCalls` off 1 — and raise worker memory first. Three concurrent
      extractions each buffering a full PDF into a 0.5 GiB container is a realistic OOM, and more
      concurrency without the backoff fix just manufactures more 429s.
- [x] ~~Add a shared `TransientFailureClassifier` keyed on typed status codes~~ — **already done
      in the worker-reliability half above (2026-07-27):** `Nornis.Application/Ai/
      TransientFailureClassifier.cs`, with tests, used by both workers. This entry predated that
      work and was never ticked.
- [ ] Store a content hash on `LibraryDocument` and short-circuit `ReindexAsync` when it matches.
      Always leave a force-reindex escape hatch.
- [ ] Move world-name generation off the premium Ask deployment onto a cheap keyed client, and give
      it an `AiUsageRecord` — it is the only model call in the codebase whose spend is invisible.
- [ ] Re-check the budget guard per chunk in the storyline retrospective, and persist verdicts per
      chunk so a late failure does not discard work already paid for.

## Phase 7 — Endpoint shape and payload — PARTIAL 2026-07-27

The two items real data said were worth doing. Clean build, 2,953 tests green, no migration.

**Measurement re-ranked this phase.** `GET /api/users` reads as the alarming item — it returns
every user in the system to any authenticated caller — but there are **2 users**. It is a
directory leak that grows, not a live exposure, so it lost to the two items below.

- [x] **Source list projected in SQL.** `/sources` polls every four seconds while anything is
      processing, and every poll loaded whole rows including `Body` and `DerivedText` — ~1.48 MB
      of transcript on the one world with real content — to render titles and status chips. Now a
      `SourceListItem` projection that never touches those columns, with the shared
      `SourceVisibilityRule` pushed into the query alongside it. Seven relational tests cover
      per-role visibility, the draft gate, ordering, campaign filters and world isolation, plus a
      type-level guard that `SourceListItem` can never grow a body column back.
- [x] **`GET /canon` bounded.** The dashboard — the landing page — pulled the world's entire
      knowledge graph to render six rows. `kind` and `limit` added; `kind` makes the service skip
      loading the other kind entirely, so it saves a query rather than only wire bytes. `Take` is
      applied after ordering and after the visibility and truth-state filters, so a cap can never
      hide an entry a filter would have kept. `Home.razor` makes two narrow calls inside its
      existing parallel fan-out.
      **Behaviour change:** a caller omitting `?limit=` now gets at most 200 entries instead of
      everything, with no signal in the response that truncation occurred. `Home.razor` is the
      only in-tree consumer; the cap is documented on the endpoint so an out-of-tree one cannot
      mistake a partial canon for a complete one.

**Corrected after review — the first version made the dashboard slower.** `CanonService` loads
every artifact in the world on each call, so having `Home` make two `kind`-narrowed calls turned
one artifact load into two. The payload shrank and the database work grew; my claim that `kind`
"saves a query" was true per-call and wrong in aggregate. It is now one call with `factLimit` and
`relationshipLimit`, which caps each kind before merging — a single overall cap over a fact-heavy
world returns no relationships at all, and there is a test pinning exactly that. `kind` remains
for callers that genuinely want one kind.

Also from that review:

- [x] Ten tests added for `kind`/the limits, which had none. Two pin the invariants the code
      comments assert: caps run after the visibility and truth-state filters, so a cap can never
      spend a slot on an entry a filter should have removed. Both would have passed silently if
      the logic were inverted.
- [x] `?kind=7` now returns 400 rather than an empty canon indistinguishable from "no canon yet"
      — `Enum.TryParse` accepts undefined numeric values, so `Enum.IsDefined` guards it.
- [x] Deterministic ordering tiebreaker on the source list. The demo template stamps every source
      with the same `CreatedAt`, and SQL leaves order within a tied group unspecified, so the list
      could reshuffle between four-second polls.
- [x] `SourceService.ListByWorldAsync` deleted rather than left dead. Both callers moved to the
      projection, and the generative visibility and ordering property tests — 100 scenarios each,
      the strongest coverage in the suite — were retargeted at the live path. They had been
      guarding a method no endpoint called.
- [x] Fixed a flaky test of my own from Phase 0: the continuity-audit tick had a ten-second
      deadline that held in isolation and failed under full-suite load.

Still open:

- [ ] Keyset paging on the unbounded list endpoints. Deferred as an API contract change needing
      every consumer audited; `/canon`'s 200-entry default cap addresses the unbounded-growth risk
      without it.
- [x] ~~Require a search term and cap results on `GET /api/users`~~ — **DONE 2026-07-27, below.**
- [ ] Add keyset paging to the unbounded list endpoints, prioritising sources, artifacts, canon and
      users. Audit each consumer in the same change — `Home.razor` currently assumes it receives
      everything.
- [x] ~~Add output caching to the anonymous public GETs~~ — **DONE 2026-07-27, below.**
- [ ] Compose the dashboard and source-detail fetches into single endpoints, built by calling the
      same application services so there is one authorization implementation. ~~`Task.WhenAll` on
      `SourceDetail`'s serial waterfall is the cheap interim step.~~ **Interim step DONE
      2026-07-29:** the campaign list and replay banner now load alongside the source fetch, and
      the four source-dependent loads (attachments, map, locations, knowledge) fan out together —
      seven serial round trips at worst are now three stages. The world id is captured once at
      load start, closing a latent NRE when a world switch landed mid-waterfall. The full
      endpoint-composition item above stays open.

## The user directory — DONE 2026-07-27

`GET /api/users` returned `{id, username}` for **every user in the system** to any authenticated
caller. It had no role check at all: the API's fallback policy requires authentication and nothing
more, so the only thing standing between a token and the whole directory was the browser choosing
not to render the picker for non-GMs. Its sole consumer was the add-member picker, which downloaded
the list whole and filtered out existing members client-side.

It is now `GET /api/worlds/{worldId}/members/addable?q=`, which sits behind the same membership
filter and GM check as adding the member itself, excludes existing members in SQL, requires a
search term of at least two characters, caps at 50, and is rate limited. The old route is gone and
`IUserRepository.ListAsync` was deleted outright, so there is no "every user" query left to call.
The picker became a `MudAutocomplete` searching server-side.

**The plan's own suggested fix was wrong, and review found the first one incomplete.**

- The note above proposed making the endpoint *world-scoped*. That would have broken the feature:
  the picker exists to list people who are **not** in the world. World-**gating** is the shape that
  works — scope the *permission* to a world you GM, not the *results*.
- The first cut gated on "GM of this world" and called it closed. `code-critic` pointed out that
  the gate is self-issuable: anyone can `POST /api/worlds` and be GM of what they just created,
  and with no rate limit on authenticated routes they could then walk `?q=a`, `?q=b`, … and
  reassemble the directory a page at a time. My doc comment claimed the directory was no longer
  enumerable, which was **false**. What actually raises the cost is the required search term (there
  is no listing mode to fall into), the cap, and a per-user rate limit — the role check only bounds
  who can ask about *your* world. The comment now says that rather than overclaiming.
- The authorization decision moved into `WorldMemberService`, matching the repo's own standard and
  every other privileged action on that controller, which check in the controller *and* the service.
  Because both layers enforce, removing either one leaves the endpoint tests green — so the service
  guard has its own Application-layer tests, which is what would catch the next caller that is not
  a controller.

Also fixed while in here: the picker swallowed a failed search and rendered it as "no matching
users"; it kept a selected candidate across a world switch, so Add could put someone in the wrong
world; and it had no `MaxItems`, so MudBlazor's default of 10 silently truncated with no indicator.

**Left alone deliberately:** `Users.Username` has no index and the match is a leading-wildcard
`LIKE`, so each search is a full scan of a two-row table. Fine now, worth an index before it isn't.
Separately, user provisioning falls back to the raw Auth0 subject when a token carries no nickname
claim, so a username can legitimately *be* an auth identifier — a pre-existing provisioning issue,
spun off as its own task rather than papered over with an assertion here that would pass for the
wrong reason.

## Output caching on the public pages — DONE 2026-07-27

All ten `GET`s under `/api/public/worlds/{slug}` are output-cached for a minute; `Ask` is not. That
surface is the one a stranger can reach — a shared link, a crawler, a chat unfurl — and the only
one in the API whose response does not depend on who is asking, because every public read runs as
Observer with a sentinel user id. A link doing the rounds now costs one set of queries rather than
one per visitor.

**The plan said "tag-evict on the public-access and demo kill switches". That was not enough**, and
`code-critic` found why. Evicting only on world settings left the writes that matter most alone:
setting a published session note to GMOnly, deleting a source, removing a fact. Those are
takedowns. A GM who spots a player's real name in a published note and hides it would have gone on
serving it to anonymous visitors for the rest of the cache window.

Worse, my own comment claimed the short duration was safe partly because a GM checking their public
page would notice. **They would not.** The Blazor host attaches the bearer token to every API call,
and output caching declines to serve a request carrying an `Authorization` header — so the signed-in
GM always sees the live page and never the copy strangers are getting. The person best placed to
notice is the one person who cannot.

Eviction is now a blanket result filter: any successful non-GET drops the tag. Enumerating write
paths would have to be redone correctly every time one is added, and getting it wrong is silent.

The review also found the cache key varied by every query key — the framework default. These
endpoints read no query parameters at all, so `?_=1` and `?_=2` were two entries for one response,
which made walking `?_=1..n` against a real slug a way to fill the 100 MB in-memory store and burn
the shared anonymous rate-limit budget while never being served from cache once. A small policy now
drops the query string from the key.

Confirmed clean by the same review, empirically rather than from memory: the path already carries
route values, so `/artifacts/{a}` and `/artifacts/{b}` do not collide; and `UseOutputCache` sitting
*inside* `UseResponseCompression` is the correct order — the cache stores uncompressed bytes and a
hit re-enters compression, so a client that did not ask for Brotli does not get it.

**Known and accepted:** a change made directly in the database, bypassing the API, is not evicted
and can be up to a minute stale. That is what the duration is for.

**Removed rather than kept:** a test asserting an unknown slug's 404 is not remembered. It is not —
the framework refuses to store any non-200 — but that is an invariant this code cannot reach, so
the test passed no matter what we did. It read like coverage and was worth nothing.

## Deferred — real but low payoff

Recorded so they are not re-discovered: `Virtualize` on long lists, search debounce intervals,
self-hosting Google Fonts, memoizing `StorylineTimelineChart.Rows`, blob lifecycle/tier policy,
`WebPushClient` HttpClient reuse, central package management, test-suite parallelisation, Docker
buildx bake, NuGet caching in CI, deleting the dead `WorldMemberFilter`, and caching the demo
template zip. Each is written up with a fix sketch in the audit.

**Do not treat as a quick win:** enabling `EnableRetryOnFailure` looks trivial but throws at runtime
on the five explicit-transaction call sites unless each is wrapped in an execution strategy.