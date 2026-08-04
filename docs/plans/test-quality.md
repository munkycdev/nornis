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
- **Exposure decision, revised 2026-08-01:** this originally read "the full report
  attaches to the workflow run as a build artifact, not the site — Nornis's source
  stays private." That premise was false: the repo is **public**, so the annotated
  source in the full ReportGenerator HTML exposes nothing GitHub does not already
  serve. The decision stands, but on the honest reason — a dashboard people read at a
  glance should be aggregates, trends and method names, not a source dump. Publishing
  the full report is now a free choice, not a risk. Do not inherit the old sentence as
  a security constraint.
- **Hosting decision, made 2026-08-01: GitHub Pages, not `$web`.** Not a preference —
  a failure-domain requirement, and it is the status page (below) that forces it.
  `$web` sits on the same Azure storage account the application uses, so a storage or
  regional outage takes down the app *and* the page whose only job is to say the app
  is down. Pages fails independently. The repo is public, so Pages is available on the
  free plan; the "if the repo's plan allows it" caveat is resolved.
  - The API's `Status:DashboardOrigins` setting exists for this: set it to the Pages
    origin so the page can fetch `/status` cross-origin. Nothing else on the API is.
  - A `status.nornis.app` CNAME to Pages is safe *because* `nornis.app` DNS is at the
    registrar (Namecheap), not Azure. Were DNS ever moved into Azure, the custom
    domain would quietly re-couple the two and the raw `*.github.io` URL would become
    the correct address.

## Phase 3 — CRAP-score hotspots

**Done 2026-08-01**, pulled ahead of its place in the execution order because the
dashboard was being built anyway and the data was already in the merged Cobertura.

- **Read the merged Cobertura, not the raw per-project files.** Merging is what makes a
  method covered by one test project and touched by another read correctly — and
  ReportGenerator also resolves async state machines, so `Foo/<BarAsync>d__7.MoveNext`
  arrives as `Foo.BarAsync` without any unmangling of our own.
- **`BuildRenderTree` is excluded, and the list is worthless without that.** It is what
  the Razor compiler emits for a component's markup: enormous, uncovered, and written by
  nobody. Left in, it holds seven of the top ten (SourceDetail alone scores 4970) and
  buries every hand-written method. Coverlet's filters are assembly- and type-scoped, so
  it cannot be dropped at collection time without also losing the `@code` block in the
  same file — the exclusion has to live in the report script.
- Consequence worth remembering for phase 4: `Nornis.Web`'s coverage *percentage* is
  still depressed by that same generated markup, since it cannot be excluded from
  collection. Another reason floors never go near Web.
- First run: 1978 methods scored, 226 red, 175 amber. The top of the list is
  Blazor component handlers and the three AI response parsers
  (`ParseProposals`, `ParseFindings`, `ParseLinks`) — model output parsing with no tests
  behind it, which is exactly the risk-weighted answer a percentage could not give.

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

- **Mechanism done 2026-08-03; the numbers are still open.** `coverage-thresholds.json`
  and `scripts/coverage-gate.ps1` exist and run on PRs and on main. Every floor is
  `null`, which the gate reports and passes — so opening the gate is editing a number,
  not building a feature, and the timing gate below is the only thing left.
  - Split this way on purpose: the mechanism is mechanical and the floors are a judgment
    call about a trend, and holding the first hostage to the second left neither done.
  - `-Suggest` prints each floor as it would be two points under what was just observed,
    for use against `history.json` rather than against a single run.
  - An assembly named in the thresholds file but absent from the report fails the gate
    rather than being skipped. A rename or a dropped test project would otherwise switch
    the gate off silently and leave a green tick behind — which is worse than no gate.
  - `scripts/coverage.ps1` now also emits `JsonSummary`. CI asked for it and the local
    script did not, so the gate could not be run on a dev machine at all — including
    `-Suggest`, which exists to be run by a person.
- Still open: after ~two weeks of `history.json`, set per-assembly line and branch floors
  for `Nornis.Domain` and `Nornis.Application` only. Collection started 2026-08-01, so
  the window opens around 2026-08-15; three days of history mostly measures whatever was
  being worked on that week.
- Floors start a couple of points below observed reality; ratcheting up is a normal
  PR when the trend holds. Never ratchet down without a written why.
- Explicitly never: a solution-wide aggregate gate, floors on
  Api/Web/Worker/Infrastructure (report-only forever until proven otherwise), or
  100% anywhere.

## Phase 5 — the authorization suite as a named check

**Done 2026-08-02. 303 tests tagged, 318 test cases.**

- The enumeration was the work, exactly as this section predicted, and the first two passes
  were both wrong in an instructive way. Matching any test that *mentions* a role caught 390
  — pagination and empty-state tests among them. Matching on assertion shape alone still let
  in a caching test and a `/health` test of mine, because `Is.Empty` near the word "Player"
  proves nothing.
- What survived: a test is in the suite when it **asserts a denial** (401, 403,
  `insufficient_role`, `access_denied`) or when its **name states the scoping fact**
  (`GmOnlyPin_IsHiddenFromPlayer`, `Observer_SeesNoPrivateContentAtAll`). Both halves are
  mechanical enough to re-run and tight enough that the count means something.
- The positive halves of gates are in deliberately — `GmOnlyArtifact_IsVisibleToAGm` is the
  twin of the invisibility test, and a suite that only counts refusals would let someone
  "fix" a leak by denying everyone.
- Distribution: Application 152, Api 135, Infrastructure 15, Domain 8, Web 8, Worker 0. The
  worker's zero is correct — it authorizes nothing; it processes what the API already
  admitted.
- CI runs it as its own step with `if: always()`, so a red build still answers "did the
  authorization tests hold" — the first question worth asking about one.
- The dashboard charts the count from `history.json`. The count is emitted per-project with
  `LogFilePrefix`, not `LogFileName`: a fixed name makes every project overwrite one file
  and the total silently becomes whichever project ran last (135, not 318).


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

- ~~Baseline audit~~ **Done 2026-08-03** → [test-quality-baseline.md](../test-quality-baseline.md).
  Run with the audit skills directly rather than the `test-quality-auditor` agent.
  - Verdict: no Critical findings — every automated Critical hit was a false positive, and
    the baseline records which, so the next audit does not re-raise them. One High (four
    constructor tests asserting only `Is.Not.Null`), fixed the same day. Four Medium/Low,
    consciously accepted with reasons.
  - Two of the plan's four listed techniques were deliberately skipped and the baseline says
    so: per-test letter grades (`grade-tests` is built for a curated set — the changed files
    in a PR — not for 2,556 methods), and `test-smell-detection` (the academic catalogue,
    which is meant to be asked for by name; the pragmatic anti-pattern set was used instead).
  - The finding worth carrying: this month's real defects were not missing tests inside a
    priority area. They were rules living in two places, and behaviour nothing exercised at
    all. Line coverage cannot see either, which is why this layer is not a percentage.
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
