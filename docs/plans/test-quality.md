# Test quality visibility

> Part of the Nornis backlog. This file is a spec, not authorization: execute only
> through the Execution order in `docs/future-features.md`, which holds sequencing,
> completion status, and the Opus/Fable gate.

2026-07-31. Chronicis demanded 100% line+branch coverage and enforced it in
`verify.ps1`; `.kiro/steering/testing-strategy.md` commits Nornis to the opposite bet —
coverage as signal, not idol. This plan builds the machinery that makes the signal
visible without reinstating the blanket mandate. Today there is none of it: no
`coverlet.collector` reference anywhere, CI runs a bare `dotnet test`.

What carries over from Chronicis (`.github/workflows/guardrails.yml`, `ci/pages/`):
the collection mechanics (coverlet.collector + runsettings + Cobertura), ReportGenerator
merging, the `coverage-history` orphan branch, and a static dashboard site. What
changes: no global gate — floors only where logic lives, set from observed reality;
CRAP-score risk-weighting replaces "everything is red until 100%"; a named
authorization suite replaces uniformity; and a qualitative audit layer covers the axis
no coverage number sees — assertion depth and false confidence.

Ground rules:

- Phases 1→2→3 are sequential. Phase 4 needs about two weeks of phase-2 history.
  Phases 5 and 6 are independent and can land any time.
- The bar is unchanged: clean `dotnet build Nornis.sln` with warnings as errors,
  tests green, nothing here touches the schema.
- Coverage excludes `Nornis.Infrastructure.Migrations` and compiler-generated code
  from day one — phantom uncovered lines poison the signal before it starts.
- All scripts are pwsh and must run on both Windows dev machines and the
  ubuntu-latest runner.
- Local runs must tolerate the dev-server file locks on `Nornis.Api.Tests` /
  `Nornis.Web.Tests`: run test projects individually, report what was skipped,
  don't fail the whole run.

## Phase 1 — collection and a local report

- Add `coverlet.collector` to every test project (six once the scrub plan's tier 2
  deletes `Nornis.Shared.Tests`; instrument whatever exists at implementation time).
- One `coverlet.runsettings` at repo root: `Include=[Nornis.*]*`, exclude the
  migrations namespace, `ExcludeByAttribute` for GeneratedCode / CompilerGenerated /
  ExcludeFromCodeCoverage, Cobertura output.
- Pin ReportGenerator in a dotnet tool manifest (`.config/dotnet-tools.json`) so
  local and CI share one version — an improvement over Chronicis's `tool update -g`.
- `scripts/coverage.ps1`: per-project `dotnet test --collect:"XPlat Code Coverage"`,
  ReportGenerator merge to `artifacts/coverage/` (gitignored), open the HTML report.
  `-Projects` switch to subset; lock-skip behavior per the ground rule.
- No gates. Done means: one command yields a browsable per-assembly, per-class HTML
  report with migrations excluded.

## Phase 2 — CI surfacing and the dashboard

- PRs (`ci.yml`): replace the bare test step with collection + ReportGenerator
  `MarkdownSummaryGithub` into `$GITHUB_STEP_SUMMARY` — a per-assembly line/branch
  table on every PR. No publishing from PRs.
- Main (new `coverage.yml` on push, keeping `deploy.yml` lean): collect, merge,
  generate `Html;Cobertura;MarkdownSummaryGithub`; append
  `{date, sha, per-assembly line/branch}` to `history.json` on an orphan
  `coverage-history` branch — the Chronicis pattern verbatim.
- Dashboard: a static site in `ci/pages/` — Nornis-styled index, trend chart drawn
  from `history.json` (per-assembly lines over time), CRAP hotspot table (phase 3),
  authorization-suite count (phase 5), and the live system-status page ("System
  status" plan below).
- **Exposure decision, made here:** the full ReportGenerator HTML report embeds
  annotated source. The public dashboard gets aggregates, trends, and method names
  only; the full report attaches to the workflow run as a build artifact, not the
  site. (Chronicis publishes its full report; Nornis's source stays private.)
- Hosting: GitHub Pages if the repo's plan allows it; otherwise the `$web` static
  container on the existing Azure storage account — same artifacts, different upload
  step.

## Phase 3 — CRAP-score hotspots

- CRAP(m) = comp(m)² × (1 − cov(m))³ + comp(m), from the merged Cobertura — coverlet
  already emits per-method cyclomatic complexity. Adapt the `dotnet-test` plugin's
  `Compute-CrapScores.ps1` / `Extract-MethodCoverage.ps1` into `scripts/crap-report.ps1`.
- Output: top-30 table (method, complexity, line coverage, CRAP) as markdown + JSON,
  emitted by the main-branch workflow onto the dashboard and step summary.
  Conventions: CRAP ≥ 30 red, ≥ 15 amber.
- Use: the hotspot list *is* the test-writing backlog, ordered by risk instead of by
  whatever a gate is yelling about; the steering doc's priority list breaks ties.
  This is the piece Chronicis never had — its gate treated an uncovered visibility
  branch and an uncovered DTO mapper as the same emergency.

## Phase 4 — floors where logic lives

- After ~two weeks of `history.json`: per-assembly line and branch floors for
  `Nornis.Domain` and `Nornis.Application` only, in a checked-in
  `coverage-thresholds.json`. CI (PR and main) fails below floor.
- Floors start a couple of points below observed reality; ratcheting up is a normal
  PR when the trend holds. Never ratchet down without a written why.
- Explicitly never: a solution-wide aggregate gate, floors on
  Api/Web/Worker/Infrastructure (report-only forever until proven otherwise), or
  100% anywhere.

## Phase 5 — the authorization suite as a named check

- Tag every test covering the steering doc's authorization list (anonymous
  rejection, non-member denial, GMOnly invisibility, observer immutability,
  private-content scoping, Ask retrieval) with NUnit `[Category("Authorization")]`.
  The `dotnet-test` plugin's `test-tagging` skill can sweep the retrofit; review the
  tags by hand — the enumeration is the value.
- CI: `dotnet test --filter "TestCategory=Authorization"` as its own named check on
  PRs, so "Authorization suite" is a distinct green mark, not a share of a
  percentage. Dashboard charts the suite's size over time.
- This is the spiritual successor to Chronicis's 100%: total coverage of a
  deliberately enumerated surface. Every **[auth]**-flagged change (see the scrub
  plan) adds to the suite — the count only goes up.

## Phase 6 — qualitative audits

- Baseline audit with the `dotnet-test` plugin (`test-quality-auditor` agent,
  `grade-tests`, anti-patterns, smells, gap analysis) → `docs/test-quality-baseline.md`:
  per-test letter grades, findings, gaps against the priority areas.
- Run it **after** the scrub plan's tier 4 lands — grading tests already scheduled
  for deletion wastes the audit.
- Per-PR: `grade-tests` on changed test files when a PR touches priority areas —
  on-demand, PR-comment table, never blocking. Re-audit after each major feature
  wave and diff against the baseline.
- Deliberately not CI-enforced: this layer exists to surface judgment, not to become
  the next dashboard to flatter.

## Interaction with the scrub plan

- Tier 2 deletes `Nornis.Shared.Tests`; phase 1 instruments what remains.
- Tier 4 prunes redundant tests. If coverage moves materially when it lands, the
  pruning cut something real — that is the tripwire, and phase 2's trend chart is
  what makes it visible.
