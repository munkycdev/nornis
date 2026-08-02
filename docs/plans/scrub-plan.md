# Implementation scrub plan

> Part of the Nornis backlog. This file is a spec, not authorization: execute only
> through the Execution order in `docs/future-features.md`, which holds sequencing,
> completion status, and the Opus/Fable gate.

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
- ~~**DI constructor null-guards**~~ **Done 2026-08-01.** All 26 removed across 13 files.
  Two of them were load-bearing for nullable flow analysis rather than for null-checking —
  `_options = options?.Value ?? throw …` — so dropping the throw left a `?.` the compiler
  then rejected. The `?.` went with it; under DI neither could ever fire.
  - Three tests existed solely to assert those guards threw
    (`AzureOpenAiLoremasterClientTests`' Constructor Validation region). They went with the
    guards: a test whose whole subject is unreachable defensive code outlives its subject
    only by keeping the code alive. Infrastructure suite 305 → 302.
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

- ~~**Enum ledger tests**: delete the fifteen `*_HasNoUnexpectedValues` count twins~~
  **Done 2026-08-01 — fourteen deleted, not fifteen.** `InviteStatus` had no
  `_HasExpectedValues` counterpart, so its count test was that enum's *only* coverage;
  it was converted to the exact-values idiom instead of deleted. Every enum now has
  exactly one test, and it is the one that fails on an addition *or* a removal.
  - Evidence for the redundancy arrived the same day: adding `AiOperationType.WorldNaming`
    required editing two assertions to state one fact, and the count twin caught nothing
    the name list had not already caught. Domain suite 714 → 700.
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
