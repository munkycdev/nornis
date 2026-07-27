# Nornis performance/cost audit — Application layer

Scope: `src/Nornis.Application/{Services,Application,Authorization,Knowledge,Validation,Models,Errors,Notifications}`.
`src/Nornis.Application/Ai/` excluded (other auditor).

Every finding below was confirmed by reading the source. Line numbers are from the working
tree at the time of the audit.

## Structural facts that constrain the fixes

- **All repositories share one scoped `NornisDbContext`** (`Nornis.Api/Program.cs:78-93`,
  everything `AddScoped`). `Task.WhenAll` over two repository calls will throw in EF Core's
  concurrency detector — `CostService.cs:44-46` already documents this. **So the fix for
  "sequential awaits" in this codebase is always query consolidation or reuse of an
  already-loaded object, never parallelism.** I have not recommended `Task.WhenAll` anywhere.
- **Every repository read is `AsNoTracking`** (all 27 repository files). There is no
  first-level cache: a second `GetByIdAsync` for the same row is a second SQL round trip to
  Azure SQL, not a free identity-map hit. This is what makes the duplicate-call findings real.
- `JsonSerializerOptions` are correctly `static readonly` everywhere (7 sites). No per-call
  `new Regex(...)`. No `async void`, no `.Result`/`.Wait()`/`GetAwaiter().GetResult()` in the
  audited folders. Those anti-pattern classes are clean.

---

### [SEVERITY: High] `WorldMemberActionFilter` loads the membership, then the service loads it again

- **Where:**
  - Filter: `src/Nornis.Api/Filters/WorldMemberActionFilter.cs:35` — `GetByWorldAndUserAsync(worldId, user.Id)`
  - Re-load 1: `src/Nornis.Application/Services/WorldService.cs:67` (`GetByIdAsync`)
  - Re-load 2: `src/Nornis.Application/Services/WorldService.cs:86` (`UpdateAsync`)
  - Re-load 3: `src/Nornis.Application/Services/WorldExportService.cs:47`
  - Re-load 4: `src/Nornis.Application/Services/WorldDeletionService.cs:32`
  - Re-load 5-7: `src/Nornis.Application/Services/CharacterService.cs:38, 179, 232`
- **What:** `[ServiceFilter(typeof(WorldMemberActionFilter))]` runs on 20+ controllers and puts
  the `WorldMember` in `HttpContext.Items`. Controllers read it (`HttpContext.GetWorldMember()`)
  and pass only `user.Id` + `member.Role` into the service — which then queries the *same row*
  again to re-derive the role it was just handed.
  Traced end to end: `GET /api/worlds/{worldId}` → `WorldsController.cs:107` filter fires →
  `WorldsController.cs:111` reads the member from Items → `WorldsController.cs:113`
  `_worldService.GetByIdAsync(worldId, user.Id, ct)` → `WorldService.cs:67` queries
  `WorldMembers` a second time for the identical `(worldId, userId)` pair.
  Same shape for `PUT /api/worlds/{worldId}` (`WorldsController.cs:136` already gates on
  `member.Role != GM`, then `WorldService.cs:86-91` re-queries and re-checks the same thing),
  for world export/delete, and for every `CharactersController` write.
- **Why it costs:** one extra indexed SELECT + round trip on every world-scoped request that
  hits these services. On the world-detail page load (world + artifacts + graph + members +
  health) this is several redundant queries per page, each carrying full Azure SQL latency.
  It is also duplicated *authorization* logic: two places decide the same thing from the same
  row, and they can drift.
- **Fix:** pass the already-resolved role/member id into the command instead of re-querying.
  `UpdateWorldCommand` already carries `ActingUserId`; add `ActingUserRole` (as
  `RenameArtifactCommand`, `SetArtifactStatusCommand` etc. already do — the pattern exists in
  this codebase) and delete the `GetByWorldAndUserAsync` call:

  ```csharp
  // WorldService.UpdateAsync
  - var member = await _worldMemberRepository.GetByWorldAndUserAsync(command.WorldId, command.ActingUserId, ct);
  - if (member is null || !member.Role.IsAtLeast(WorldRole.GM))
  + if (!command.ActingUserRole.IsAtLeast(WorldRole.GM))
        return AppResult<World>.Fail(new AppError(403, "insufficient_role", ...));
  ```

  For `WorldService.GetByIdAsync` the membership check is entirely redundant — the filter
  already 403s non-members before the action runs.
- **Effort:** small (mechanical; touches ~6 services and their command records).
- **Risk:** the filter applies `ApplyViewAs` (the GM "view as player" downgrade,
  `HttpContextExtensions.cs:38-56`) but the services' own lookup does **not**. Moving to the
  filter's role therefore makes view-as-player apply to these endpoints too — which
  `HttpContextExtensions.cs:33-37` says is the intended semantics, but it is a behavior change
  worth stating. Any service called from a non-HTTP host (worker) must keep supplying a role.

---

### [SEVERITY: High] Batch accept re-reads every proposal, batch and source 3-4× over

- **Where:** `src/Nornis.Application/Services/ReviewService.cs:561` (`BatchAcceptAsync`),
  `:606-619` (`OrderCreatesFirstAsync`), `:634-642` (`TryAcceptOneAsync`), `:836` (`UpdateBatchLifecycleAsync`)
- **What:** for a 50-proposal batch accept (the cap is 50, `:552`):
  1. `OrderCreatesFirstAsync` loops `proposalIds` and calls
     `_reviewProposalRepository.GetByIdAsync(id)` — **50 queries**, only to read `ChangeType`.
  2. `TryAcceptOneAsync` then calls `GetByIdAsync(proposalId)` for the **same 50 rows again**
     (`:634`), plus `_reviewBatchRepository.GetByIdAsync(proposal.ReviewBatchId)` (`:638`) and
     `_sourceRepository.GetByIdAsync(batch.SourceId)` (`:642`) **per proposal** — and in a batch
     accept those 50 proposals almost always share one batch and one source, so 98 of those
     100 reads return a row already in hand.
  3. `UpdateBatchLifecycleAsync` (`:836`) loads the batch **once more** per affected batch.

  Single accept has the same shape at smaller scale: `AcceptProposalCoreAsync` loads the batch
  at `:254`, then `UpdateBatchLifecycleAsync(batch.Id)` at `:329` loads that identical batch
  again at `:836`. And `AcceptWithPrerequisitesAsync` (`:342-372`) recurses back into
  `AcceptProposalCoreAsync` once per prerequisite plus a final retry, re-loading
  proposal+batch+source each time.
- **Why it costs:** ~200 SQL round trips for one "accept all" click on a normal extraction
  batch, where ~55 would do. Review is the core loop of the product — a GM does this after
  every session. At Azure SQL latency this is the difference between a snappy accept and a
  multi-second one.
- **Fix:** load once, pass down.
  - Add `IReviewProposalRepository.ListByIdsAsync(ids)` (mirroring the `ListByIdsAsync` that
    already exists on `IArtifactRepository`, `IArtifactFactRepository`,
    `IArtifactRelationshipRepository`) and have `OrderCreatesFirstAsync` return the *loaded*
    proposals, not just ids.
  - Change `TryAcceptOneAsync` to take the already-loaded `ReviewProposal`, and memoize
    batch/source in a `Dictionary<Guid, ReviewBatch>` / `Dictionary<Guid, Source>` local to
    `BatchAcceptAsync`.
  - In `AcceptProposalCoreAsync`, pass the loaded `batch` into `UpdateBatchLifecycleAsync`
    instead of its id.
  `BatchRejectAsync` (`:720-786`) has the identical per-proposal batch+source reload and takes
  the same fix.
- **Effort:** medium.
- **Risk:** `TryAcceptOneAsync`'s doc comment (`:621-626`) says it deliberately *re-reads* the
  proposal so the retry pass sees the updated `Status` and takes the idempotent path. If you
  cache proposals across the retry loop you must refresh status, or keep the re-read only for
  the retry pass. Batch/source are immutable within the operation and are safe to cache.

---

### [SEVERITY: High] `ProposalApplicator` re-loads the batch's `Source` on every apply

- **Where:** `src/Nornis.Application/Application/ProposalApplicator.cs:84, 321, 381, 491, 538, 592, 690`
  (`_sourceRepository.GetByIdAsync(batch.SourceId, ct)` — seven call sites, one per change type)
- **What:** `ApplyAsync` receives `(proposal, batch, filter)` and each arm re-fetches
  `batch.SourceId` to read `source.Visibility` and `source.CreatedByUserId`. The caller already
  has that exact `Source`:
  - `ReviewService.cs:258` loads it, then calls `ApplyAsync` at `:298` without passing it.
  - `ReviewService.cs:642` (batch path) does the same, once per proposal.
  - `RevealService.cs:242` *creates* the Source in memory, then calls `ApplyAsync` (`:406`) once
    per revealed item (`RevealService.cs:245-283`, four loops) — so a 20-item reveal issues 20
    redundant SELECTs for a row it constructed moments earlier in the same transaction.
- **Why it costs:** one wasted round trip per applied proposal. A 50-proposal batch accept: 50.
  A 20-item reveal: 20. Inside an open transaction, so it also holds the transaction longer.
- **Fix:** add `Source source` to the `IProposalApplicator.ApplyAsync` signature and delete the
  seven `GetByIdAsync` calls (keep the null guard as an argument check). `IProposalApplicator.cs`
  is 34 lines; both callers already hold the source.
- **Effort:** small.
- **Risk:** low. The only semantic difference is that a source deleted between the caller's read
  and the apply would no longer be detected — but the whole apply runs inside a transaction the
  caller opened, so that window does not exist in practice.

---

### [SEVERITY: High] Artifact detail: one query per cited source, plus one per connected artifact

- **Where:** `src/Nornis.Application/Services/ArtifactService.cs:190-200` and `:610-635`
  (`ResolveConnectedArtifactsAsync`)
- **What:** `GetDetailAsync` is the artifact page. Two N+1 loops:
  ```csharp
  foreach (var sourceId in allReferences.Select(r => r.SourceId).Distinct())   // :190
  {
      var source = await _sourceRepository.GetByIdAsync(sourceId, ct);          // :192
      ...
  }
  ```
  and
  ```csharp
  foreach (var otherId in otherIds)                                            // :623
  {
      var other = await _artifactRepository.GetByIdAsync(otherId, ct);          // :625
  }
  ```
  A well-developed artifact cited by 15 sessions with 12 relationships costs 27 extra round
  trips on top of the 5 the method already makes.
- **Why it costs:** this is the single most-visited read path (`GET .../artifacts/{id}`,
  `ArtifactsController.cs:283`) **and** it is served anonymously on the public world page
  (`PublicController.cs:136-147`, rate-limited but uncached). Latency scales linearly with how
  interesting the artifact is — the pages users care about most are the slowest.
- **Fix:** both have a ready-made batch API.
  - Connected artifacts: `IArtifactRepository.ListByIdsAsync(ids)` already exists — replace the
    loop with one call and filter in memory (the loop's world/visibility checks are all
    in-memory predicates already).
  - Sources: add `ISourceRepository.ListByIdsAsync(ids)` (the interface has no by-ids method
    today), or reuse `ListByWorldAsync(worldId)` — one query either way.
- **Effort:** small.
- **Risk:** none behavioural; the filtering predicates are unchanged. Adding a repository method
  needs a matching EF implementation + tests.

---

### [SEVERITY: High] `SourceKnowledgeService`: a query per provenance row, plus O(n²) dedup scans

- **Where:** `src/Nornis.Application/Services/SourceKnowledgeService.cs:65-117`
- **What:** the "what this session contributed" panel walks every `SourceReference` for the
  source and issues a point lookup per row: `_artifactFactRepository.GetByIdAsync` (`:83`),
  `_artifactRelationshipRepository.GetByIdAsync` (`:100`), and `ArtifactAsync` (`:59`, cached by
  id but still one query per distinct artifact). Within the same loop, dedup is a linear scan of
  the accumulating list: `artifacts.All(a => ...)` (`:74`), `facts.Any(f => ...)` (`:85`),
  `relationships.Any(r => ...)` (`:103`) — O(n²) in the number of accepted items.
- **Why it costs:** an extraction batch for a full session note routinely produces 50-200
  accepted items. That is 50-200 SQL round trips for one panel, plus ~20k list comparisons.
  Also served anonymously (`PublicController.cs:221-253`).
- **Fix:** batch by target type before the loop, then join in memory:
  ```csharp
  var byType = references.ToLookup(r => r.TargetType);
  var facts = (await _artifactFactRepository.ListByIdsAsync(
      byType[SourceReferenceTargetType.ArtifactFact].Select(r => r.TargetId).Distinct().ToList(), ct))
      .ToDictionary(f => f.Id);
  // same for relationships (ListByIdsAsync) and artifacts (ListByIdsAsync)
  ```
  `ListByIdsAsync` already exists on all three repositories (used by
  `ContinuityAuditService.cs:483-495`). Replace the `All`/`Any` dedup with `HashSet<Guid>` adds.
- **Effort:** medium (the loop body has real visibility logic that must be preserved verbatim).
- **Risk:** the current code preserves *first-reference-wins* for the `Quote` shown. Any rewrite
  must keep reference order when picking the quote, or the panel's excerpts change.

---

### [SEVERITY: High] Continuity audit loads the entire world graph twice per run

- **Where:** `src/Nornis.Application/Services/ContinuityAuditService.cs:131` vs `:136-161`;
  the first call lands in `src/Nornis.Application/Services/HealthService.cs:32-58`
- **What:** `RunAssessmentAsync` calls `_healthService.GetHealthAsync(worldId)` for the
  heuristic base score. `HealthService` loads *all* artifacts (`HealthService.cs:32`), *all*
  facts with `int.MaxValue` (`:45`), *all* relationships (`:48`), and *all* source references
  for every artifact/fact/relationship id (`:58`). `RunAssessmentAsync` then immediately loads
  the same four collections again at `ContinuityAuditService.cs:136, 145, 150, 159`.
  `GetLatestAsync` (`:237`) also calls `GetHealthAsync` — so the plain
  `GET /health/assessment` page load pulls the whole world graph too.
- **Why it costs:** four large table scans duplicated on the most data-heavy operation in the
  app, on a request the user is already waiting 10-30s for. On a mature world the fact and
  source-reference reads are the biggest queries the system issues.
- **Fix:** two steps, either alone is worth it.
  1. Split `HealthService` into a pure scorer over already-loaded collections plus a thin
     loader, and have `ContinuityAuditService` load once and call the scorer:
     ```csharp
     var artifacts = ...; var facts = ...; var relationships = ...; var refs = ...;
     var heuristic = HealthScorer.Score(artifacts, facts, relationships, refs).OverallScore;
     ```
     (the audit's filters are a *subset* of health's — audit drops Archived and `False`, health
     drops only `False` — so health must score the unfiltered set; keep both lists.)
  2. Memoize the heuristic score. It is a pure function of world state and the audit already
     persists `HeuristicScore` on the assessment (`ContinuityAssessment.HeuristicScore`) —
     `GetLatestAsync` could read the stored value instead of recomputing the whole graph, or
     cache it per world with a short TTL.
- **Effort:** medium.
- **Risk:** the two paths apply *different* filters (see above). Getting that wrong silently
  changes the published health score. Worth a `code-critic` pass.

---

### [SEVERITY: High] `WorldService.ListForUserAsync` — a membership query per world

- **Where:** `src/Nornis.Application/Services/WorldService.cs:201-217`
- **What:**
  ```csharp
  var worlds = await _worldRepository.ListByUserAsync(userId, ct);
  foreach (var world in worlds)
      var member = await _worldMemberRepository.GetByWorldAndUserAsync(world.Id, userId, ct);  // :209
  ```
  `ListByUserAsync` already filters to worlds this user is a member of (`WorldRepository.cs:48-51`),
  so the per-world lookup can never return null — it exists purely to fetch the role.
- **Why it costs:** `GET /api/worlds` is the app shell / world switcher; it runs on essentially
  every page load. A GM in 8 worlds pays 9 queries where 2 would do.
- **Fix:** `IWorldMemberRepository.ListByUserAsync(userId)` **already exists** (used by
  `CostService.cs:74`). One extra query, then a dictionary join:
  ```csharp
  var roles = (await _worldMemberRepository.ListByUserAsync(userId, ct))
      .ToDictionary(m => m.WorldId, m => m.Role);
  var result = worlds.Where(w => roles.ContainsKey(w.Id))
      .Select(w => new WorldWithRoleDto(w, roles[w.Id])).ToList();
  ```
- **Effort:** trivial.
- **Risk:** none — same filter semantics (a world with no membership row still drops out).

---

### [SEVERITY: Medium] Journey map auto-pick builds a full map view for every candidate and throws all but one away

- **Where:** `src/Nornis.Application/Services/JourneyMapService.cs:56-79`, calling
  `src/Nornis.Application/Services/MapViewService.cs:37-80`
- **What:** when no `mapSourceId` is supplied — the default for members and the **only** path on
  the public site (`PublicController.cs:175` passes `null`) — the service loops every `Map`
  source in the world and calls `_mapViewService.GetMapAsync(candidate.Id, ...)` in full. Each
  call costs: `_sourceRepository.GetByIdAsync` (`MapViewService.cs:40`) — *for a `Source` that
  `JourneyMapService.cs:41` already has in `allSources`* —, `ListBySourceAsync` (`:46`),
  `GenerateDownloadSasUrlAsync` (`:53`), `ListByAttachmentAsync` (`:56`), `ListByIdsAsync`
  (`:59`). Only the winner's view is used; everything else, including every SAS URL, is discarded.
- **Why it costs:** 4 DB round trips + one SAS signing per candidate map. A world with 10 maps
  pays 40 queries to render one journey. The SAS generation is local (no network —
  `AzureBlobStorageService.cs:55-60`), so the DB traffic is the real cost.
- **Fix:** pick the canvas on cheap data first, build the view once.
  Add a lightweight `MapViewService` path (or query placemark counts per attachment directly)
  that returns `(attachment, placemarkCount)` per candidate, run `IsRicher` over that, and call
  the full `GetMapAsync` exactly once for the winner. Separately, add an overload of
  `GetMapAsync` that accepts an already-loaded `Source` so the `GetByIdAsync` at
  `MapViewService.cs:40` is skipped when the caller has it.
- **Effort:** medium.
- **Risk:** `IsRicher` (`JourneyMapService.cs:194-207`) ties-breaks on **visible** pin count, so
  the cheap pre-pass must apply the same visibility filter or the auto-picked map can change for
  a given caller. Getting this subtly wrong changes which map the public page shows.

---

### [SEVERITY: Medium] Public "Ask the Loremaster" loads the same World row three times

- **Where:**
  - `src/Nornis.Api/Controllers/PublicController.cs:80` → `:282` `GetBySlugAsync`
  - `src/Nornis.Application/Services/AiBudgetGuard.cs:53` (`GetPublicAskStatusAsync` → `GetByIdAsync`)
  - `src/Nornis.Application/Services/AiBudgetGuard.cs:27` (`GetStatusAsync` → `GetByIdAsync`), reached from
    `src/Nornis.Application/Services/LoremasterService.cs:126` (`_budgetGuard.CheckAsync`)
- **What:** `POST /api/public/worlds/{slug}/ask` resolves the world by slug, then
  `CheckPublicAskAsync` loads it by id for the monthly cap, then `LoremasterService.AskAsync`
  independently loads it by id again for the *daily* cap. Three SELECTs for one row, all
  `AsNoTracking`. The member-facing `POST .../ask` (`LoremasterController.cs:31`) pays two
  (filter membership + `AiBudgetGuard`).
- **Why it costs:** small per-request (two extra indexed lookups) but this is the one anonymous
  write-ish endpoint exposed to the internet and the one gated by a *money* cap. Every wasted
  query is on the path an attacker can drive with rate-limited-but-free requests.
- **Fix:** give `IAiBudgetGuard` overloads taking an already-loaded `World` (or a
  `decimal? budget`), and pass the world through from `PublicController.ResolveAsync`. The
  budget guard's only use of the world is `world?.DailyAiBudgetUsd` / `world?.PublicAskMonthlyBudgetUsd`.
- **Effort:** small.
- **Risk:** low. Note the daily-budget gate inside `AskAsync` also applies to public asks,
  which is deliberate (defence in depth) — keep both checks, just stop re-reading the row.

---

### [SEVERITY: Medium] `SourceLocationService.BuildLocationsAsync` — one query per referenced artifact

- **Where:** `src/Nornis.Application/Services/SourceLocationService.cs:142-154`
- **What:** loops the source's distinct artifact references and calls
  `_artifactRepository.GetByIdAsync(artifactId)` per id, only to discard everything that is not
  a visible non-archived `Location`.
- **Why it costs:** a session note typically references 20-60 artifacts and links a handful of
  locations; that is ~50 round trips to produce ~4 rows. Runs on the source detail page and on
  the public source page (`PublicController.cs:256-273`).
- **Fix:** `IArtifactRepository.ListByIdsAsync(ids)` — one query, then filter in memory with the
  identical predicate. Or `ListByTypeAsync(worldId, ArtifactType.Location, filter)` and
  intersect with the reference ids, which also pushes the type filter into SQL.
- **Effort:** trivial.
- **Risk:** none — the predicate is unchanged.

---

### [SEVERITY: Medium] `ArtifactRemovalService.PreviewAsync` — one query per relationship

- **Where:** `src/Nornis.Application/Services/ArtifactRemovalService.cs:63-69`
- **What:** builds the "what will be deleted" strings by loading each relationship's counterpart
  artifact one at a time (`_artifactRepository.GetByIdAsync(otherId)` at `:67`).
- **Why it costs:** a hub artifact with 30 relationships costs 30 round trips for a confirmation
  dialog. Modest volume, but the fix is a two-line change.
- **Fix:** collect `otherId`s, one `ListByIdsAsync`, dictionary lookup in the loop.
- **Effort:** trivial.
- **Risk:** none.

---

### [SEVERITY: Medium] `ApplyAddRelationship` queries the same artifact's relationships twice

- **Where:** `src/Nornis.Application/Application/ProposalApplicator.cs:605` and `:635`
- **What:** the `PartOf` branch loads `ListByArtifactAsync(artifactA.Id)` at `:605`. If the
  storyline has no existing parent link, the branch falls through (`:629`) and `:635` issues the
  **identical query again** for the duplicate-edge check.
- **Why it costs:** one duplicate round trip on every accepted `PartOf` relationship — which is
  every storyline-hierarchy edge the extractor proposes, and extraction proposes a lot of them.
- **Fix:** hoist the call above the `PartOf` branch and reuse the list:
  ```csharp
  var existingForA = await _artifactRelationshipRepository.ListByArtifactAsync(artifactA.Id, ct);
  // PartOf branch filters existingForA; the duplicate check below also filters existingForA
  ```
- **Effort:** trivial.
- **Risk:** none — the `PartOf` branch only ever deletes/updates rows it returns from, and if it
  returns, `:635` is never reached.

---

### [SEVERITY: Medium] Cost summary: four sequential full-table aggregates, one of them unbounded

- **Where:** `src/Nornis.Application/Services/CostService.cs:47-50`
- **What:** `GetSummaryAsync` issues four separate `AggregateAsync` calls (today / this week /
  this month / **all time**) against `AiUsageRecords`. The comment at `:44-46` correctly
  explains they cannot be parallelised (shared scoped DbContext) — but they can be *merged*.
  The all-time aggregate has no date predicate at all, so it scans the world's entire usage
  ledger every time the costs page opens.
- **Why it costs:** four round trips plus an unbounded scan that grows forever. The ledger is
  append-only and the only table in the system with no natural retention bound.
- **Fix:** one query with conditional aggregation —
  `SUM(CASE WHEN CreatedAt >= @today THEN Cost ELSE 0 END)` etc. — exposed as a new
  `AggregateTimePeriodsAsync(worldId, userId, today, weekStart, monthStart)`. Separately, the
  all-time figure is a good candidate for a rolled-up counter or a short-TTL cache; it changes
  only when a usage record is written.
- **Effort:** medium (new repository method + SQL).
- **Risk:** the four ranges come from `TimePeriodCalculator`; merging them into one query must
  preserve the exact boundary semantics (inclusive/exclusive) the existing `AggregateAsync`
  applies, or reported spend shifts.

---

### [SEVERITY: Medium] `HealthService` and `CanonService` pull the whole world graph with no bound and no cache

- **Where:** `src/Nornis.Application/Services/HealthService.cs:32-58`;
  `src/Nornis.Application/Services/CanonService.cs:33-46`
- **What:** both load every artifact in the world, then every fact via
  `ListByArtifactIdsAsync(..., int.MaxValue, ...)`, then every relationship, then (health) every
  source reference — all into memory, to produce four integers (health) or a flat list ordered by
  `UpdatedAt` (canon). Neither paginates; neither caches.
- **Why it costs:** memory and query time scale linearly with world size, on endpoints a user
  hits repeatedly. `HealthService`'s output is four percentages that change only when canon
  changes; `CanonService` returns an unbounded list straight to the client.
- **Fix:**
  - Health: the four scores are `COUNT`/`SUM` reductions — push them into aggregate queries
    (`Percent(developed, artifacts.Count)` etc. need counts, not entities). Failing that, cache
    per world keyed on "latest accepted proposal timestamp", which the audit eligibility check
    already tracks (`ContinuityAuditEligibility.IsEligible` takes `latestAcceptanceAt`).
  - Canon: add paging/limit to `GetCanonAsync` and the `CanonController` route.
- **Effort:** medium (health), small (canon paging).
- **Risk:** health scoring is user-visible; an aggregate rewrite must reproduce the exact
  filters (`TruthState != False`, summary non-empty, 30-day recency window). Canon paging is an
  API contract change for the client.

---

### [SEVERITY: Low] Artifact search scores the entire world in memory, re-parsing the term per artifact

- **Where:** `src/Nornis.Application/Services/ArtifactService.cs:91-105`;
  `src/Nornis.Application/Services/ArtifactRelevance.cs:28-92`
- **What:** `SearchAsync` loads every artifact in the world (`ListByWorldAsync(worldId, null, null)`)
  and calls `ArtifactRelevance.Score(a.Name, a.Summary, term)` on each. `Score` re-does
  `term.Trim()` (`:30`) and, for multi-word queries, `Tokenize(needle)` (`:60`) — a `string.Split`
  allocating a fresh array plus substrings — **once per artifact**, for a term that is constant
  across the whole call.
- **Why it costs:** this backs the always-present search bar, so it fires on keystrokes. The
  allocations are per-artifact garbage; more importantly the whole artifact table crosses the
  wire on every keystroke to return at most 50 rows.
- **Fix:** cheap half — hoist the term normalisation out of the loop by adding a `Score` overload
  that takes a pre-tokenised term (`ReadOnlySpan<char> needle, string[] tokens`), computed once
  in `SearchAsync`. Real fix — push a `LIKE`/full-text prefilter into the repository so only
  candidate rows are materialised, then rank the survivors in memory.
- **Effort:** trivial (hoisting) / large (server-side prefilter).
- **Risk:** the ranking tiers are deliberately exclusive and explainable (`ArtifactRelevance`
  doc comment); a SQL prefilter must not drop rows the in-memory scorer would have matched —
  particularly the `NameAllTokens` / `SummaryAllTokens` tiers, which no single `LIKE` covers.

---

### [SEVERITY: Low] `StorylineDevelopmentReader.Add` dedups with a linear scan inside the reference walk

- **Where:** `src/Nornis.Application/Services/StorylineDevelopmentReader.cs:112-128` (`Add`), called from the loop at `:130-164`
- **What:** `if (!list.Any(d => d.Kind == development.Kind && d.Text == development.Text))` scans
  the whole accumulated list per insert — O(k²) per `(storyline, session)` bucket, with a string
  comparison per element.
- **Why it costs:** small in practice (developments per storyline per session are usually single
  digits) but it sits inside the timeline read, which is served on the public page
  (`PublicController.cs:149-160`) and already the heaviest assembly in `ArtifactService`.
- **Fix:** keep a parallel `HashSet<(string Kind, string Text)>` per bucket, or dedup once at the
  end with `DistinctBy`.
- **Effort:** trivial.
- **Risk:** none — insertion order is preserved either way.

---

## Unverified / worth a look

- **`ReviewService.BuildProposalContextAsync`** (`ReviewService.cs:111-130`) loads **every fact
  of every artifact in the world** with `int.MaxValue` the moment a single `UpdateFact` proposal
  is present in the queue, and likewise every relationship for a single `UpdateRelationship`.
  Only the targeted rows are actually used (`ResolveTargetName`, `:182`, `:204`). This looks like
  it should be `ListByIdsAsync(proposals.Select(p => p.TargetId))` — but I could not confirm the
  cost without knowing typical fact counts per world, and the comment at `:113-116` shows the
  unrestricted filter is a deliberate (and load-bearing) security decision. Flagging rather than
  asserting.
- **`ExtractionService.cs:1007`** (`foreach (var link in partOfLinks)` with an awaited
  `GetByIdAsync` inside) matches the N+1 shape of the confirmed findings, but it is on the
  worker's extraction path, which I did not trace end to end. Worth a look with the extraction
  path in view.
- **`SourceReprocessService.cs:311`** (`foreach (var artifactId in createdArtifactIds)`) — same
  shape, same caveat.
- **`LibraryService.GetAllowedScopes`** (`LibraryService.cs:65-68`) returns a freshly allocated
  collection expression on every call and is used with `.Contains` (`:192`). Trivial garbage;
  only worth changing if the library list turns out to be hot, which I could not establish.

---

## Summary

| Severity | Count |
|----------|-------|
| High | 6 |
| Medium | 6 |
| Low | 2 |

The dominant theme is **the same row read twice (or fifty times) in one request** — the
authorization filter's membership, the review batch's source, the artifact's neighbours. Because
every repository read is `AsNoTracking` there is no identity map to absorb it, so each duplicate
is a real Azure SQL round trip. The best payoff-per-hour is the cluster of trivial/small fixes:
`WorldService.ListForUserAsync`, `SourceLocationService`, `ArtifactRemovalService`, the
`ApplyAddRelationship` double query, and threading the already-loaded `Source` into
`ProposalApplicator`.

> These findings were produced by an AI assistant reading the source. Each was verified against
> the code, but verify with a profiler or query log before optimising, and re-run the test suite
> — several fixes touch authorization and visibility logic.
