# Nornis data-access audit — performance & operating cost

Scope: `src/Nornis.Infrastructure/Persistence/**`, `src/Nornis.Domain/{Repositories,Entities}`, plus every EF query reachable from `src/`. All EF usage is confined to `Persistence/Repositories/*` and `Infrastructure/Knowledge/KeywordKnowledgeRetriever.cs` — services talk only to repository interfaces, so query shape is centralized (this is good and makes most fixes local).

Ordered by impact / effort.

---

### [SEVERITY: High] The 15-second nav badge poll loads every session transcript in the world, twice

- **Where:**
  - `src/Nornis.Web/Components/Layout/NavMenu.razor:226` (poll, `TimeSpan.FromSeconds(15)`)
  - `src/Nornis.Api/Controllers/SourcesController.cs:121` (`GET /worlds/{id}/sources/activity`)
  - `src/Nornis.Application/Services/SourceService.cs:348`
  - `src/Nornis.Application/Services/ReviewService.cs:72`
  - `src/Nornis.Infrastructure/Persistence/Repositories/SourceRepository.cs:34` (`ListByWorldAsync`)
- **What:** `NavMenu` polls the activity endpoint every 15s for every signed-in user. The endpoint calls `_sourceService.ListByWorldAsync(...)`, which issues `SELECT * FROM Sources WHERE WorldId = @p` with `Include(s => s.Campaign)` — **every column**, including `Body` (`nvarchar(max)`, full session transcripts) and `DerivedText` (`nvarchar(max)`, PDF/vision output, `Source.cs:21,48`). The result is used only for `GroupBy(s => s.ProcessingStatus).Count()` (`SourcesController.cs:136`). The same request then calls `reviewService.ListReviewQueueAsync`, which at `ReviewService.cs:72` runs **the identical `ListByWorldAsync` query a second time** in the same DbContext scope, then loads all `ReviewBatches` and all `Artifacts` for the world (`ReviewService.cs:100,104`).
- **Why it costs:** The badge counts are ~5 integers. A world with 60 sessions of 40 KB of transcript is ~2.4 MB pulled from Azure SQL, twice, per user, every 15 s — 480 MB/user/hour of pure waste, plus the deserialization allocations on the API side. This is a continuous, idle-time DTU floor that scales with world size × concurrent users, i.e. it grows precisely as the product succeeds. `SourceListItemResponse` proves `Body`/`DerivedText` are never used by any list caller.
- **Fix:** Three independent wins, in order of payoff:
  1. Add a projection method and use it for counts:
     ```csharp
     Task<IReadOnlyDictionary<SourceProcessingStatus,int>> CountByStatusAsync(Guid worldId, CancellationToken ct) =>
         _context.Sources.AsNoTracking()
             .Where(s => s.WorldId == worldId)
             .GroupBy(s => s.ProcessingStatus)
             .Select(g => new { g.Key, Count = g.Count() })
             .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
     ```
     (Visibility filtering must move into SQL alongside it — see the `VisibilityFilter` predicate already written in SQL at `SourceRepository.cs:61-65`.)
  2. Add a `ListSummariesByWorldAsync` returning a `SourceSummary` projection (no `Body`, no `DerivedText`) and switch `SourceService.ListByWorldAsync`, `ReviewService.cs:72`, `ImportSessionService.cs:167,210,640`, `JourneyMapService.cs:41`, `SuggestionService.cs:166`, `StorylineDevelopmentReader.cs:91` to it. Only the extraction/AI paths need `Body`.
  3. Pass the already-materialized source list from the controller into `ListReviewQueueAsync` (or cache per-request) to kill the duplicate query.
- **Effort:** medium
- **Risk:** Low for (1) and (3). For (2), the projection must reproduce `CanSeeSource` exactly; any caller that silently relied on `Body` being present would get null — grep each call site. `Sources.razor:178` polls the full list every 4 s and benefits from the same projection.

---

### [SEVERITY: High] Artifact detail issues one query per citing source (N+1)

- **Where:** `src/Nornis.Application/Services/ArtifactService.cs:190`
- **What:**
  ```csharp
  foreach (var sourceId in allReferences.Select(r => r.SourceId).Distinct())
  {
      var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
      ...
      sourceTitles[sourceId] = source.Title;
  }
  ```
  `GetByIdAsync` (`SourceRepository.cs:26`) is `SELECT * ... Include(s => s.Campaign)` — a full row plus a JOIN — and only `source.Title` and the visibility fields are used.
- **Why it costs:** The artifact detail page is the most-visited authenticated page. An artifact with 30 facts cited across 18 distinct sources = **18 sequential round trips** on top of the 6 the method already makes, each dragging a full session transcript across the wire to read one `Title`. At ~5 ms RTT to Azure SQL that is ~90 ms of pure latency per page view, and megabytes of wasted I/O.
- **Fix:** Batch it. Add `ISourceRepository.ListVisibilityByIdsAsync(IReadOnlyList<Guid>, ct)` returning a projection:
  ```csharp
  .Where(s => ids.Contains(s.Id))
  .Select(s => new SourceVisibilityInfo(s.Id, s.Title, s.Visibility, s.CreatedByUserId))
  ```
  then apply `CanSeeSource` in memory over that small set. One query instead of N, and no `nvarchar(max)` at all.
- **Effort:** small
- **Risk:** Very low — `CanSeeSource` (`ArtifactService.cs:53`) reads only `Visibility` and `CreatedByUserId`, both in the projection. Keep the fail-closed behaviour for ids that return no row.

---

### [SEVERITY: High] `SourceReferences.TargetId` is unindexed, and the accept path scans it once per proposal

- **Where:**
  - `src/Nornis.Infrastructure/Persistence/Configurations/SourceReferenceConfiguration.cs` (no `HasIndex` at all)
  - `src/Nornis.Infrastructure/Persistence/Repositories/SourceReferenceRepository.cs:24` (`ListByTargetAsync`), `:32` (`ListByTargetIdsAsync`), `:95` (`DeleteByTargetAsync`), `:110`
  - `src/Nornis.Application/Application/ProposalApplicator.cs:788`
- **What:** `SourceReferences` has exactly one index — the EF-generated FK index on `SourceId` (confirmed in `NornisDbContextModelSnapshot.cs:1074`). Every query that filters on `TargetId` therefore does a full clustered-index scan. `ProposalApplicator.CreateSourceReference` runs `ListByTargetAsync(ReviewProposal, proposalId)` for **every proposal accepted**, and `ArtifactRemovalService.cs:108,117` runs `DeleteByTargetAsync` once per relationship *and* once per fact.
- **Why it costs:** `SourceReferences` is the highest-cardinality table in the schema — one row per accepted fact, relationship, and artifact, plus one per proposal. Accepting a 50-proposal batch = 50 full scans of that table. Deleting an artifact with 40 facts = 40 full scans, all inside one open transaction holding locks. Scan cost grows linearly with total world knowledge, so this degrades continuously rather than hitting a cliff.
- **Fix:** Add a migration with a covering index — the queries filter on `(TargetType, TargetId)` or `TargetId` alone:
  ```csharp
  builder.HasIndex(sr => new { sr.TargetId, sr.TargetType });
  ```
  `TargetId`-leading serves both `ListByTargetIdsAsync` (which filters on `TargetId` only) and the `TargetType`-qualified queries. Additive, safe to apply pre-deploy per the repo's migration rule.
- **Effort:** trivial
- **Risk:** Negligible — additive index. Costs a little write amplification on a table that is append-mostly.

---

### [SEVERITY: High] Batch accept re-reads every proposal 2–3× and reloads the whole artifact table per proposal

- **Where:** `src/Nornis.Application/Services/ReviewService.cs:561,606-618,634` and `src/Nornis.Infrastructure/Persistence/Repositories/ArtifactRepository.cs:106`
- **What:** For a batch of up to 50 proposals (cap at `ReviewService.cs:552`):
  - `OrderCreatesFirstAsync` (`:606`) loops `GetByIdAsync` per id — **50 round trips just to read `ChangeType`**.
  - `TryAcceptOneAsync` (`:634`) then re-reads the same proposal, plus its batch, plus its source — **150 more round trips**, mostly duplicates (all 50 proposals usually share one batch and one source).
  - Each `ApplyAsync` that resolves an artifact by name reaches `ArtifactRepository.ListByEquivalentNameAsync` (`:106`), which loads **every non-archived artifact in the world** and filters in memory (`:114`).
- **Why it costs:** ~200 round trips plus up to 50 full-world artifact loads for one button press. At 5 ms RTT that is a full second of pure network latency before any work happens, and the artifact reloads are O(batch × world size). The in-memory name filter is deliberate and well-argued in the comment at `:93-105` — the issue is not the filter but that the fetch is repeated per proposal instead of once per batch.
- **Fix:** Two contained changes:
  1. Load the batch's proposals, their `ReviewBatch`, and the `Source` **once** before the loop (`ListByIdsAsync` on proposals; batch/source are shared) and pass them into `TryAcceptOneAsync`. Keep the re-read only for the single retry pass at `:577`, where staleness actually matters.
  2. Hoist the artifact candidate set: fetch once per `BatchAcceptAsync` call, pass an `IReadOnlyList<Artifact>` (or a name-key lookup) down through `ApplyAsync`, and refresh it only after a `CreateArtifact` proposal lands. `OrderCreatesFirstAsync` already guarantees creates are applied first, so one refresh after the creates group suffices.
- **Effort:** medium
- **Risk:** Moderate — this is the correctness-sensitive apply path. The per-proposal transaction boundary must stay; only the *reads* move out of the loop. The retry pass at `:577` exists precisely because intra-batch state changes, so the hoisted artifact set must be invalidated after each successful create or the "salt factor" class of bug described at `ArtifactRepository.cs:99-101` returns. Route this one through `code-critic`.

---

### [SEVERITY: Medium] `Update()` on detached entities rewrites `nvarchar(max)` transcripts on every status change

- **Where:** `src/Nornis.Infrastructure/Persistence/Repositories/SourceRepository.cs:212`; callers at `src/Nornis.Application/Services/SourceService.cs:401,407,414,423`
- **What:** Repositories return `AsNoTracking()` entities, so `_context.Sources.Update(source)` marks **all** properties modified and emits `UPDATE Sources SET Title=…, Body=…, DerivedText=…, …` — the entire row. `MarkReadyAsync` calls it **twice in a row** (`:407` Draft→Ready, then `:414` Ready→Queued) to change one enum column each time.
- **Why it costs:** Two full-row writes carrying the whole session transcript and derived text, plus transaction log volume proportional to the transcript, to flip one small column. On Azure SQL the log write is the expensive part. Note that `SourceRepository` already has the right pattern in `UpdateProcessingStatusAsync` (`:153`) — a tracked load with a scoped write — but `MarkReadyAsync` doesn't use it.
- **Fix:** Point `MarkReadyAsync` at the existing `UpdateProcessingStatusAsync`, and collapse the double write to a single `Queued` transition (the comment at `SourceService.cs:409-412` explains the ordering intent — that is satisfied by committing `Queued` before the enqueue, which one write does). For the general case, prefer `ExecuteUpdateAsync` on the relational provider:
  ```csharp
  await _context.Sources.Where(s => s.Id == id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProcessingStatus, status), ct);
  ```
  guarded by `_context.Database.IsRelational()` for the InMemory test provider, mirroring `WorldRepository.cs:75`.
- **Effort:** small
- **Risk:** Low. `ExecuteUpdate` bypasses the change tracker, so any in-scope tracked copy goes stale — these repositories return `AsNoTracking` entities anyway. Check the worker's redelivery logic still sees the status it expects.

---

### [SEVERITY: Medium] `ListByTargetIdsAsync` drags a full `Source` row per reference

- **Where:** `src/Nornis.Infrastructure/Persistence/Repositories/SourceReferenceRepository.cs:32-42`
- **What:** `.Include(sr => sr.Source)` on a query filtered by `targetIds.Contains(sr.TargetId)`. Consumers use only `Source.Title`, `OccurredAt`, `CreatedAt` — see `KeywordKnowledgeRetriever.cs:184-185` and `ArtifactService.cs:177`.
- **Why it costs:** SQL Server returns the joined `Source` columns **once per reference row**, so a Loremaster question matching 40 facts across 8 sources ships those 8 transcripts 40 times over the wire (EF's identity map dedupes the objects, not the bytes). This runs on every `Ask` (`KeywordKnowledgeRetriever.cs:100`), every artifact detail, and inside the continuity audit (`ContinuityAuditService.cs:159`) — and it compounds with the unindexed `TargetId` scan from the finding above.
- **Fix:** Replace the `Include` with a projection to a small record:
  ```csharp
  .Select(sr => new SourceReferenceWithSource(
      sr.Id, sr.SourceId, sr.TargetId, sr.TargetType, sr.Quote,
      sr.Source.Title, sr.Source.OccurredAt, sr.Source.CreatedAt))
  ```
- **Effort:** small
- **Risk:** Low — changes the return type, so all six call sites need touching. `MapSourceReference` already treats the navigation as possibly-null ("best-effort"), so nothing depends on the full entity.

---

### [SEVERITY: Medium] Cascade deletes run row-by-row: SELECT + DELETE + SaveChanges per row

- **Where:** `src/Nornis.Application/Services/ArtifactRemovalService.cs:104-118`; `src/Nornis.Application/Services/SourceReprocessService.cs:120-135`
- **What:** Each loop iteration calls `_artifactFactRepository.DeleteAsync(id)` / `_artifactRelationshipRepository.DeleteAsync(id)`, and each of those (`ArtifactFactRepository.cs:47`, `ArtifactRelationshipRepository.cs:80`) does a tracked `FirstOrDefaultAsync` **then** `Remove` **then** its own `SaveChangesAsync`. `ArtifactRemovalService` additionally calls `DeleteByTargetAsync` per row (another SELECT + DELETE + SaveChanges, over the unindexed `TargetId`).
- **Why it costs:** ~4 round trips and one full `SourceReferences` scan per fact. Removing an artifact with 40 facts and 15 relationships ≈ 220 round trips and 55 table scans, all inside one open transaction — so lock duration scales with the graph too. A world reprocess (`SourceReprocessService`) can delete far more than that in one go.
- **Fix:** Add set-based repository methods and call them once:
  ```csharp
  Task DeleteByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct) =>
      _context.ArtifactFacts.Where(f => ids.Contains(f.Id)).ExecuteDeleteAsync(ct);
  ```
  plus `DeleteByTargetsAsync(targetType, IReadOnlyList<Guid> targetIds)`. Guard with `IsRelational()` and fall back to `RemoveRange` + one `SaveChanges` for the InMemory test provider — the pattern is already established in `WorldRepository.DeleteWorldGraphAsync` (`:95-105`), which does this correctly.
- **Effort:** medium
- **Risk:** Moderate. The comment at `ArtifactRemovalService.cs:111-112` notes facts are deleted explicitly so their provenance rows go too — batching must preserve that pairing. `ExecuteDelete` ignores the change tracker and does not fire cascades through EF, but the FK cascade behaviours here are database-level, so this is safe; verify against `ArtifactConfiguration`/`ArtifactFactConfiguration` delete behaviours before shipping.

---

### [SEVERITY: Medium] Cost dashboard scans `AiUsageRecords` four times with no supporting index

- **Where:** `src/Nornis.Application/Services/CostService.cs:44` (comment confirms the sequential intent) and `src/Nornis.Infrastructure/Persistence/Repositories/AiUsageRecordRepository.cs:243` (`BuildFilteredQuery`)
- **What:** `GetSummaryAsync` runs `AggregateAsync`, `AggregateByOperationTypeAsync`, `AggregateByModelAsync`, `AggregateByUserAsync` sequentially, each rebuilding the same `WorldId + CreatedAt` range filter and re-scanning. The only index on `AiUsageRecords` is the FK index on `WorldId` (`NornisDbContextModelSnapshot.cs:87`) — there is no `(WorldId, CreatedAt)` composite.
- **Why it costs:** Four scans of a monotonically growing ledger per dashboard load, each doing a range seek that degenerates to scanning the whole world's history once `WorldId` alone is no longer selective. Separately, `AiBudgetGuard.GetStatusAsync` (`AiBudgetGuard.cs:33`) runs `AggregateAsync` **before every AI operation**, and `SumPublicAskCostAsync` (`AiUsageRecordRepository.cs:101`) does the same per public Ask — those are the hot ones.
- **Fix:** Add `builder.HasIndex(r => new { r.WorldId, r.CreatedAt })` — this is the single highest-value index in the schema given the budget guard runs it per AI call. Optionally include `EstimatedCostUsd` as an `.IncludeProperties(...)` covering column so the budget check becomes an index-only seek. The four dashboard aggregates can also collapse into one `GroupBy(r => new { r.OperationType, r.Model, r.UserId })` with client-side rollup, though that is secondary.
- **Effort:** trivial (index) / medium (aggregate collapse)
- **Risk:** Negligible for the index. The aggregate collapse changes result shapes — do it only if the dashboard shows up in traces.

---

### [SEVERITY: Low] No connection resiliency or context pooling against Azure SQL

- **Where:** `src/Nornis.Api/Program.cs:78-79`; `src/Nornis.Worker/Program.cs:59-60`
- **What:** `AddDbContext<NornisDbContext>(o => o.UseSqlServer(cs))` with no `EnableRetryOnFailure()` and no `AddDbContextPool`.
- **Why it costs:** Azure SQL throttles and drops connections routinely; without the retry strategy every transient fault surfaces as a 500 and the client retries the *whole* request — which, for the endpoints above, means re-running the expensive queries. Pooling saves the per-request `DbContext` + change-tracker allocation on a service that does 2 DB round trips (`UserProvisioningMiddleware.cs:52`, `WorldMemberFilter.cs:24`) before any controller code runs.
- **Fix:**
  ```csharp
  options.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(
      maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null));
  ```
  Consider `AddDbContextPool` once the explicit-transaction paths are reviewed.
- **Effort:** trivial
- **Risk:** `EnableRetryOnFailure` refuses to auto-retry user-initiated transactions — `EfUnitOfWork.BeginTransactionAsync` is used in `ReviewService`, `ArtifactRemovalService`, `SourceReprocessService`, `WorldRepository`, `LibraryChunkRepository`. Those call sites must be wrapped in an execution strategy (`db.Database.CreateExecutionStrategy().ExecuteAsync(...)`) or they will throw at runtime. That makes this low-effort-to-write but needs care to land — do not ship it without exercising those paths.

---

## Unverified / worth a look

- **`ArtifactRepository.ListByNamesInTextAsync` (`:160`)** loads all non-archived artifacts per Loremaster question and substring-matches in memory (`:178`). Same shape as `ListByEquivalentNameAsync` but called once per question rather than in a loop, so the cost is bounded by world size. It is defensible today; it becomes a problem at a few thousand artifacts per world. No SQL equivalent exists for "artifact name appears anywhere in this text", so a fix means a projection to `(Id, Name)` first, then a targeted `ListByIdsAsync` — worth doing but I could not measure the current pain.
- **`ArtifactFactRepository.ListByArtifactIdsAsync` (`:61`)** applies `maxPerArtifact` **after** materializing (`:83-86`), so the cap does not bound the fetch. `ReviewService.cs:118` calls it with `int.MaxValue` over every artifact in the world, which loads every fact in the world — but only when the queue contains an `UpdateFact` proposal. I could not establish how common that is; if the review queue is ever slow, this is the first place to look. A windowed SQL query (`ROW_NUMBER() OVER (PARTITION BY ArtifactId ORDER BY UpdatedAt DESC)`) would push the cap into SQL.
- **`LibraryChunkRepository.SearchAsync` (`:64`)** does an exact KNN scan over all of a world's chunks with no ANN index. The comment at `:73-74` claims milliseconds at a few thousand rows, which is plausible, but there is no index backing `VectorDistance` and nothing bounds chunk count as libraries grow. Worth measuring before it matters.
- **`ImportSessionRepository.GetByIdAsync`/`GetNonTerminalByWorldAsync` (`:24,:32`)** both `Include(s => s.Items)`; `Import.razor:664` polls every 2 s. Single collection include, so no cartesian explosion, and item counts are probably modest — but a 2-second poll over a large import warrants a look at the row count.

## Things that are already right

Worth stating so they don't get "fixed": `WorldRepository.DeleteWorldGraphAsync` (`:93-153`) is a model set-based cascade with a correct InMemory fallback; `AiUsageRecordRepository`'s aggregates all compute in SQL rather than materializing; `ReviewProposalRepository.CountOpenBySourcesAsync` (`:60`) and `ListReviewQueueAsync` (`:94`) join and page in SQL with a `Take(limit + 1)` has-more probe; `AsNoTracking()` is applied consistently on read paths across every repository; and the `VisibilityFilter` predicates are deliberately kept in SQL with a test guarding against drift (`ArtifactRepository.cs:93-95`).
