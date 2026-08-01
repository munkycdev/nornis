# Execution order

2026-08-01. The master sequence across every plan below, ordered for execution.
Sorting rule: work whose judgment calls are already made in these specs — mechanical
sweeps, config, CI wiring, precisely-scoped fixes — sits at the top and is safe to run
on Opus; work whose *value is* the judgment (policy decisions, prompt and product
design, architectural carving, subtle concurrency) holds for Fable. Items reference
their specs below; nothing here restates them.

**Done** (2026-07-31 → 08-01, deployed): scrub tiers 1 and 2; D1's applicator by-id
gates and drifted-visibility leaks (via 1.2/1.7 + the auth review's fixes: world-scoped
review-queue name resolution, Draft gate on the Ask session feed, own-row visibility on
fact/relationship updates); D4's validator-enum rejection (via 1.7); scrub 1.10's
LibraryDocumentDetail reload guard.

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

---

# Implementation scrub plan

2026-07-30. Product of a nine-reviewer audit of the whole tree, one reviewer per layer,
looking for implementation details that read as generated rather than engineered. The
verdict was consistent across all nine: individual files pass review, but cross-cutting
concerns were re-solved per session and never swept. Every tier below is a sweep the
codebase already started somewhere and didn't finish.

Ground rules for whoever picks this up:

- Line numbers are as-of-audit; verify each citation against the current tree before
  changing anything.
- Tiers are independently shippable, one branch each. Within a tier, items are ordered
  by signal; nothing depends on a later item.
- Items touching authorization or visibility (marked **[auth]**) get an independent
  review after implementation.
- The bar is unchanged: clean `dotnet build Nornis.sln` with warnings as errors, tests
  green, migrations additive (nothing here touches the schema).
- Not in scope: comments and docs are already clean apart from what tier 5 lists;
  `#region` markers stay; `.kiro/`, `CLAUDE.md`, and git history stay.

## Tier 1 — consolidations ✅ (done 2026-08-01, commits 3398f61..d3cc069)

The loudest tell is the same problem solved N times with drift. Each item names the
copies, the shared form, and any drift the consolidation must resolve rather than
preserve.

### 1.1 One error-to-HTTP mapper

`MapError` is re-declared 24 times across 23 controllers, and every switch arm is
behaviorally identical to its own fallback. Copies have drifted: HealthController drops
the 409 arm, TutorialController drops 403, CostsController carries two copies in one
file that sanitize 5xx, and the literal "Something went wrong. Please try again." now
lives in four places.

- Add one extension: `AppError.ToActionResult()` → `ObjectResult(new ErrorResponse(Code, Message)) { StatusCode }`.
- Decide the 5xx-sanitization policy once, in that extension, and delete the four
  scattered copies of the sanitized message (LoremasterService already returns it;
  controllers re-sanitizing it is double work).
- Delete all 24 local mappers.

### 1.2 One source-visibility predicate **[auth]**

`SourceVisibilityRule` (Domain) exists so this predicate cannot fork. It has forked:
six private service copies (JourneyMapService:222, MapViewService:263,
SourceKnowledgeService:146, SourceLocationService:169, StorylineDevelopmentReader:41,
plus SuggestionService:177's role-first rewrite) and seven inline repository copies
(four in ArtifactRepository, one each in ArtifactFactRepository,
ArtifactRelationshipRepository, two in SourceRepository).

- Services: call `SourceVisibilityRule` (or promote
  `StorylineDevelopmentReader.CanSeeSource`, already `public static`, and make it
  delegate). Known drift to resolve, not preserve: JourneyMapService's copy lacks the
  `Guid.Empty` identity guard and the Draft gate; ArtifactService's has the guard but
  no Draft gate.
- Repositories: per-entity `Expression<Func<T,bool>>` factories beside
  `VisibilityFilter.CanSee`, so the four ArtifactRepository copies collapse to one
  private static.

### 1.3 One authorized-proposal loader in ReviewService **[auth]**

The load proposal → batch → source → visibility → role ladder is pasted five times
(ReviewService:262, 429, 480, 668, 772) and diverged: **EditProposalAsync checks only
`batch is null` where the other four check `batch.WorldId != command.WorldId`** — the
one path with no world-scoping on the proposal. Also in all five copies: a third
status guard ("only Pending or Edited") that is unreachable after the Accepted and
Rejected checks, with the Edit copy returning 400 where the others return 409 for the
impossible state.

- One private `LoadAuthorizedProposalAsync` returning `(proposal, batch, source)` or
  an error; all five methods use it. World-scope check included — this closes the Edit
  gap, which is the one behavioral change in the tier and the reason for the review flag.
- Replace the three sequential status guards with one `switch`.
- While in the file: give BatchRejectAsync the batch/source memoization BatchAcceptAsync
  already got, and make `BuildProposalContextAsync` resolve display names from the ids
  actually present in the page of proposals instead of loading every batch, artifact,
  and fact in the world.

### 1.4 One AI usage tracker

`TrackUsageAsync` + `CalculateCost` (same formula, byte-for-byte in places) exist in
~10 services: ContinuityAuditService:751, ContinuityFixService:490,
LoremasterService:331, LibraryIndexingService:177, RelationshipBackfillService:505,
StorylineWrapUpService, StorylineRetrospectiveService:288, and three sites inside
ExtractionService (379, 874, 1268).

- One `IAiUsageRecorder.RecordAsync(worldId, userId, operationType, usage, succeeded, errorCode)`
  owning the pricing math.
- Prerequisite that also stands alone: the nine AI response records each re-declare the
  usage block (InputTokens/OutputTokens/TotalTokens/DurationMs/Model) and five request
  records are `{ SystemPrompt, UserMessage, Model, TimeoutSeconds }` verbatim. One
  `AiUsage` value embedded in responses, one shared prompt-request record. This is what
  makes the tracker's signature clean instead of tuple-shaped
  (see `TrackVisionUsageAsync`'s anonymous tuple parameter today).

### 1.5 One AI-call executor and one exception taxonomy

The nine Azure OpenAI clients in Infrastructure/Ai are one template pasted nine times.
The stopwatch + timeout-CTS + catch-ladder preamble drifted into three exception
vocabularies: timeout surfaces as `AiExtractionTimeoutException`,
`AiLoremasterTimeoutException`, or bare `TimeoutException` depending on the client, and
six clients throw malformed-model-output as `HttpRequestException` — a parse problem
wearing an HTTP costume, which is exactly the distinction TransientFailureClassifier
exists to make. Several ladders contain no-op catch blocks whose only job is to shield
typed exceptions from a final catch-all.

- One executor: run call with timeout, translate `ClientResultException`/timeout once,
  return content + usage + duration. Each client shrinks to prompt + schema + parse.
- One taxonomy in Application/Ai (timeout, parse, transient-HTTP, permanent-HTTP). Move
  the two Loremaster exceptions out of Infrastructure — the extraction path already
  proved the layering; LoremasterService's `IsRateLimitByTypeName` string-matching
  workaround (LoremasterService:375) then dies.
- Drop the catch-alls; the shield blocks disappear with them.
- Application side: ExtractionService's four near-identical five-block cascades (327,
  491, 803, 1080) collapse into one `ExecuteAiCallAsync<T>` mapping exception → outcome.

### 1.6 Enums in API contracts

`Type`, `Visibility`, `Status`, `Role` are strings in every request record, hand-parsed
in 22 `Enum.TryParse` → 400 blocks across 10 controllers. CanonController:70 documents
the hole (TryParse accepts "7") and fixes it in exactly one of the 22 sites.

- Either `JsonStringEnumConverter` on the contracts or one shared
  `TryParseDefined<TEnum>` helper. Kills all 22 blocks and propagates the
  `Enum.IsDefined` fix everywhere at once.

### 1.7 Validator/applicator seam

- ProposalValidator: the identical deserialize-and-null-check prologue opens all eight
  arms (~120 of 371 lines). One generic `TryDeserialize<T>(json, label, out payload, out error)`.
- The validator and ProposalApplicator each declare their own identical
  `JsonSerializerOptions` with the same explanatory comment; a payload accepted by one
  and unparseable by the other is the failure mode both files worry about. One shared
  `ProposalJson.Options`.
- Align create/update rigor (update currently skips the 200-char name cap and the
  status enum check that create enforces) and pick one policy for unparseable enum
  strings — today it's reject / silently ignore / silently default depending on the arm.
- Applicator: one `ResolveTargetArtifactAsync(worldId, id, name, filter)` replacing the
  three inline resolutions that disagree about the world-scope check **[auth]**; batch
  the merge reassignments instead of one `UpdateAsync` (each with its own SaveChanges)
  per fact/relationship/pin; decide the self-referencing-relationship case before
  mutating the tracked entity, not after.

### 1.8 Web display helpers

`Ago` (6 copies), `StatusColor` (7), `FormatSize` (4 + a dialog), `Humanize` (7 across
pages and panels), `HealthColor` (2), and the status/visibility literal arrays (6) are
re-declared per component. The codebase's own convention exists
(`Services/SourceTypeDisplay`, `ArtifactTypeDisplay`, `LoremasterDisplay`).

- Add `RelativeTime.Ago`, `DisplayText.Humanize`, `LibraryDisplay`
  (StatusLabel/StatusColor/FormatSize), and shared status constants; delete the copies.
- Known user-visible drift this fixes: the library detail page shows raw "IndexFailed"
  and "1228.8 MB" where the list shows "Index failed" and "1.2 GB".
- LoremasterPanel re-implements `CleanAnswer`/`ConfidenceColor`/`BuildContext` that
  `LoremasterDisplay` centralizes for the other two surfaces; fold it in, including the
  conversation-context wire format and storage-key literal.

### 1.9 One Truncate

Ten private `Truncate` declarations, two semantics (raw cut vs ellipsis cut), applied
in places where the input cannot exceed the limit and skipped in one place where it
can. One string extension with an explicit ellipsis option, applied only where
truncation is possible.

### 1.10 Small real defects found via drift

Fix alongside whichever tier touches the file: "open question" compared
case-sensitively in StorylineRetrospectiveService:273 where its three siblings use
OrdinalIgnoreCase (promote the predicate to one shared helper + public const);
StorylineRetrospectiveService:83's per-storyline fact loop (batch method exists two
files away); LibraryUploadDialog creates a `DotNetObjectReference` and never disposes
it (only JS-interop component in the folder without the house dispose pattern);
LibraryDocumentDetail collapses transient API failures into "doesn't exist" and uses a
weaker reload guard than its two sibling detail pages; SourcesController
constructor-injects the blob-dependent attachment service in violation of the
action-injection invariant WorldsController documents three times; the reveal endpoint
refetches the source RevealService just loaded (return it from the service instead).

## Tier 2 — deletions ✅ (done 2026-08-01, commit 1f21e55)

Scaffolding generated to satisfy a phase plan and never retired.

- Three `Placeholder.cs` files (Shared, Application, Infrastructure).
- `Nornis.Shared` and `Nornis.Shared.Tests`: an empty project referenced by all five
  siblings, and a test project whose one test is `Assert.That(true, Is.True)`. Delete
  both from the solution and remove the project references. (Alternative if
  contract-sharing between Api and Web ever becomes desirable: this is where the
  Contracts.cs mirrors would live. Today the client-owned-contracts decision is
  documented and deliberate — so delete, don't repurpose.)
- `ConversationRole` enum (zero production references) and its entries in
  EnumDefinitionTests.
- `WorldMemberFilter` (dead twin of WorldMemberActionFilter, never registered, already
  flagged by the performance audit) — and correct HttpContextExtensions:24, whose
  exception message names the dead class.
- `NornisApiClient.GetHealthAsync` + `ApiHealth`/`ApiHealthStatus` (no callers).
- Speculative repository surface: `AiUsageRecordRepository.QueryAsync` (no production
  callers), the `VisibilityScope? visibility = null` parameters on
  ISourceRepository/IArtifactRepository (~25 call sites, all pass null; the required
  `VisibilityFilter` methods are the surviving idiom), the unreachable
  `CompletedAt` branch in ReviewBatchRepository:110.
- RedeliveryBackoff: `MaxDelay` and the exponent clamp are unreachable behind the
  deliveryCount early-return; the XML doc promises a 60s step that cannot happen.
  Delete the dead half and fix the doc (or delete the early return — pick one).
- `AiExtractionTimeoutException.DurationMs`: set at both throw sites, read nowhere.
  Log it or drop it.
- `tests/Nornis.Web.E2E/`: untracked bin/obj remnants only, not in git or the
  solution. Delete from disk.
- Sundry one-liners flagged by reviewers: `CostSummary.Empty` (identical to `new()`),
  the anonymous-object response in ArtifactsController:55 (the API's only one),
  `Worker.Tests`' copy of SanityTests.

## Tier 3 — convention unification

Same decision made two or three ways across sibling files. Pick once, apply everywhere.

- **Delete return type**: `AppResult`, not `AppResult<bool>` that only ever carries
  true (LibraryService, SourceAttachmentService, WorldDeletionService).
- **Missing-row contract**: SourceRepository throws, ImportSessionRepository silently
  returns, ReviewBatchRepository uses bare `FirstAsync`, deletes no-op in two shapes.
  Decide per-verb (mutations throw, deletes no-op is a defensible pair), document it on
  the Domain interfaces, and align the implementations.
- **GM-gating seam** **[auth]**: three styles today (inline in four controllers, a
  `RequireGm()` helper in one, service-layer-only in the documented majority). Adopt
  the service-layer pattern everywhere; keep the one documented defense-in-depth site
  (WorldMembersController.ListAddable); delete the rest of the inline checks and their
  slightly-differently-worded duplicate messages.
- **Trust the filter**: SourceService-style `ActingUserRole` on the command everywhere,
  instead of WorldsController/WorldMemberService/WorldInviteService re-fetching the
  membership row the action filter already resolved (up to three identical queries per
  request today).
- **Options classes**: `public const string SectionName` on all seven, not four; hosts
  bind by the constant.
- **Persisted enums**: explicit values on all of them, not just the three newest.
- **InMemory-provider strategy**: three answers today (tracked-load-always,
  `Database.IsRelational()` branch, unconditional ExecuteDelete that would throw).
  Generalize the IsRelational helper and apply it; also collapse SourceRepository's
  four copy-paste single-column mutators onto one private `MutateAsync(id, apply, ct)`.
- **DI constructor null-guards**: the repo splits ~50/50 on
  `?? throw new ArgumentNullException`. Under DI they're unreachable; drop them all.
- **Small idioms**, enforceable via .editorconfig where possible: collection
  expressions vs `new List<>` (currently 84/91 split — IDE0028/IDE0305),
  `IsNullOrWhiteSpace` as the default check, `IReadOnlyList<T>` uniformly on the API
  client (one method returns `List<T>`), the `Query()` helper for all query strings in
  NornisApiClient, `Task.WhenAll` for independent loads (serial sites:
  Sources.razor:145, ArtifactDetail.razor:808, PublicWorldSourceDetail.razor:108,
  WorldSettingsPanel.razor:452), one role-comparison idiom plus a `WorldState.IsGm`
  property replacing ~25 raw `== "GM"` comparisons and two local variants.

## Tier 4 — test suite pruning

The suite is two strata. The newer register (NavActivityCadenceTests,
RedeliveryBackoffTests, the import-walk fixtures) is the model; this tier prunes the
older spec-shaped layer down to it. Expect the test count to drop meaningfully; that is
the point.

- **Enum ledger tests**: delete the fifteen `*_HasNoUnexpectedValues` count twins
  (strictly weaker than their `Is.EquivalentTo` siblings, can never fail alone) and the
  two running-ledger tests (`AllEnums_AreInExpectedNamespace` with its hand-maintained
  count of 28, and EntityStructureTests' equivalent).
- **Reflection roster**: delete `RepositoryInterfaceContractTests.ExpectedMethods` and
  its existence test (the compiler does this); keep the CancellationToken/Task
  convention sweeps in the same file — those are real architecture tests.
- **Property-test theater**: ReviewServicePropertyTests1–4 contain FsCheck properties
  whose generated input is ignored while a deterministic body runs 100 times. Convert
  to plain `[Test]`s (most scenarios already exist in ReviewServiceAcceptTests) or
  route the generator through; merge the four numbered files into concern-named
  fixtures with one shared factory instead of four ~35-line construction helpers. The
  newer PropertyTests folder (real `Gen.Elements` + adversarial strings) shows the
  house standard.
- **Per-field decomposition**: LoremasterServiceUsageTrackingAndErrorHandlingTests'
  seven one-field assertions over the same arrange become one record-fields test; the
  3×3 error grid becomes one `[TestCase]` over (failure kind, status, code). Same
  treatment for AzureOpenAiExtractionClientTests' nine 20-line JSON literals differing
  by one key — one JSON-mutating helper + `[TestCase("changeType")]`, asserting the
  exception names the offending field.
- **Attribute padding**: CostsControllerTests' region promising a 403 test that
  contains only attribute-reflection checks (the behavior is covered by the
  integration fixture) and the redundant `Received(1)` pass-through beside an
  exact-argument stub.
- **Consolidation sweep**: ProposalApplicatorTests' earlier tests inline the 10-line
  seeding block the file's own mid-file helpers abstract; sweep them onto the helpers.
- The "Validates: Requirements N.N" stamps across 73 files go with tier 5's comment
  pass.

## Tier 5 — comment pass

The razor: a comment earns its place by teaching the system (an invariant, an ops
reality, a cross-boundary contract), not by narrating the code's relationship to other
code. "Mirrors X" is legitimate exactly when no compiler can enforce the sameness.

- **Delete**: same-assembly sameness-narrators ("identical to MapViewService's gate",
  "parity with LoremasterService", the three "Mirrors IAuditAiClient" headers) — most
  die mechanically with tier 1, since the refactor removes the code they annotate;
  justify-to-reviewer parentheticals; numbered spec-step comments ("// 5. Empty body
  short-circuit"); "Validates: Requirements N.N" stamps in tests.
- **Keep untouched**: boundary mirrors — Contracts.cs's client-owned API mirrors
  (separate deployables), ArtifactGraph's JS color/status mirrors (C#↔JS),
  WorldMemory's server penalty-constant mirrors (client↔server), RedeliveryBackoff's
  MaxDeliveryCount note (code↔infrastructure) — and every invariant/incident comment.
- **Open decision, not started until it's made**: whether to compress the essayistic
  register (em-dash aphorisms, counterfactual framing) in the surviving comments.
  ~11% of comment lines carry it. It is also the register of the README and commit
  history — if that voice is staying, consistency beats camouflage and this bullet is
  a no-op.

---

# Test quality visibility

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

---

# System status

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
- Live-only, no history: App Insights availability tests already record uptime over
  time. Link out to that; do not rebuild it on the history branch.
- Sequencing: the endpoint work is independent and useful bare (curl-able) — it can
  land before the dashboard exists. The page needs phase 2's site.

## What does not change

- `/health` semantics, the availability alert watching it, and the post-deploy
  migration health window stay exactly as documented.
- No auth model changes: `/status` joins `/health` in the anonymous carve-out the
  steering doc already defines. Everything else on the API stays authenticated.

---

# Operational hardening

2026-07-31. Six items from an ops review of the deploy pipeline, messaging paths, and
credential setup. The theme the review surfaced: Nornis is well-instrumented for "is
it working" and thin on "what happens when it isn't." Items are independent and
ordered by priority; O1 and O2 interlock with the System status plan above.

Considered and deferred, not rejected: a rehearsed restore drill plus world
soft-delete, Azure-side cost tripwires, and local-dev/prod separation.

## O1 — post-deploy verification and revision safety

`deploy.yml` currently ends the moment `az containerapp update` returns; nothing
confirms the new revision came up.

- Wire `/health` as Container Apps liveness + readiness probes on `ca-nornis-api`
  (and on Web once the status plan gives it a `/health`), so a failing revision
  never takes traffic and the old revision keeps serving.
- Pipeline step after the update: poll the public `/health` until healthy, with a
  timeout that fails the run loudly. The response body names the failing check, so
  the step can distinguish "app broken" from the known pending-migrations window and
  say "run the manual migration step" instead of a bare failure.
- The Worker has no probe surface; its post-deploy verification is the
  worker-heartbeat check in the System status plan.
- `containerapp update` kills mid-extraction work. Safety rests on Service Bus
  redelivery plus the idempotency items in the defect plan below — this raises
  their priority.

## O2 — dead-letter queue visibility

`RedeliveryBackoff` deliberately preserves the dead-letter backstop; nothing watches
the backstop. A message that exhausts retries today vanishes silently.

- Azure Monitor alert on dead-lettered message count > 0.
- A `dlq` row in the `/status` `deps` checks — message count only, via the
  admin client.
- `scripts/dlq.ps1`: peek, resubmit, purge — the runbook companion for O6.

## O3 — managed identity sweep

SQL, blob, and Service Bus all authenticate by connection string in config. The
deploy pipeline already uses OIDC; extend the pattern to runtime.

- Blob and Service Bus first (easy): `DefaultAzureCredential` + endpoint, with
  Storage Blob Data Contributor and Service Bus sender/receiver roles on each app's
  identity. SQL after: Entra auth with contained users provisioned per identity.
- Config becomes endpoints, not secrets; the startup guards that demand connection
  strings change wording to demand endpoints.
- Local dev keeps working via `DefaultAzureCredential`'s az-login fallback against
  the same resources — unchanged until dev/prod separation is taken up.

## O4 — AI kill switch

Per-world budgets cap spend; nothing can pause all paid AI during a provider
incident or runaway behavior without a redeploy.

- One global flag in a new single-row operational-flags table (additive migration),
  read with a ~60s cache at every paid-AI dispatch seam: extraction, Ask, continuity
  assessment, library indexing. Flipped by script; no admin UI needed yet.
- **Paused must not mean dead-lettered:** the Worker re-schedules messages using the
  same scheduled-copy mechanism `RedeliveryBackoff` already uses, so queued work
  waits out the pause without burning delivery counts. Interactive paths (Ask,
  assess) return an explicit "AI is paused" error.
- The status page renders the flag as a banner when set — a pause should look
  deliberate, not broken.

## O5 — dependency patching

- Dependabot: `nuget` + `github-actions` ecosystems, weekly, minor/patch grouped
  into one PR.
- `dotnet list package --vulnerable --include-transitive` as a CI step that fails
  on findings. The existing gates (warnings-as-errors, tests, format) are what make
  auto-bump PRs safe to merge quickly.

## O6 — runbooks

`docs/runbooks/`, one short doc per nameable failure mode: worker dead, migration
missed, DLQ non-empty, Auth0 outage, budget cap hit, AI paused. Each: the symptom
(which alert fires, what `/status` shows), diagnosis steps, remedy commands,
verification. Every Azure alert's description links to its runbook — an alert that
doesn't say what to do next is half an alert.

---

# Defect remediation

2026-08-01. Product of a follow-up scan with a different lens: not authorship tells but
genuine engineering defects. Five reviewers (transactionality/idempotency,
architecture/SOLID, Web state safety, silent-failure paths, infrastructure I/O) plus a
hands-on read of the review/applicator core. Same ground rules as the scrub plan above:
verify line numbers before changing anything, **[auth]** items get independent review
after implementation, migrations stay additive (the two index fixes below qualify).
Where a defect's fix *is* a scrub tier item, it is cross-referenced — the defect
changes that item's priority, not its shape.

## D1 — security and data integrity (do first, before or alongside tier 1)

- **[auth] Applicator by-id resolution skips both world and visibility checks.**
  `ProposalApplicator.ResolveRelationshipEndpointAsync` by-id branch (:903-917) checks
  only null — never `artifact.WorldId == batch.WorldId`, never `actingFilter` — while
  the by-name branch beside it applies the filter with a doc comment explaining
  exactly why the payload is dangerous (player-editable). Same gap in
  ApplyUpdateArtifact (:465), ApplyMergeArtifact (:522), ApplyUpdateFact (:681),
  ApplyUpdateRelationship (:832), ApplyAddFact's TargetId branch (:624). Scenario: a
  Player edits their own proposal's payload to a GM-only or cross-world GUID and
  accepts it — the relationship binds to the hidden artifact and its existence leaks
  through graph and canon surfaces. Fix: world-scope + `actingFilter` gate on every
  by-id load (facts via their parent artifact). Executes naturally with scrub 1.7's
  `ResolveTargetArtifactAsync` consolidation.
- **[auth] The drifted visibility copies are a live leak, not just a tell.** The six
  hand-rolled predicates enforce only the scope gate — they miss the Draft gate the
  canonical `SourceVisibilityRule` enforces. Import-walk notes are created
  PartyVisible+**Draft** (`ImportSessionService.cs:125`), so
  `SuggestionService.cs:166-174` can quote a draft import's title to a player that the
  sources list correctly hides. Second axis: five copies also lack the
  `Guid.Empty` anonymous-identity guard on paths reachable from `PublicController`.
  Fix = scrub **1.2**, promoted from cleanliness to leak closure. (Note:
  `ReviewService.IsSourceVisibleToUser` looks like a seventh copy but is a different,
  correct rule — review authorization; rename it rather than "fixing" it.)
- **[auth] Reveal corrections skip the Private guard.** `RevealService.cs:175-191`:
  steps 1-3 all apply `PrivateGuard`; the corrections step does not, and runs under
  `VisibilityFilter.All` — a correction naming a player's Private fact flips its truth
  state and files a PartyVisible reveal as its provenance, violating the class's own
  "never touches Private knowledge" contract. Fix: `PrivateGuard` in the corrections
  loop.
- **Concurrent duplicate extraction commits two batches for one source.** Idempotency
  is gated on batch-existence plus an unconditional status write
  (`ExtractionService.cs:148-192`, `SourceRepository.cs:253-265`; Source has no
  RowVersion), and lock-lost redelivery (a transcription+extraction run crossing
  `MaxAutoLockRenewalDuration`) runs a second full pass *concurrently*: two batches,
  ~2× proposals, 2× AI spend. Fix: filtered unique index
  `ReviewBatches(SourceId) WHERE Kind IS NULL` (additive) so the second commit fails
  into the existing Skipped path, plus a conditional claim
  (`UPDATE … WHERE Status='Queued'`, rows-affected as the gate).
- **Upload size caps never check the actual blob.** Caps validate the client's
  *declared* `SizeBytes` (`LibraryService.cs:90`, `SourceAttachmentService.cs:205-215`),
  then a Create|Write SAS is issued and `ConfirmUploadAsync` stores the real size
  without comparing it (`LibraryService.cs:146-153`). Downstream pipelines buffer
  whole documents in RAM (`PdfPigTextExtractor.cs:13-15`, the vision paths) — any
  member can OOM the worker with a multi-GB blob, killing both queue processors on a
  repeatable loop. Fix: re-validate `metadata.SizeBytes` at both confirm sites and
  delete the oversized blob.
- **A pricing-key miss silently prices AI usage at $0 — which disables the budget
  guard.** `TryGetValue → return 0m` in six services; `AiBudgetGuard` sums
  `EstimatedCostUsd`, so a renamed deployment or omitted config section means every
  record is $0, `IsExceeded` never trips, and the dashboard shows healthy. One
  options file even documents the failure mode. Fix: warn once per unknown model,
  alert on Succeeded-with-tokens-but-zero-cost, consider a conservative fallback
  price. Folds into scrub **1.4**'s usage recorder.
- **Ask contaminates the other world's saved history on mid-flight world switch.**
  `Ask.razor:292-313` re-reads `_current` after the await; `OnWorldChanged` has
  replaced it, so world A's Q&A is appended to world B's conversation and persisted
  under B's storage key (NRE variant when B has no conversations).
  `LoremasterPanel.razor:183-204` has the same bleed. Fix: capture conversation
  reference and storage key before the await; discard the response if
  `Worlds.Current?.Id` changed.

## D2 — deterministic functional bugs

- **Clearing a source's body or URI silently reverts under a success toast.**
  `UpdateSourceRequest` is partial-update with `ClearOccurredAt`/`ClearCampaign` but
  no `ClearBody`/`ClearUri`; an emptied editor maps to null = "unchanged"
  (`SourceDetail.razor:1015-1072`). The server keeps the old body, the UI says
  "Source updated.", and an intentional body-clear never triggers reprocess. Fix: add
  the two Clear flags API-side and client-side, mirroring the OccurredAt idiom.
  **Ordering dependency:** fix `NotesEditor`'s init/empty conflation
  (`NotesEditor.razor:119-129` — a failed JS init returns null forever) in the same
  change, or the new flag turns a JS failure into a data-destroying clear.
- **Second update of the same row in one request throws.** Repositories read
  `AsNoTracking` and write via `DbSet.Update(entity)`; after SaveChanges the instance
  stays tracked, so attaching a second instance with the same key throws.
  Deterministic: a reveal whose `FactIds` and `Corrections` name the same fact →
  `ApplyUpdateFact` twice → 500 every time (`RevealService.cs:255-284`). Also fails
  the second of two proposals touching one row in a single `BatchAcceptAsync`. Fix:
  repositories reuse the tracked instance (`Find` + `CurrentValues.SetValues`) or
  detach entries after SaveChanges.
- **Merge leaves the duplicate↔target relationship row live, and the continuity audit
  re-flags it forever.** `ProposalApplicator.cs:559-579`'s skip branch discards an
  unpersisted mutation (rows are no-tracking), leaving the DB row pointing at the
  archived duplicate. Confirmed downstream: the target's detail page lists the
  archived duplicate as a connection (`ArtifactService.cs:626-646` has no status
  filter); the continuity audit renders it as "**Unknown artifact**" evidence that
  survives finding validation — a recurring, score-penalizing DanglingThread after
  every merge of related artifacts; `SourceReprocessService.cs:320-321` counts it,
  blocking cleanup. Fix: delete the relationship in the skip branch, inside the merge
  transaction — and correct the comment, which misdescribes the mechanism.
- **Dead-lettered extraction wedges the source at Queued forever.** After five
  redeliveries (~2 minutes of backoff) the message dead-letters; nothing consumes the
  DLQ, and `ValidTransitions` offers no user-reachable exit from Queued — update,
  delete, mark-ready, and reprocess all reject it. Same wedge from the
  crash-between-commit-and-enqueue window. Fix: allow Queued→Ready retry after a
  staleness threshold, or sweep batch-less Queued sources.
- **Two Active replays per world.** Check-then-create with a non-unique index
  (`ExtractionReplayConfiguration.cs:32`) — while `ImportSessionConfiguration.cs:28-31`
  enforces exactly this invariant correctly two files away. Double-click → two Active
  rows, arbitrary advance/cancel targeting, both requeue sources. Fix: filtered
  unique index `(WorldId) WHERE Status='Active'` (additive), map the violation to the
  existing 409.
- **The stale-response family.** One shape, several sites: a load captures an
  identity, awaits, then applies the result without re-checking. `WorldState.
  LoadContinuityCoreAsync` (:224-240) lets the previous world's score clobber the
  current one's; `SourceDetail`/`ArtifactDetail`/`PublicWorldArtifactDetail` paint the
  loser of overlapping detail loads (SourceDetail's comment claims discard checks that
  don't exist); the Sources poll and CostsPanel range switches race the same way.
  `NavMenu.RefreshActivityAsync:404-415` and `Home.RunAssessment:450` already
  implement the guard — apply that three-line pattern at each site. Absorbs scrub
  1.10's LibraryDocumentDetail reload item.
- **Extraction can persist proposals that can never be accepted.** `EnforceVisibility`
  truncates payloads at 50,000 chars (guaranteeing invalid JSON when it fires) while
  the validator caps at 32,768 — and extraction never runs the validator at all. A
  payload between the caps is Pending forever; every accept fails `payload_too_large`.
  Fix: one shared cap constant; run `IProposalValidator` at extraction time, treating
  failures as parse-retryable.
- **Skipping an in-flight import note starts a concurrent extraction.**
  `ImportSessionService.AdvanceAsync` (:462-468) never checks the current item's
  state before dispatching the next — defeating the serialization this feature exists
  to provide. Fix: refuse skip while Extracting, mirroring `item_not_ready`.
- **Ink autosave re-entrancy creates duplicate Draft sources.** `SaveInkAsync` sets no
  flag; two debounced callbacks interleave during a slow first save while `_sourceId`
  is still null → two Drafts, one orphaned (`InkCapture.razor:113-195`). Fix: a
  `_saving` flag with a trailing-dirty bit.
- **Extraction never validates `source.WorldId == worldId`.** A mis-enqueued pair
  extracts normally but checks and bills the *wrong world's* budget silently. Fix:
  assert world consistency at pipeline entry (and standardize worldId-first parameter
  order — both orders currently exist across sibling interfaces).
- **A dead queue processor looks healthy forever.** `StartProcessingAsync` is called
  once with no retry (`ExtractionWorker.cs:38`), exceptions are ignored by design, and
  the worker exposes no health surface — a Service Bus blip at boot silently halts
  extraction until the next deploy. Fix: retry with backoff; surface processor
  liveness or fail fast.
- **Wrap-up reports total failure after partial success.** Closures commit in their
  own transaction; a later step's failure returns an error, the GM retries, and a
  duplicate wrap-up source/batch is minted for an already-closed storyline
  (`StorylineWrapUpService.cs:239-297`). Fix: pre-validate all decisions before the
  closure transaction, or return per-step results (the `BatchOperationResult` shape
  exists).

## D3 — error handling and observability

- **ReviewService has no logger, and its blanket catches swallow everything.**
  :328-333 and :738-742 convert bugs, constraint violations, and concurrency
  conflicts alike into an unlogged generic 500 — the loser of a duplicate-accept race
  gets "transaction_failed" instead of the idempotent result the code promises
  sequential retries. A swallowed conflict also leaves the scoped DbContext poisoned
  (stale Modified entity fails later SaveChanges in the same request — see
  `ExtractionReplayService.cs:139-149`). Fix: inject a logger; catch
  `DbUpdateConcurrencyException` specifically (re-read, return idempotent/409);
  `ChangeTracker.Clear()` after swallowed conflicts; let true bugs propagate.
- **LoremasterService's belt-over-suspenders catch hides bugs and mangles
  cancellation** (:174-178, no logger; a user-cancelled request becomes a 500). Fix:
  narrow it, rethrow when `ct.IsCancellationRequested`, log the rest. Its type-name
  exception sniffing (`IsRateLimitByTypeName`) is scrub **1.5** — confirmed against
  the codebase's own documented string-matching incident.
- **`ReferencePassageRetriever` catch-all swallows cancellation** (:92-97) — shutdown
  reads as "no passages" and extraction continues on a cancelled token. Fix: OCE
  filter-rethrow above the catch-all.
- **Blob container init does sync network I/O in the constructor and bypasses
  exception translation** (`AzureBlobStorageService.cs:33-34`) — a transient storage
  503 at first use surfaces as raw `RequestFailedException`, which the classifier
  can't type-match, wrongly marking documents IndexFailed. Fix: async lazy init
  inside the first operation, wrapped in the same translation as `OpenReadAsync`.
- **Paid tokens from failed attempts go unmetered.** Embedding retries re-pay
  unrecorded batches; parse-failure responses record zero tokens. The guard
  undercounts exactly when spend is roughest. Fix: attach usage to parse exceptions;
  record per attempt.
- **Demo-world name generation is the only unmetered AI call** (no guard, no usage
  record — bounded only by the demo rate limit). Fix: write the usage record even if
  the guard stays off by design.
- **A failed heuristic read silently becomes continuity score 0**
  (`ContinuityAuditService.cs:130-132, :237-238`) — indistinguishable from "the
  record is terrible". Fix: fail the run or mark the input degraded.

## D4 — hardening and design debt

- **Embeddings are the one AI path with no application timeout**
  (`AzureOpenAiEmbeddingClient.cs:18-23`; SDK default ≈ 7 min worst case per batch,
  inside user-facing Ask and against the worker's lock ceiling). Fix: the linked
  timeout-CTS pattern all nine chat clients already use.
- **Azure SDK internal retries stack beneath the designed backoff ladder** (default 3
  per delivery × 5 deliveries — the near-instant re-request behavior
  `RedeliveryBackoff` was written to eliminate, happening below its sight line). Fix:
  set `MaxRetries` explicitly (0-1); classification + backoff own retry policy.
- **The AI budget is check-then-act** — N concurrent runs at budget-ε each buy a full
  call. Overshoot is bounded by worker concurrency; either accept as a documented
  soft cap or insert a provisional usage row inside the check.
- **Zero means opposite things in the two budget caps**: world daily budget `<= 0` →
  guard *off*; public Ask monthly `<= 0` → feature *blocked* — same class. Fix: null
  inherits, 0 blocks, both.
- **The validator accepts what the applicator silently reinterprets**: a GM typo like
  truthState `"Flase"` is coerced to Likely with no error; unparseable Status is
  dropped. Fix with scrub **1.7**: reject unknown enum strings at the validator.
- **ExtractionService is a four-pipeline god class** (1,696 lines, 21 constructor
  dependencies) whose size is already warping API decisions — the nullable-dependency
  hack exists, per its own comment, because the constructions were too numerous to
  update. Fix: extract a MapExtractionPipeline and SourceTextDerivation
  (transcription + attachment derivation) as owned collaborators plus the shared
  usage recorder from scrub **1.4**; keep the orchestrating state machine.
- **The Web re-implements the continuity scoring it renders**
  (`WorldMemory.razor:226-246` mirrors the penalty table, cap, and suspension rule;
  no compiler or test spans the deploy boundary). Fix: the assessment DTO carries the
  breakdown; the razor renders received numbers only.
- **Nullable "optional" DI dependencies turn misregistration into silent feature
  loss** (`ExtractionService.cs:42-49`, `ReviewService.cs:33-35` — replays silently
  stall, grounding silently vanishes). Fix: required parameters with no-op
  implementations (`NoOpWorldNameGenerator` is the house pattern). Same item as
  scrub tier 1's finding; priority raised.
- **`AppError` speaks HTTP inside Application** (~340 sites choose literal status
  codes). Mitigations are real: the 404-anti-existence-oracle policy is consistently
  applied, and the Worker already uses the correct non-HTTP idiom
  (`ExtractionOutcome`). Fix if desired: a semantic error-kind enum mapped once in
  Api — mechanical and wire-compatible; do it with scrub **1.1** or not at all.
- **The prompt seam has two owners**: five clients receive Application-built prompt
  strings; extraction's system prompt — the product's most consequential business
  text — lives in the vendor adapter. Converge on the string seam (an
  Application-side extraction prompt builder; the client keeps transport, timeout,
  parse). Extends scrub **1.5**.
- **The review-provenance invariant is hand-assembled in eight services**, and one
  divergence already exists: `ArtifactMergeService` creates batches with `Kind =
  null` — the value reserved for normal source extraction (currently masked because
  merge batches are born Completed). Fix: `proposal.Accept(userId, now)` on the
  entity, one shared synthetic-batch writer, and a named merge Kind.
- **Smaller items**: Ask history grows localStorage without bound and fails silently
  at quota (cap + one-time snackbar); accepting an invite doesn't persist the world
  selection, so the next full load restores the old world; abandoned PendingUpload
  rows and their blobs are never swept; the indexing pipeline holds an entire
  document's PDF + text + chunks + embeddings in RAM at once (incremental chunk
  writes, or lower the indexable cap); `WorldState.EnsureLoadedAsync` caches a failed
  first load if a future caller passes a CancellationToken — a loaded gun, note only.

## What the scan verified as sound

Recorded so nobody re-litigates it: accept/merge/reveal/reprocess are genuinely
atomic (EfUnitOfWork is real — one scoped DbContext, so per-repository SaveChanges
enlists); RowVersion properly fences duplicate accepts, replay claims, and invite
redemption; worker PeekLock completion ordering is correct and
commit-Queued-before-enqueue holds on every enqueue path; world deletion is atomic
with deliberate best-effort blob cleanup; the budget guard covers every AI path except
demo naming; vector search is exact KNN in SQL, not client-side cosine; Service Bus
sender/processor lifetimes are right; all nine chat clients link their timeout CTS to
the caller token correctly; Web DI lifetimes, event subscribe/unsubscribe pairing
(25+ components), all five poll loops, the auth-expiry stand-down, and JS-side
teardown are uniformly clean; pagination, cost math, week boundaries, the continuity
score blend, and the import walk's optimistic concurrency all check out. The
`.csproj` dependency graph is textbook; `ProposalApplicator`'s size is judged
inherent, not negligent; `TransientFailureClassifier` is the standard the rest of the
error handling should be held to.

---

# Loremaster wiki operations

2026-08-01. Product of reading Karpathy's LLM-wiki pattern
(gist.github.com/karpathy/442a6bf555914893e9891c11519de94f) against the tree. The
pattern: an LLM maintains a persistent synthesis layer between raw sources and
queries — three layers (sources → wiki → schema) and three operations (ingest,
query, lint). The reading's verdict: Nornis already *is* this pattern, with a
stricter trust model than the gist proposes (the review gate, TruthState,
visibility). What remains are four operations the pattern names that the tree
doesn't have. Ordered by value; W3 and W4 are independent of the rest.

**Already built — do not rebuild.** Recorded so the gist doesn't inspire
duplicates: the three layers are Sources → Artifacts → Views verbatim
(domain-model.md); the *lint* operation shipped as Continuity Health — heuristic
`IHealthService`, AI `ContinuityAuditService` (Contradiction, DanglingThread,
StaleStoryline, TimelineConflict, SummaryDrift; grounded evidence, dismissals),
and `ContinuityFixService` drafting fixes as ordinary Pending proposals; the
`log.md` analog is ReviewBatch history + SourceReference + AiUsageRecord. The
gist's "simple markdown indexing works at moderate scale" is the same bet
ai-extraction.md makes deferring vector retrieval — the pattern endorses the
architecture; these items only fill in missing operations.

Ground rules:

- Every new AI call goes through `IAiBudgetGuard` and the shared usage recorder
  (scrub **1.4**) and executor (scrub **1.5**) — no new hand-rolled tracking or
  catch ladders.
- Every synthesized batch gets a named `Kind`. The defect plan already flags the
  `Kind = null` divergence (D4, review-provenance item); nothing here may add to it.
- Visibility law is unchanged: nothing derived from GMOnly/Private material may
  surface at a wider scope (ai-extraction.md's default mapping).
- Migrations additive, as everywhere else in this file.

## W1 — accept-time summary maintenance

The gist's core loop is "ingest updates the relevant wiki pages." Nornis ingests
into facts and relationships, but `Artifact.Summary` — the page — only changes
when a proposal happens to carry one. `AiOperationType.ArtifactSummary` is
declared and referenced nowhere: the MVP op ("generate artifact summaries from
accepted facts") was never built. The audit's SummaryDrift category exists to
*detect* the rot this operation would *prevent*.

- `IArtifactSummaryService`: artifact + accepted facts + relationships in, fresh
  summary out. Uses the dormant `ArtifactSummary` operation type.
- Trigger: after `BatchAcceptAsync` completes, enqueue a refresh for each artifact
  whose facts or relationships changed — Service Bus message, worker-side,
  budget-guarded; never inline in the accept request. Coalesce to one refresh per
  artifact per batch; skip pure-visibility changes.
- **Policy decision, made before implementation and recorded in
  ai-extraction.md:** does the refreshed summary route through review, or is it a
  "trusted system operation" under the core rule's own carve-out? Recommended:
  trusted operation — the summary is derived presentation over already-accepted
  knowledge, not new knowledge; forcing a review round per accepted batch would
  double review traffic to approve restatements. Record provenance either way,
  and let a per-world setting opt back into review if a GM wants the gate.
- Payoff: Ask grounds on current summaries (grounding order already puts
  artifacts first), SummaryDrift findings decay toward zero, and public Ask gets
  cheaper grounding — which stretches the monthly cap.

## W2 — whole-world duplicate sweep

Dedup runs only at ingest, against name-matched context — "Voss" and "Captain
Voss" created three sessions apart survive forever, and no audit category looks
for them. The machinery to *act* on duplicates exists end to end (`MergeArtifact`
change type, `ArtifactMergeService`); only the sweep that feeds it is missing.

- Cheapest first: a sixth audit category (`DuplicateArtifact`) in
  `ContinuityAuditService` — the prompt already reads the whole record; evidence
  is the two artifact refs. Extend `ContinuityFixService`'s allowed changes with
  MergeArtifact so the fix path can draft the merge.
- **Ordering dependency:** lands after the defect plan's merge fix (D2, the live
  duplicate↔target relationship row) — a sweep that triggers more merges before
  that fix multiplies the recurring DanglingThread it causes.
- Phase 2, if candidate quality disappoints: embeddings already exist
  (`AiOperationType.Embedding`, exact-KNN search verified sound) — compute
  name-trigram + embedding-similarity candidate pairs in SQL and have the LLM
  adjudicate only the candidates, instead of asking it to spot pairs unaided.

## W3 — the world digest (the gist's `index.md`)

One maintained world-level synthesis: active storylines and their momentum,
recent movements, open questions. Storyline retrospectives and wrap-ups exist
per-storyline; nothing renders the state of the *world*.

- A generated read-model, **not** an artifact: an artifact's mutations must flow
  through review, and a derived page would pollute the knowledge graph it
  summarizes. In domain-model.md's terms a digest is a View. Persist the last
  digest per world with its generation time; show staleness rather than
  regenerating on every visit.
- Trigger: GM-invoked, plus optionally auto after N accepted batches. Grounding
  mirrors the audit's whole-record read (and shares its prompt-size guards).
- Two renderings from one generation pass: the GM digest (full, GMOnly) and a
  PartyVisible recap with hidden/GM material withheld — the second doubles as the
  session-recap and new-player-onboarding surface, which is the same need the
  demo-world/tutorial work keeps circling.

## W4 — Ask answers filed back

The gist files good query answers back into the wiki; Ask is currently read-only.
When an answer synthesizes something not yet recorded — connects two facts, names
an implication — the synthesis evaporates when the conversation ends.

- A "file this" action on an Ask answer: the answer text becomes a synthesized
  GM-only source routed through ordinary extraction, yielding a reviewable batch.
  The provenance pattern already exists (StorylineRetrospectiveService,
  ContinuityFixService); new batch `Kind`, e.g. `AskFileBack`.
- **Ordering dependency:** waits for D4's shared synthetic-batch writer — built
  before it, this becomes the ninth hand-assembled copy of the provenance
  invariant.
- Visibility follows grounding: an answer grounded on GMOnly material files
  GMOnly. Smallest item in the set; UI is one button and a snackbar.
