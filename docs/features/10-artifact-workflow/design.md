# Design Document

## 1. Source excerpts on artifact detail

`SourceReference.Quote` and `SourceId` already reach the client in
`ArtifactDetailResponse.SourceReferences`. Add `SourceTitle` to the reference DTO
(joined in `ArtifactService` when assembling detail), and render in
`ArtifactDetail.razor`: quote text (blockquote style) + source title linking to
`/sources/{sourceId}`.

## 2. Per-world AI budget

- `World.DailyAiBudgetUsd: decimal?` + rename-free additive migration.
- `AiBudgetGuard.CheckAsync(worldId)` loads the world (IWorldRepository) and uses
  `world.DailyAiBudgetUsd ?? options.DailyWorldBudgetUsd`. Guard callers unchanged.
- `UpdateWorldRequest`/`WorldResponse` gain the field; `WorldService.UpdateAsync`
  validates 0–100 and GM-only (existing role gate covers it).
- Settings page: numeric field with "server default" placeholder when null.
- `CostsController` summary returns the effective budget instead of raw config.

## 3. Artifact quick-add

Pure client + existing APIs. On `ArtifactDetail.razor` (hidden for observers): a
multiline box + visibility select + "Add to record" button that

1. `POST /sources` with `title: "Note on {artifact.Name} — {date}"`, `type: GMNote`
   (members: `JournalEntry`), body prefixed `Regarding {artifact.Name}: `,
2. `POST /sources/{id}/ready`,
3. snackbar linking to the review queue.

The name prefix guarantees the artifact is pulled into extraction context via the
existing name-match retrieval; no schema or pipeline change.

## 4. Storyline retrospective

New Application service `StorylineRetrospectiveService` (GM-only, budget-gated):

1. Load Active storylines with facts (open questions included) and relationships.
2. One AI call (new `IRetrospectiveAiClient`, Azure OpenAI implementation, structured
   output): for each storyline, verdict `Resolved | Dormant | StillActive` + one-line
   rationale. Chunk storylines if the list is huge (50 per call).
3. Create a synthetic source (`type: GMNote`, `ProcessingStatus: Processed`, title
   "Storyline Retrospective — {date:yyyy-MM-dd}", body listing assessed storylines) and
   a ReviewBatch on it containing `UpdateArtifact { status }` proposals for every
   non-StillActive verdict, `targetId` = storyline id. Max 50 proposals per batch — the
   proposal cap and the chunk size align.
4. Track usage under new `AiOperationType.StorylineRetrospective`.

API: `POST /api/worlds/{id}/storylines/retrospective` → `{ assessed, proposed, batchId }`.
UI: GM-only button on the Storylines page; done-state points at the review queue.

Closing "associated artifacts" happens the same way afterwards: dormancy of
non-storyline artifacts is observable in the same pass output when the AI marks a
storyline Resolved — kept OUT of scope for the first cut; artifacts change status via
normal proposals when future sources warrant it.
