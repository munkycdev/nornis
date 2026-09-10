# Design Document

## Overview

One new source type, three nullable columns on `Source`, one application service, two
repository lookups, one prompt section, and two GM affordances in the UI. Nothing downstream of
"a source exists and is Queued" changes: extraction, review, provenance, summary refresh and Ask
all already do the right thing with a source whose body is four pages of the Player's Guide.

```text
LibraryDocument ──chunks──▶ LibraryChunk (page, ord, text)
                                  │  GM picks (by similarity, or by page range)
                                  ▼
                       Source { Type = LibraryExcerpt,
                                LibraryDocumentId, LibraryPageFrom, LibraryPageTo,
                                Body = filing line + passages, Visibility = document's }
                                  │  MarkReadyAsync — the one path to the queue
                                  ▼
                       Extraction ▶ ReviewBatch ▶ accept ▶ facts cite the excerpt
                                                  ▶ summary refresh (already accept-time)
```

## Where each rule lives (pre-implementation check 1)

| Rule | The one place |
| --- | --- |
| How a source reaches the queue | `SourceService.MarkReadyAsync`. The excerpt service creates the row Draft and calls it; it does not enqueue itself. |
| The shape of an excerpt's title and body | `LibraryExcerptComposer` (Application, static). The prompt builder describes the filing line by reference to it; nothing parses the body back. |
| Who may file | `LibraryExcerptService`: GM only, both operations. |
| Which shelves a role sees | `LibraryService.GetAllowedScopes(role)`, unchanged. |
| Bound on an excerpt | `LibraryOptions.MaxExcerptChunks` (default 12 → at most 38,400 chars at the default chunk size, under `SourceService.MaxBodyChars`). Both entry points reject over it. |
| What happens to excerpts when the document goes | `LibraryDocumentRepository.DeleteAsync` detaches them (`SetWhereAsync`), the way `CampaignRepository.DeleteAsync` detaches sources — the FK is Restrict because SQL Server refuses a second cascade path from Worlds. |
| What null means | `Source.LibraryDocumentId` null on a `LibraryExcerpt` = the document has since been deleted; the excerpt keeps its title and text. On every other type it is always null. `LibraryPageFrom`/`To` are always set on an excerpt and always null elsewhere. |

## Domain

```csharp
SourceType
+ LibraryExcerpt   // GM-filed passages of a Library document; extracted like any source

Source
+ LibraryDocumentId: Guid?   // FK Restrict; null = deleted document (excerpts) or n/a (others)
+ LibraryPageFrom: int?      // 1-based, inclusive
+ LibraryPageTo: int?
+ LibraryDocument: LibraryDocument?   // navigation, for the title on the source page
```

`SourceConfiguration`: index on `LibraryDocumentId`; FK `Restrict`.

Migration `AddLibraryExcerptSources`: additive — three nullable columns, one index, one FK.
Applied before deploy per `nornis-migrations-ops`.

## Application

### `LibraryExcerptComposer`

```csharp
static string BuildTitle(string? artifactName, string documentTitle, int pageFrom, int pageTo)
    // "Thistlehold — Player's Guide, pp. 42–45"  /  "Player's Guide, p. 42"; at most 200 chars
static string BuildBody(string? artifactName, string documentTitle, int pageFrom, int pageTo,
                        IReadOnlyList<LibraryChunkHit> chunksInOrder, int overlapChars)
    // Line 1: the filing line — Excerpt from "Player's Guide", pp. 42–45, filed for the codex
    //   entry "Thistlehold". (or without the clause). Then the passages, in ord order, with
    //   the chunker's trailing overlap stripped from each chunk that starts with the previous
    //   chunk's tail — verified by StartsWith, never assumed.
```

The filing line is the artifact hint. It names the entry, not its id: an id would go stale on
merge or removal, and the name is exactly what `ListByNamesInTextAsync` matches to put the
entry into the existing-artifacts context, where the prompt's dedup rule already sends
proposals at the listed id.

### `ILibraryExcerptService`

```csharp
Task<AppResult<IReadOnlyList<LibraryExcerptCandidate>>> SearchAsync(SearchLibraryExcerptsCommand, ct)
Task<AppResult<Source>> FileAsync(FileLibraryExcerptCommand, ct)

record SearchLibraryExcerptsCommand(WorldId, ActingUserId, ActingUserRole, Guid? ArtifactId, string? Query)
record FileLibraryExcerptCommand(WorldId, ActingUserId, ActingUserRole, Guid DocumentId,
                                 IReadOnlyList<Guid> ChunkIds, int? PageFrom, int? PageTo, Guid? ArtifactId)
record LibraryExcerptCandidate(Guid ChunkId, Guid DocumentId, string DocumentTitle, int Page, string Text)
```

`SearchAsync`: GM only (403). Query = `Query` when given, else the artifact's name and summary
(artifact must be in the world, else 404; neither given → 400). Calls
`IReferencePassageRetriever.RetrieveForScopesAsync` with the GM's scopes and the GM as the
attributed user, so the embedding cost lands on the ledger as every other retrieval does.
Returns the passages as candidates, ordered as the retriever orders them (document, then
reading order).

`FileAsync`: GM only. Loads the document through `ILibraryService.GetByIdAsync` (world and shelf
checks in one place); refuses a document that is not `Indexed` (409 `document_not_indexed`).
Resolves chunks: by ids (`ILibraryChunkRepository.ListByIdsAsync(documentId, ids)` — an id of
another document is simply absent, and an absent id is 400 `chunk_not_found`), or by page range
(`ListByDocumentPagesAsync(documentId, from, to)`; empty → 400 `no_passages_in_range`). Over
`MaxExcerptChunks` → 400 `excerpt_too_large`. When `ArtifactId` is given, the artifact must be
in the world (404) — its name goes into the title and filing line; nothing else about it is
read or written. Creates the `Source` (Draft, `ExtractionEnabled`, visibility = document's,
`CreatedByUserId` = the GM, no campaign, no `OccurredAt`) through `ISourceRepository`, then
`ISourceService.MarkReadyAsync` — which is what makes it Queued and sends the message, and which
reverts to Ready and reports 502 if the queue is down, exactly as it does for a captured note.

### Extraction

- `ExtractionPromptBuilder.BuildSystemPrompt`: a `## Library Excerpt` section when
  `SourceType == "LibraryExcerpt"` — published setting material the GM chose; describes the
  world as it is, not events at the table; facts stated as description default to
  `Confirmed`; the first line of the Source Content is Nornis's filing line, not the
  document; when it names a codex entry, that entry is in the Existing World Artifacts list
  and proposals about it target that id.
- `ExtractionService.RetrieveReferencePassagesAsync` returns nothing for an excerpt: the
  excerpt *is* the reference, and retrieving its own neighbours back would pay an embedding
  to tell the model what it is already reading.

### Repositories

```csharp
ILibraryChunkRepository
+ Task<IReadOnlyList<LibraryChunkHit>> ListByIdsAsync(Guid documentId, IReadOnlyList<Guid> chunkIds, ct)
+ Task<IReadOnlyList<LibraryChunkHit>> ListByDocumentPagesAsync(Guid documentId, int pageFrom, int pageTo, ct)
```

Both ordered by `Ord`; both plain relational reads (no vector), so they run on InMemory.
`LibraryDocumentRepository.DeleteAsync` gains the `Source` detach before the delete.
`SourceRepository.GetByIdAsync` includes `LibraryDocument` beside `Campaign`.

## API

```text
POST api/worlds/{worldId}/library/excerpts/search        {artifactId?, query?}   GM  → candidates
POST api/worlds/{worldId}/library/{documentId}/excerpts  {chunkIds[], pageFrom?, pageTo?, artifactId?}  GM  → {sourceId, title, processingStatus}
```

Both on `LibraryController` (already behind `WorldMemberActionFilter`). `SourceResponse` gains
`LibraryDocumentId`, `LibraryDocumentTitle`, `LibraryPageFrom`, `LibraryPageTo` — the same
projection for every reader, so the indistinguishability tests keep holding.

## Web

- **`EnrichFromLibraryDialog`** (Shared), opened from a GM card on the artifact page. On open it
  searches with the artifact; shows a query field for a second search; lists candidates grouped
  by document with a checkbox per passage and the page beside it; "File as source" files one
  excerpt per document with selected passages and closes with the created sources. The page
  snackbars each title with a link to its source page. No indexed documents → a sentence saying
  the Library has nothing indexed yet, with a link to it.
- **Library document page**, GM only: a "File pages as a source" card — page from, page to, an
  optional codex-entry autocomplete (the artifact list, same call the merge picker uses), and a
  button. Success snackbars the title with a link.
- **Source page**: a `Library` row in the details card — the document as a link when it still
  exists, its title in plain text when it does not — with the page range. `SourceTypeDisplay`
  gains the `LibraryExcerpt` label; it is not a capture option.
- No new route; `ReachabilityTests` unaffected.

## Testing strategy

- `LibraryExcerptComposerTests`: title with and without an artifact, single page vs range,
  200-char truncation; body strips a verified overlap and leaves an unverified one alone; the
  filing line names the entry.
- `LibraryExcerptServiceTests`: Player and Observer refused on both operations; search query is
  name + summary and uses GM scopes; file by ids composes in ord order, inherits the document's
  visibility, ends Queued with one queue message; foreign chunk id → 400; unindexed document →
  409; range with no chunks → 400; over the bound → 400; queue failure → 502 and the source
  stays Ready. **Sabotage:** make `FileAsync` skip `MarkReadyAsync`; the Queued assertion and
  the queue-message assertion both go red.
- `ExtractionPromptBuilderTests`: the excerpt section is present for `LibraryExcerpt` and absent
  for `SessionNote`; it names `Confirmed`.
- `ExtractionServiceLibraryTests`: an excerpt source makes no retrieval call.
- `LibraryDocumentDetachesExcerptsTests` (Infrastructure, Sqlite): delete a document with an
  excerpt; the source survives with `LibraryDocumentId` null and its body intact.
- `LibraryExcerptEndpointTests` (Api): Player → 403 on both; GM files by chunk ids → 200 and the
  source reads back Queued with the library fields; a foreign document id → 404.
- Live: file Thistlehold's pages from the Player's Guide as GM, watch the batch land in Review,
  accept, confirm the summary refresh and the citation on the source page.

## Design decisions

1. **An excerpt is a Source, not a new provenance type.** `SourceReference` requires a
   `SourceId`; every reader of provenance — the artifact page, Ask citations, reveal, export —
   handles a source and nothing else. A second citation kind would touch all of them for no
   gain. Reveal already proved a synthetic source type fits.
2. **The text is copied, not referenced.** Sources are immutable and so are documents, but a
   document can be deleted; an excerpt that pointed at chunks would lose its body. Copying is
   what lets Requirement 5 hold without ceremony.
3. **No artifact id on the source.** The filing line names the entry. An id column would be a
   second FK to worry about on merge and removal, and would stamp knowledge structure onto a
   source — the thing `domain-model.md` says not to do with campaigns. The name is enough for
   the existing dedup path.
4. **Filed through `MarkReadyAsync`, not around it.** The queue-before-send ordering, the
   enqueue-failure revert and the stale-Queued wedge rule all live there. A second sender would
   be a second copy of each.
5. **Whole documents stay unextractable.** There is no "file the whole book" button, and the
   bound on passages per excerpt is not configurable from the client. A GM who wants a whole
   chapter files it a range at a time, which is the friction the rule intends.
6. **Confirmed by default, but still a proposal.** A written guide is more reliable than table
   hearsay, so the truth state default moves; the review gate does not.
