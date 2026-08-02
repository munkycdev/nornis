# Defect remediation

> Part of the Nornis backlog. This file is a spec, not authorization: execute only
> through the Execution order in `docs/future-features.md`, which holds sequencing,
> completion status, and the Opus/Fable gate.

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

- ~~**Clearing a source's body or URI silently reverts under a success toast.**~~
  **Fixed 2026-08-01.** `ClearBody`/`ClearUri` added through request, command, service and
  client, mirroring `ClearOccurredAt`. A clear now also counts as a body change for the
  reprocess gate — emptying an extracted source invalidates exactly the derived knowledge
  that gate protects.
  - The ordering dependency was the real hazard and was handled first: `NotesEditor.
    GetMarkdownAsync` returned null when JS init had failed, indistinguishable from "the
    user emptied the box". Adding a clear flag on top of that would have turned a JS
    failure into silent data destruction. It now throws, and both callers check
    `Initialized` — `SourceDetail` falls back to the loaded body, so a dead editor reads
    as "unchanged" and can never be read as an instruction to erase.
  - Original diagnosis follows.

- **Clearing a source's body or URI silently reverts under a success toast.**
  `UpdateSourceRequest` is partial-update with `ClearOccurredAt`/`ClearCampaign` but
  no `ClearBody`/`ClearUri`; an emptied editor maps to null = "unchanged"
  (`SourceDetail.razor:1015-1072`). The server keeps the old body, the UI says
  "Source updated.", and an intentional body-clear never triggers reprocess. Fix: add
  the two Clear flags API-side and client-side, mirroring the OccurredAt idiom.
  **Ordering dependency:** fix `NotesEditor`'s init/empty conflation
  (`NotesEditor.razor:119-129` — a failed JS init returns null forever) in the same
  change, or the new flag turns a JS failure into a data-destroying clear.
- ~~**Second update of the same row in one request throws.**~~ **Fixed 2026-08-01** at the
  repository layer rather than per-caller. All 19 whole-entity write sites across 16
  repositories now route through `TrackedUpdateExtensions.SaveAndDetachAsync`, which
  detaches after the save so each call is the self-contained read-modify-write the
  interface already implied. `WorldInviteRepository` detaches inline because it translates
  a concurrency failure on the way out.
  - Fixing it per-caller would have left the same trap armed at seventeen other sites and
    added exactly the horizontal duplication the scrub plan exists to remove.
  - Tested in `Nornis.Infrastructure.Tests` and nowhere else on purpose: this needs a real
    change tracker, and the in-memory fakes track nothing, so a service-level test would
    have passed against the broken code. Verified failing with the detach removed — the
    original "another instance with the same key value is already being tracked".

- **(original diagnosis)** Second update of the same row in one request throws. Repositories read
  `AsNoTracking` and write via `DbSet.Update(entity)`; after SaveChanges the instance
  stays tracked, so attaching a second instance with the same key throws.
  Deterministic: a reveal whose `FactIds` and `Corrections` name the same fact →
  `ApplyUpdateFact` twice → 500 every time (`RevealService.cs:255-284`). Also fails
  the second of two proposals touching one row in a single `BatchAcceptAsync`. Fix:
  repositories reuse the tracked instance (`Find` + `CurrentValues.SetValues`) or
  detach entries after SaveChanges.
- ~~**Merge leaves the duplicate↔target relationship row live, and the continuity audit
  re-flags it forever.**~~ **Fixed 2026-08-01.** The skip branch now deletes the row
  inside the merge transaction, and the comment describing it as "simply left behind,
  orphaned with the archived source" is gone — that sentence documented the bug.
  - Worth recording *how* this survived: a unit test asserted the wrong behaviour
    outright ("A would-be self-referencing relationship must not be mutated at all",
    checking the row was still present and unchanged), while the property test two
    directories away states Requirement 9.5 as "reassign all ArtifactRelationships …
    **removing** any that would become self-referencing". The requirement was right the
    whole time; the unit test had been written to describe the code. A test that
    codifies current behaviour rather than intended behaviour turns a bug into a
    guarded invariant.
  - Verified by removing the fix and confirming the updated test fails, then restoring.
  - This unblocks W2 (duplicate sweep), which would otherwise have generated a fresh
    dangling row per merge.

  - As originally diagnosed: the skip branch discarded an unpersisted mutation (rows are
    no-tracking), leaving the DB row pointing at the archived duplicate. Downstream, the
    target's detail page listed the archived duplicate as a connection (`ArtifactService`
    has no status filter); the continuity audit rendered it as "**Unknown artifact**"
    evidence that survives finding validation — a recurring, score-penalizing
    DanglingThread after every merge of related artifacts — and `SourceReprocessService`
    counted it, blocking cleanup. **Those two downstream readers are untouched and still
    have no status filter**; they are now simply never handed a dangling row by merge.
    Any row left over from a merge *before* this fix is still out there and still
    counted.
- **Dead-lettered extraction wedges the source at Queued forever.** **Not fixed — the
  obvious fix is a trap. Read this before attempting it (assessed 2026-08-01).**
  - `ValidTransitions` has `Queued → { Processing }` only, so neither route out works:
    - **Staleness-gated `Queued → Ready`** needs a clock, and there is none. `Source` has
      no `UpdatedAt`, and `UpdateProcessingStatusAsync` writes the status column without
      stamping a time. So this needs a schema change (a `StatusChangedAt`, additive) plus
      a migration applied to prod before its deploy — not a one-line table edit.
    - **Ungated `Queued → Ready`** would let a GM re-ready a source the worker is
      genuinely mid-way through, producing a second extraction and a second paid AI call
      for one source. That trades a visible wedge for silent double spend, which is worse.
    - **Sweeping batch-less Queued sources** needs a background job and a definition of
      "too long" — the same clock problem, relocated.
  - Less urgent than when it was written: the wedge is now *visible* rather than silent.
    `worker-heartbeat` on `/status` reports work outstanding with no worker, the
    `nornis-sb-deadletter` alert fires when a message dead-letters, and `scripts/dlq.ps1`
    peeks and resubmits it — which un-wedges the source through the normal path.
  - Recommended: `StatusChangedAt` (additive) + staleness-gated `Queued → Ready`, with the
    threshold comfortably past the worker's redelivery window.

- **(original diagnosis)** Dead-lettered extraction wedges the source at Queued forever. After five
  redeliveries (~2 minutes of backoff) the message dead-letters; nothing consumes the
  DLQ, and `ValidTransitions` offers no user-reachable exit from Queued — update,
  delete, mark-ready, and reprocess all reject it. Same wedge from the
  crash-between-commit-and-enqueue window. Fix: allow Queued→Ready retry after a
  staleness threshold, or sweep batch-less Queued sources.
- ~~**Two Active replays per world.**~~ **Fixed 2026-08-01.** Filtered unique index
  `IX_ExtractionReplays_WorldId_Active` (additive migration
  `AddExtractionReplayActiveUniqueIndex`), matching the ImportSessions precedent verbatim.
  - The 409 mapping had to happen in the repository, not the service: `Nornis.Application`
    references no persistence library, so `DbUpdateException` is not a type it can name.
    `CreateAsync` now returns null on the conflict and the service maps that to the same
    `replay_active` error the check-then-create gate returns. The in-memory fake enforces
    the same invariant, so it cannot disagree with production about what is possible.
  - Checked production before shipping: zero worlds currently hold more than one Active
    replay, so the index applies cleanly. Worth doing for any unique index added after the
    fact — the migration fails on existing duplicates and takes the deploy with it.

- **(original diagnosis)** Two Active replays per world. Check-then-create with a non-unique index
  (`ExtractionReplayConfiguration.cs:32`) — while `ImportSessionConfiguration.cs:28-31`
  enforces exactly this invariant correctly two files away. Double-click → two Active
  rows, arbitrary advance/cancel targeting, both requeue sources. Fix: filtered
  unique index `(WorldId) WHERE Status='Active'` (additive), map the violation to the
  existing 409.
- ~~**The stale-response family.**~~ **Fixed 2026-08-01** at all six sites, each with the
  identity captured before the await and re-checked after: `WorldState.
  LoadContinuityCoreAsync` (world), `ArtifactDetail` and `SourceDetail` (`_loadedId` as a
  sequence marker, plus world), `PublicWorldArtifactDetail` (`_loadedKey`), the `Sources`
  poll and initial load (world **and** campaign filter), and `CostsPanel` breakdowns
  (world **and** both range ends).
  - `SourceDetail` carried a comment claiming late responses were "discarded by the
    callers' own checks". There was no such check anywhere. A comment asserting a
    guarantee is worth exactly as much as the code implementing it, and this one had been
    load-bearing in review for however long it had been there.
  - Two sites needed more than the world id. The Sources poll and the Costs breakdowns
    race on *filter* rather than identity — same world, different question — so checking
    the world alone would have looked correct and fixed nothing.
  - Tested at `WorldState`, the one site that is a plain service rather than a component:
    a gated HTTP handler holds the first world's assessment open, the test switches world,
    then releases. Verified failing with the guard removed. The five component sites are
    the same three lines and are not separately covered — bUnit can drive them, but the
    harness cost is real and the shape is now uniform.

- **(original diagnosis)** One shape, several sites: a load captures an
  identity, awaits, then applies the result without re-checking. `WorldState.
  LoadContinuityCoreAsync` (:224-240) lets the previous world's score clobber the
  current one's; `SourceDetail`/`ArtifactDetail`/`PublicWorldArtifactDetail` paint the
  loser of overlapping detail loads (SourceDetail's comment claims discard checks that
  don't exist); the Sources poll and CostsPanel range switches race the same way.
  `NavMenu.RefreshActivityAsync:404-415` and `Home.RunAssessment:450` already
  implement the guard — apply that three-line pattern at each site. Absorbs scrub
  1.10's LibraryDocumentDetail reload item.
- **Extraction can persist proposals that can never be accepted.** **Partly fixed
  2026-08-01 — read the second bullet before assuming this is closed.**
  - Done: one cap. `ProposalValidator.MaxJsonLength` is now public and extraction builds
    against it, so the 50,000/32,768 gap that made a whole size range permanently
    unacceptable is gone. Truncation is gone with it — cutting JSON at a fixed length
    slices mid-token and guarantees a payload that cannot deserialize, so an oversized
    value now throws, rolls the batch back, and fails the source with a reason instead of
    storing something nobody can accept.
  - **Not done:** running `IProposalValidator` over each payload at extraction time.
    That needs the validator injected into `ExtractionService` and pre-flight validation
    ahead of `CreateProposalsAtomicallyAsync` so a schema failure lands in the existing
    parse-retry loop rather than the persist catch. Schema-invalid payloads can still be
    stored Pending today; only the size class is closed.

- **(original diagnosis)** Extraction can persist proposals that can never be accepted. `EnforceVisibility`
  truncates payloads at 50,000 chars (guaranteeing invalid JSON when it fires) while
  the validator caps at 32,768 — and extraction never runs the validator at all. A
  payload between the caps is Pending forever; every accept fails `payload_too_large`.
  Fix: one shared cap constant; run `IProposalValidator` at extraction time, treating
  failures as parse-retryable.
- ~~**Skipping an in-flight import note starts a concurrent extraction.**~~ **Fixed
  2026-08-01.** Skip now refuses while the current item is `Extracting`, returning the same
  `item_not_ready` the non-skip path uses.
  - Only `Extracting` blocks. `Reviewing` and `Failed` stay skippable — those are the GM
    declining to finish something that has already stopped moving, which is what skip is
    for. Blocking those would have broken the feature to fix the bug.
  - Verified by removing the guard and watching the test fail.

- **(original diagnosis)** Skipping an in-flight import note starts a concurrent extraction.
  `ImportSessionService.AdvanceAsync` (:462-468) never checks the current item's
  state before dispatching the next — defeating the serialization this feature exists
  to provide. Fix: refuse skip while Extracting, mirroring `item_not_ready`.
- ~~**Ink autosave re-entrancy creates duplicate Draft sources.**~~ **Fixed 2026-08-01.**
  A `SemaphoreSlim` serialises saves, so the second of two interleaved autosaves finds
  `_sourceId` already set instead of creating a second Draft.
  - Chose a gate over the spec's flag-plus-dirty-bit because two of the three callers must
    not be skipped: `ExitAsync` and `ProcessAsync` need the canvas actually persisted, and
    a flag that returns false while a save is in flight would have read as a save failure
    and aborted processing. Autosave alone drops its callback when the gate is held —
    those strokes ride the next change.
  - Not covered by a test: reproducing it needs two interleaved JS-invoked callbacks
    against a real circuit, which bUnit does not give cheaply. The guard is three lines and
    the failure mode is now structurally impossible rather than timing-dependent.

- **(original diagnosis)** Ink autosave re-entrancy creates duplicate Draft sources. `SaveInkAsync` sets no
  flag; two debounced callbacks interleave during a slow first save while `_sourceId`
  is still null → two Drafts, one orphaned (`InkCapture.razor:113-195`). Fix: a
  `_saving` flag with a trailing-dirty bit.
- **Extraction never validates `source.WorldId == worldId`.** **Assertion added
  2026-08-01; the parameter-order half is not done.**
  - Done: `ProcessExtractionAsync` refuses an inconsistent pair before any paid call,
    returning non-transient — redelivering the same mismatch reproduces it, so retrying is
    pointless. Verified nothing is billed to either world.
  - **Not done:** standardising worldId-first parameter order across sibling interfaces.
    Both orders still exist, which is what made a mis-enqueued pair plausible in the first
    place. That is a mechanical sweep and belongs with the scrub plan's convention tier,
    not bolted onto a defect fix.

- **(original diagnosis)** Extraction never validates `source.WorldId == worldId`. A mis-enqueued pair
  extracts normally but checks and bills the *wrong world's* budget silently. Fix:
  assert world consistency at pipeline entry (and standardize worldId-first parameter
  order — both orders currently exist across sibling interfaces).
- ~~**A dead queue processor looks healthy forever.**~~ **Fixed 2026-08-01.** Both queue
  workers now start through `ProcessorStartup.StartWithRetryAsync` — exponential backoff
  capped at two minutes, retrying until it starts or shutdown cancels.
  - Retrying forever is deliberate: there is no useful "give up" for a queue processor. If
    the namespace returns in ten minutes the right behaviour is to start consuming.
  - The start delegate is passed in rather than the processor, because
    `ServiceBusExtractionProcessor` is sealed with non-virtual methods and could not be
    faked. That also made the retry itself testable without any Service Bus at all.
  - The second half of the spec item — "surface processor liveness" — is covered by the
    worker heartbeat from the System status plan, which is already live.

- **(original diagnosis)** A dead queue processor looks healthy forever. `StartProcessingAsync` is called
  once with no retry (`ExtractionWorker.cs:38`), exceptions are ignored by design, and
  the worker exposes no health surface — a Service Bus blip at boot silently halts
  extraction until the next deploy. Fix: retry with backoff; surface processor
  liveness or fail fast.
- **Wrap-up reports total failure after partial success.** **Duplicate-minting fixed
  2026-08-01; the reporting half is not.**
  - Done: closures are now idempotent. A closure whose storyline already holds the
    requested status is skipped, and a call where every closure was already applied writes
    no synthetic source and reports `Closed: 0, BatchId: null`. That kills the actual harm
    — a retry after a later step failed used to mint a second wrap-up batch closing an
    already-closed storyline.
  - **Not done:** returning per-step results. A later step's failure still reports the
    whole call as failed, so the GM is told nothing happened when the closures did. Fixing
    that properly means changing what the endpoint returns and teaching the UI to read a
    partial result — today it treats anything non-success as "nothing applied". Worth
    doing with the `BatchOperationResult` shape, not as a tail-end.
  - Narrower in practice than it reads: `SessionWrapUpCard` submits one decision per call,
    so the multi-step path is reachable through the API but not through the UI.

- **(original diagnosis)** Wrap-up reports total failure after partial success. Closures commit in their
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
- ~~**LoremasterService's belt-over-suspenders catch hides bugs and mangles
  cancellation**~~ **Fixed 2026-08-01.** Logger injected; a cancelled request rethrows
  instead of becoming a 500 nobody caused; everything else is logged with the world id
  rather than collapsing into one untraceable message. The catch stays broad on purpose —
  context assembly touches several stores and a partial failure should not lose the
  question — but it is no longer silent. `IsRateLimitByTypeName` remains scrub 1.5's.
- **(original)** LoremasterService's belt-over-suspenders catch hides bugs and mangles
  cancellation (:174-178, no logger; a user-cancelled request becomes a 500). Fix:
  narrow it, rethrow when `ct.IsCancellationRequested`, log the rest. Its type-name
  exception sniffing (`IsRateLimitByTypeName`) is scrub **1.5** — confirmed against
  the codebase's own documented string-matching incident.
- ~~**`ReferencePassageRetriever` catch-all swallows cancellation**~~ **Fixed
  2026-08-01.** An OCE filter-rethrow sits above the catch-all, so shutdown no longer
  reads as "this world has no reference passages" while extraction carries on against a
  cancelled token.
- **(original)** `ReferencePassageRetriever` catch-all swallows cancellation (:92-97) — shutdown
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
- ~~**Demo-world name generation is the only unmetered AI call**~~ **Fixed 2026-08-01.**
  New `AiOperationType.WorldNaming`; the call now writes a usage record on success *and*
  on failure. The budget guard stays off by design — naming must never fail or block demo
  creation — but "not guarded" was never a reason to be invisible on the cost page.
  - The failure row matters more than the success one here: this call was failing silently
    in production from 2026-07-26 (the `max_tokens` 400) and nobody noticed, because the
    catch turns any failure into the static-name fallback. A zero-token failed row is what
    would have made that visible.
  - Recording is wrapped in its own try: metering must never be the thing that breaks
    world creation.
  - No test — `ChatClient` is a concrete SDK type with no seam here, the same reason the
    sibling AI clients are covered only where they parse.
- **(original)** Demo-world name generation is the only unmetered AI call (no guard, no usage
  record — bounded only by the demo rate limit). Fix: write the usage record even if
  the guard stays off by design.
- ~~**A failed heuristic read silently becomes continuity score 0**~~ **Fixed 2026-08-01**
  at both sites: the audit run and the read path now return the underlying failure instead
  of substituting 0. A fabricated zero is indistinguishable from a record in ruins, and on
  the audit path it also meant spending a paid AI call blending against a fiction.
  - Not covered by a test: the fixture wires a real `HealthService`, so forcing the failure
    means breaking a repository underneath it. The change is two guard clauses.
  - `ContinuityAuditService` still has no logger, so these failures return but are not
    logged. Injecting one cascades through its construction sites — worth doing with the
    ReviewService logger item below, which needs the same treatment.
- **(original)** A failed heuristic read silently becomes continuity score 0
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
