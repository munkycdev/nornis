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
4. O5 dependency patching + O6 runbooks — config and docs. **Done 2026-08-01.**
   Takes the Node 20 rider with it: the `github-actions` ecosystem is what will now
   propose those major bumps, to be read one changelog at a time rather than accepted
   as a batch.
5. O1 post-deploy verification + O2 DLQ visibility. **O2 done 2026-08-01**; the pipeline
   poll landed earlier the same day with the status work. The `/status` dlq row was
   dropped on purpose — it needs Manage rights the public API must not hold; see the plan.
   **O1 done 2026-08-01** — probes are on api and web, with liveness deliberately on TCP
   rather than `/health` (see the plan: liveness on `/health` turns the migration window
   into a crash loop). The one piece left is naming the failing check in `/health`'s body,
   which is an additive change to a payload the availability alert reads.
6. D2 deterministic functional bugs — every item carries a repro and a prescribed fix.
   Includes the merge skip-branch row deletion (prerequisite for W2) and the
   stale-response family. Two additive migrations (batch and replay unique indexes).
   **2026-08-01: eleven of twelve done**, migration `AddExtractionReplayActiveUniqueIndex`
   applied. Three of the eleven are deliberately partial and say so in the plan (payload
   cap without extraction-time schema validation; world assertion without the
   parameter-order sweep; wrap-up idempotency without per-step reporting). The twelfth —
   the Queued wedge — is **assessed and deliberately not attempted**: every route out needs
   a status timestamp the schema does not have, and the ungated version trades a wedge for
   double AI spend. The plan spells out what it needs.
7. D3 error handling — **done 2026-08-01, all seven items.** Prescribed, mostly mechanical. Includes the D1 leftovers that
   are metering-shaped: the $0-pricing alert (in the shared recorder) and
   failed-attempt usage recording.
8. D1 remainder — reveal-corrections PrivateGuard, upload-size re-validation at
   confirm, Ask world-switch bleed, concurrent-extraction claim. Sharp specs, but
   auth- and concurrency-critical: **whatever model runs these, the independent
   review rule applies before merge.** If uncomfortable on Opus, these are the first
   thing Fable does on return.
9. Scrub tier 3 convention unification, then tier 4 test pruning — mechanical sweeps;
   the doc names each decision.
10. Test quality phase 5 (authorization-suite tagging). **Phase 3 (CRAP hotspots) done
    2026-08-01** — pulled forward while the dashboard was open and the data was already
    in the merged Cobertura.
11. O4 AI kill switch (design pre-decided, one additive migration) and O3 managed
    identity (procedural, but touches prod credentials — do it attended).

**Hold for Fable:**

12. Test quality phase 4 (coverage floors) — timing-gated anyway on two weeks of
    phase-2 history; setting the floors is a judgment call.
13. W1 accept-time summary maintenance — the review-vs-trusted policy decision and
    the summary prompt are the work.
14. W3 world digest — product and prompt design; two-rendering visibility judgment.
15. W2 duplicate sweep — after D2's merge fix lands; prompt/candidate-quality
    judgment.
16. D4 architectural items — ExtractionService split, prompt-seam convergence,
    shared synthetic-batch writer (which unblocks W4), AppError error-kind enum.
17. W4 Ask file-back — small, but waits on the D4 writer.
18. Scrub tier 5 comment pass — pure editorial judgment.
19. Test quality phase 6 (qualitative audits) — after tier 4's pruning, so we never
    grade tests scheduled for deletion.

## Riders

Small things noticed in passing, too small for a plan file, parked against the item
that already opens the right file. Neither is urgent; both rot if left unwritten.

- ~~**Node 20 action deprecation**~~ **(handled 2026-08-01 by item 4.)** Dependabot's
  `github-actions` ecosystem will open the major bumps for `actions/checkout`,
  `actions/setup-dotnet`, `azure/login` and `docker/setup-buildx-action` as separate
  PRs. Read each changelog before merging — majors change action inputs, and
  `azure/login` gates the OIDC deploy.
- **Tier 2's unfinished sweep** (rides with 9, tier 3 conventions). Deleting
  `src/Nornis.Shared` left three references behind: an ItemGroup in
  `src/Nornis.Web/Nornis.Web.csproj` now empty apart from a comment describing the
  deleted project, a row in the README project table, and the solution layout in
  `.kiro/steering/coding-standards.md` — that last one wants the dated-amendment
  convention, not a silent edit. Historical records under `docs/features/` and
  `docs/performance-audit/` mention it too and are meant to; leave them.

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
