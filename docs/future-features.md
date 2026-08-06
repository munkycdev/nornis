# Execution order

2026-08-01. The master sequence across every plan below, ordered for execution.
Sorting rule: work whose judgment calls are already made in these specs — mechanical
sweeps, config, CI wiring, precisely-scoped fixes — sits at the top and is safe to run
on Opus; work whose *value is* the judgment (policy decisions, prompt and product
design, architectural carving, subtle concurrency) holds for Fable. Items reference their spec
files under `docs/plans/`; nothing here restates them.

**Done** (2026-07-31 → 08-01, deployed): scrub tiers 1 and 2; D1's applicator by-id
gates and drifted-visibility leaks (via 1.2/1.7 + the auth review's fixes: world-scoped
review-queue name resolution, Draft gate on the Ask session feed, own-row visibility on
fact/relationship updates); D4's validator-enum rejection (via 1.7); scrub 1.10's
LibraryDocumentDetail reload guard; item 1 below (test quality phase 1); item 2 below
(system status — `/status`, the five checks, Web `/health`, worker heartbeat).

Numbers below stay fixed for this run even as items complete — sessions and notes
refer to them by number.

**Deploy was broken 2026-08-01 16:04–17:41** and nobody noticed for four commits: the
Dockerfile still copied `src/Nornis.Shared`, deleted by scrub tier 2. `test` stayed
green the whole time, so the run summary looked half-fine. Fixed in `1797d0b`. The
lesson is O1's, and it is already on the list: nothing verified the deploy actually
landed.

**Session bootstrap** (everything else a cold session needs): the bar is a clean
`dotnet build Nornis.sln` (warnings are errors), full `dotnet test Nornis.sln` green,
and `dotnet format` on touched files (CI verifies). If Api.Tests/Web.Tests binaries
are file-locked, the local dev servers are running — test the other projects
individually and say what you could not verify; never kill the servers. Branch per
work item, merge to main only when asked; a push to main deploys to production.
Migrations are applied manually before the deploy that needs them
(`dotnet ef database update --project src/Nornis.Infrastructure --startup-project
src/Nornis.Api --connection "<prod>"`) and must stay additive.

**Opus-ready, in order:**

1. Test quality phase 1 — coverage collection + local report. Config and scripts only.
2. System status: the `/status` endpoint, checks, Web `/health`, worker heartbeat.
   **Done, but its migration is not applied yet** — `AddWorkerHeartbeats` (additive,
   one CreateTable) must be run against prod *before* the deploy that carries it, or
   the worker's first beat 500s and `worker-heartbeat` reports the worker dead.
   The status *page* is not part of this — it lands with item 3, which owns the site.
3. Test quality phase 2 + the status page — CI surfacing, history branch, one static
   dashboard site carrying both faces. Pages-vs-`$web` fallback is pre-decided.
   **Done 2026-08-01** (`b8e87b2`..`9524ddc`), recorded a day late: `ci.yml` reports
   coverage per PR, `coverage.yml` merges and appends to the `coverage-history` orphan
   branch, and `ci/pages/` carries both faces at <https://status.nornis.app>. Phase 3's
   CRAP table rode along, which is why it is marked done under item 10.
4. O5 dependency patching + O6 runbooks — config and docs. **Done 2026-08-01.**
   Takes the Node 20 rider with it: the `github-actions` ecosystem is what will now
   propose those major bumps, to be read one changelog at a time rather than accepted
   as a batch.
5. O1 post-deploy verification + O2 DLQ visibility. **O2 done 2026-08-01**; the pipeline
   poll landed earlier the same day with the status work. The `/status` dlq row was
   dropped on purpose — it needs Manage rights the public API must not hold; see the plan.
   **O1 done 2026-08-01** — probes are on api and web, with liveness deliberately on TCP
   rather than `/health` (see the plan: liveness on `/health` turns the migration window
   into a crash loop). **Fully closed 2026-08-02**: `/health` now emits
   `{status, failing}` and `deploy.yml` names the cause. That last piece had been held
   as "a payload the availability alert reads" — it was not; the alert pinged only the Web
   app's `/welcome`. **Closed 2026-08-02** with a second availability test against
   `/health`, plus a dimension split on the alert without which the new test would have
   diluted the old one's sensitivity. See the plan.
6. D2 deterministic functional bugs — every item carries a repro and a prescribed fix.
   Includes the merge skip-branch row deletion (prerequisite for W2) and the
   stale-response family. Two additive migrations (batch and replay unique indexes).
   **2026-08-01: eleven of twelve done**, migration `AddExtractionReplayActiveUniqueIndex`
   applied. Three of the eleven are deliberately partial and say so in the plan (payload
   cap without extraction-time schema validation; world assertion without the
   parameter-order sweep; wrap-up idempotency without per-step reporting). The twelfth —
   the Queued wedge — was assessed and deferred on 2026-08-01, then **fixed 2026-08-02**
   once the extraction batch unique index removed half its objection: a duplicate run can no
   longer commit a second batch, so the remaining risk is bounded waste. `StatusChangedAt`
   (additive) plus a one-hour staleness gate. **All twelve done.**
7. D3 error handling — **done 2026-08-01, all seven items.** Prescribed, mostly mechanical. Includes the D1 leftovers that
   are metering-shaped: the $0-pricing alert (in the shared recorder) and
   failed-attempt usage recording.
8. D1 remainder — reveal-corrections PrivateGuard, upload-size re-validation at
   confirm, Ask world-switch bleed, concurrent-extraction claim. Sharp specs, but
   auth- and concurrency-critical: **whatever model runs these, the independent
   review rule applies before merge.** If uncomfortable on Opus, these are the first
   thing Fable does on return.
9. Scrub tier 3 convention unification, then tier 4 test pruning — mechanical sweeps;
   the doc names each decision. **Both largely done 2026-08-02.**
   - Tier 3 merged in three passes: the mechanical conventions (one GM test, `SectionName`
     on all seven Options, `AppResult` over `AppResult<bool>`, the tier-2 rider); the
     repository contracts (missing-row per verb, `DeleteWhereAsync`/`SetWhereAsync`,
     `SourceRepository.MutateAsync`); and the small idioms. Two items reversed the plan's
     stated direction and say why in the scrub doc — persisted-enum ordinals, and the
     collection-expression rule's scope.
   - **The two [auth] items are done 2026-08-02** and sit on `tier3-auth-seams`, unmerged
     behind the same independent-review gate as item 8. Sixteen of seventeen inline GM
     checks collapsed onto the service layer; five services stopped re-reading the
     membership row the action filter had already resolved. Four of those "duplicates"
     turned out to be the *only* enforcement (HealthController's four, StorylinesController's
     one) and were moved rather than deleted. The trade — services now trust an opt-in
     filter — is covered by a new `WorldMemberFilterCoverageTests`.
   - Tier 4 merged: the three running ledgers, the reflection roster (now an assembly scan),
     one shared `ReviewHarness` replacing six copies, and sixteen properties that generated
     an argument they never read. **The last three bullets closed 2026-08-02** — per-field
     decomposition, attribute padding, consolidation sweep. Net −21 cases across the suite
     and two facts that had no test before (the extraction client's empty-rationale branch,
     a non-GUID worldId reaching routing). **Tier 4 is now fully closed** — the last two
     leftovers went the same day: the sixteen properties are plain `[Test]`s, and the four
     numbered files are five fixtures named for what they hold. Tier 5 is all that remains
     of the scrub, and it is item 18, Fable-held.
10. Test quality phase 5 (authorization-suite tagging). **Done 2026-08-02** — 303 tests
    tagged, 318 cases, a named CI step that runs even on a red build, and the count charted
    on the dashboard. The enumeration was the work: two broader passes caught pagination and
    empty-state tests, so the rule is now "asserts a denial, or its name states the scoping
    fact". **Phase 3 (CRAP hotspots) done 2026-08-01** — pulled forward while the dashboard
    was open and the data was already in the merged Cobertura.
11. O4 AI kill switch (design pre-decided, one additive migration) and O3 managed
    identity (procedural, but touches prod credentials — do it attended).
    **O4 done 2026-08-02.** Its transport design was wrong in the spec and was corrected
    before anything was built: the Worker was to reschedule messages "using the same
    scheduled-copy mechanism `RedeliveryBackoff` already uses" — which uses no such
    mechanism and argues against it, on a Basic-tier namespace where scheduled messages do
    not exist. Paused now means *stop consuming*, so queued work waits in the queue instead
    of burning delivery counts. Migration `AddOperationalFlags` is additive (one
    CreateTable) and must be applied before the deploy that carries it.
    **O3 remains the only open item, and is attended-only** — it changes how every host
    authenticates to prod.

**Hold for Fable:**

12. Test quality phase 4 (coverage floors) — **the mechanism landed 2026-08-03**;
    `coverage-thresholds.json` plus `scripts/coverage-gate.ps1`, running on PRs and on
    main with every floor null and therefore report-only. What is left is the judgment
    call it was always gated on: picking the numbers, once ~two weeks of history exists
    (collection started 2026-08-01, so around 2026-08-15). `-Suggest` proposes a floor two
    points under what the current run observed, and says so — the history on the
    `coverage-history` branch is what the number should actually be read against. Turning
    the gate on is editing a number.
13. W1 accept-time summary maintenance — the review-vs-trusted policy decision and
    the summary prompt are the work. **Done 2026-08-05** on `w1-summary-maintenance`:
    trusted operation per ai-extraction.md's dated amendment, per-world review opt-in,
    applicator-reported refresh candidates with explicit summaries pinning, prompt-level
    visibility scoping (the leak-surface tests assert on the captured prompt), and the
    `SummaryRefresh` message kind on the existing queue. **Carries migration
    `AddSummaryMaintenance` (additive) — apply before the deploy.** The plan file records
    the decisions.
14. W3 world digest — product and prompt design; two-rendering visibility judgment.
    **Done 2026-08-05** on `w3-world-digest`: the judgment resolved as two separately-scoped
    generation passes (the plan file records why its own one-pass bullet was reversed — the
    party pass reads the Observer-floor record, since an instruction to withhold is not a
    guarantee), fixed text for a party-empty world, shared audit formatter and caps, one
    upserted row per world on the Home rail. Auto-trigger deferred; GM-invoked only.
    **Carries migration `AddWorldDigests` (additive) — apply before the deploy.**
15. W2 duplicate sweep — after D2's merge fix lands; prompt/candidate-quality
    judgment.
16. D4 architectural items — ExtractionService split, prompt-seam convergence,
    shared synthetic-batch writer (which unblocks W4), AppError error-kind enum.
    **Three of four done 2026-08-04** on `d4-carving` (merged 2026-08-05, three commits —
    one per item; the defect-remediation plan records each decision, including where the
    split's prescription was corrected: the state machine had to be *repatriated*, not
    kept). Item 17 is unblocked. **AppError is deliberately untouched** — its own spec said
    "do it with scrub 1.1 or not at all", 1.1 shipped without it, and nobody ever chose the
    "not at all"; that choice belongs to David, not to this session.
17. W4 Ask file-back — small, but waits on the D4 writer. **Done 2026-08-05** on
    `w4-ask-fileback` (branched from main — the writer dependency dissolved: the answer
    files as an ordinary GMNote source through the ordinary source API, so the reviewable
    batch is the source's own extraction batch and W4 is Web-only; the plan file records
    the two reversals, including why an `AskFileBack` batch Kind must not exist).
18. Scrub tier 5 comment pass — **the enumerated deletes are done 2026-08-03**; what is
    left is the one bullet that was always the judgment call: whether to compress the
    essayistic register in the surviving comments. The scrub doc notes that if that voice
    is staying — it is also the README's and the commit history's — the bullet is a no-op.
19. Test quality phase 6 (qualitative audits) — **baseline done 2026-08-03**, once tier 4
    closed and unblocked it → [test-quality-baseline.md](test-quality-baseline.md). No
    Critical findings; the one High was fixed the same day and the rest are accepted with
    reasons. What remains is the ongoing half: `grade-tests` on changed files when a PR
    touches a priority area, and a re-audit diffed against this baseline after each feature
    wave. Both are on-demand and never blocking, by design.

## Riders

Small things noticed in passing, too small for a plan file, parked against the item
that already opens the right file. None is ever urgent; all of them rot if left
unwritten. Only the sign-in one is open; the rest are closed and kept for the record.

- ~~**ImageSharp 4 needs a licence key to build at all.**~~ **Decided 2026-08-03: stay on
  3.1.x.** Dependabot PR #30 (3.1.12 → 4.0.0) was taken as far as a build and stopped
  there: v4 added a build-time licence check, and without `$(SixLaborsLicenseKey)`,
  `$(SixLaborsLicenseFile)` or a `sixlabors.lic` in the workspace, the *compile* fails —
  not a warning, not a runtime nag.
  - The Six Labors Split License grants Apache-2.0-style terms under 1M USD annual gross
    revenue, and this project qualifies. v4 enforces regardless: free use still needs a key
    from sixlabors.com, which means an account plus a secret in Actions and on every dev
    machine. 3.1.x carries the same terms without the enforcement and is still maintained
    (3.1.12 shipped 2025-10-29), so the decision costs nothing today.
  - The major is now ignored in `dependabot.yml` — patches and minors on the 3.1 line still
    come through, so this pin does not cost security updates. PR #30 closed.
  - What would reopen it: the 3.1 line going unmaintained, or revenue approaching the
    threshold. The exit is cheaper than it looks — the whole dependency is `Image.Load`,
    `Clone(c => c.Crop(...))` and `SaveAsPng` in `MapRefinement.CropTiles`, over PNG and
    JPEG only, and SkiaSharp (MIT, no threshold) covers all four operations.
- **Nothing exercises real sign-in.** 3,127 tests and not one of them authenticates.
  The API's dev-auth bypass stands in for Auth0 everywhere, so every test — including
  all 318 cases in the authorization suite — starts from an identity the test handed
  itself. What that suite proves is that *authorization* is enforced once an identity
  exists. Whether Auth0 issues one, whether the JWT validates against the real issuer
  and audience, whether `UserProvisioningMiddleware` maps claims to the right Nornis
  user — none of that has a test, and a break in it takes the whole product down for
  everyone at once rather than degrading.
  - Not a gap a unit test closes. It wants either a smoke test against the deployed
    stack with a real token (which needs a service-principal identity and a place to
    keep its secret) or an Auth0 test tenant. Both are decisions, not chores, which is
    why this is written down rather than done.
  - Recorded 2026-08-03, having been raised in passing twice without ever landing
    anywhere. That is precisely the failure mode this section exists to prevent.

- ~~**Node 20 action deprecation**~~ **(handled 2026-08-01 by item 4; finished
  2026-08-02.)** Item 4 took `actions/checkout`, `azure/login` and
  `docker/setup-buildx-action` but left `actions/setup-dotnet` on v4, so every run
  kept emitting the deprecation annotation — which is how this was noticed, from the
  MudBlazor deploy's summary. Now on v6 in all three workflows; v5 and v6 both run
  node24, and v6 is an ESM migration with no input changes. Original note: Dependabot's
  `github-actions` ecosystem will open the major bumps for `actions/checkout`,
  `actions/setup-dotnet`, `azure/login` and `docker/setup-buildx-action` as separate
  PRs. Read each changelog before merging — majors change action inputs, and
  `azure/login` gates the OIDC deploy.
- ~~**Tier 2's unfinished sweep**~~ **(closed; confirmed 2026-08-02.)** All three went
  with item 9's tier-3 passes: the `Nornis.Web.csproj` ItemGroup is now a bare comment
  explaining why there is no ProjectReference, the README row is gone, and
  `coding-standards.md` carries a dated amendment over the stale solution layout rather
  than a silent edit. Original note: deleting `src/Nornis.Shared` left three references
  behind: an ItemGroup in `src/Nornis.Web/Nornis.Web.csproj` now empty apart from a
  comment describing the deleted project, a row in the README project table, and the
  solution layout in `.kiro/steering/coding-standards.md` — that last one wants the
  dated-amendment convention, not a silent edit. Historical records under
  `docs/features/` and `docs/performance-audit/` mention it too and are meant to; leave
  them.

## The spec files

Each plan lives whole in its own file. They are specs, not authorization — a session
implements only what this file's sequence assigns it, and does not browse sibling
plans for inspiration:

- [plans/scrub-plan.md](plans/scrub-plan.md) — the nine-reviewer audit sweep (tiers 1-2 done; 3-5 open)
- [plans/test-quality.md](plans/test-quality.md) — coverage, CRAP, floors, authorization suite, audits
- [plans/system-status.md](plans/system-status.md) — /status endpoint, checks, worker heartbeat, status page
- [plans/operational-hardening.md](plans/operational-hardening.md) — O1-O6
- [plans/defect-remediation.md](plans/defect-remediation.md) — D1-D4 and the verified-sound record
- [plans/loremaster-wiki.md](plans/loremaster-wiki.md) — W1-W4 (all currently Fable-held)
