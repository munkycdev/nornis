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
LibraryDocumentDetail reload guard.

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
2. System status: the `/status` endpoint, checks, Web `/health`, worker heartbeat
   (first additive migration of this run — apply before its deploy).
3. Test quality phase 2 + the status page — CI surfacing, history branch, one static
   dashboard site carrying both faces. Pages-vs-`$web` fallback is pre-decided.
4. O5 dependency patching + O6 runbooks — config and docs.
5. O1 post-deploy verification + O2 DLQ visibility — probes, pipeline poll, alert,
   status row, peek/resubmit script. All spec'd.
6. D2 deterministic functional bugs — every item carries a repro and a prescribed fix.
   Includes the merge skip-branch row deletion (prerequisite for W2) and the
   stale-response family. Two additive migrations (batch and replay unique indexes).
7. D3 error handling — prescribed, mostly mechanical. Includes the D1 leftovers that
   are metering-shaped: the $0-pricing alert (in the shared recorder) and
   failed-attempt usage recording.
8. D1 remainder — reveal-corrections PrivateGuard, upload-size re-validation at
   confirm, Ask world-switch bleed, concurrent-extraction claim. Sharp specs, but
   auth- and concurrency-critical: **whatever model runs these, the independent
   review rule applies before merge.** If uncomfortable on Opus, these are the first
   thing Fable does on return.
9. Scrub tier 3 convention unification, then tier 4 test pruning — mechanical sweeps;
   the doc names each decision.
10. Test quality phase 5 (authorization-suite tagging) and phase 3 (CRAP hotspots).
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
