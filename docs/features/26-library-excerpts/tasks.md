# Tasks

Ordered so the composition and the queue path are proven before any page reads them. One
migration, additive.

**Status: built and shipped 2026-09-10** in one branch (`feature-26-library-excerpts`), from a
worktree, with the migration applied to production by the build session before the merge.
What the build changed from the spec, and what it found:

- **`SourceRepository.CreateAsync` now detaches the inserted row.** Filing an excerpt inserts a
  source and marks it ready in one request; `MarkReadyAsync` reloads the row and updates it,
  and the still-tracked insert made EF throw on the second instance. The fix is the contract
  `AddAndDetachAsync` already states, applied to the one repository insert that lacked it —
  found by the endpoint test, not by reading.
- **Search results come back with the full passage text**, not a snippet; the dialog trims
  for display. The GM is deciding whether a passage belongs, and 600 characters is the least
  that lets them.
- **The document page's card shows only once the document is Indexed**; before that there are
  no passages to file and the server would refuse anyway.
- **The two sabotages fired**: skipping `MarkReadyAsync` in `FileAsync` turned exactly the
  Queued test and the queue-failure test red; the enum-definition test caught the new
  `SourceType` on the first full run and was updated.
- **The format gate was already red on `main`** from the previous commit (IDE0300 in three
  test files, two of which the fixer could not rewrite); fixed here so the gate means something
  again.
- **Live walk, 2026-09-10, Ruins of Symbaroum as GM** (local API and Web against production,
  deployed worker): opened Thistlehold — "No facts recorded" — and *Enrich from the library*;
  the search returned twelve passages (eleven from the Gamemaster's Guide, one from the
  Player's Guide), five were ticked and filed as two sources, one per document, each titled
  "Thistlehold — {document}, pp. …" and queued. Both were Processed within a minute. The
  Gamemaster's Guide excerpt (GM-only, as its shelf) produced proposals of the intended shape:
  Confirmed facts and relationships aimed at Thistlehold's existing id, and CreateArtifact for
  Karabbadokk, Nighthome, the Ordo Magica tower and Count Alkantor Argona; one observed-cuddling
  relationship came back Likely, which is the prompt's hearsay rule doing its job. The source
  page shows the Library row linking the document with the page range, and the body opens with
  the filing line. Acceptance was left to the GM.

## Phase A — The excerpt

- [x] A1. `SourceType.LibraryExcerpt`; `Source.LibraryDocumentId` / `LibraryPageFrom` /
      `LibraryPageTo` and navigation; `SourceConfiguration` FK Restrict + index; `LibraryDocument`
      comment amended; `domain-model.md` amendment, dated.
- [x] A2. Migration `AddLibraryExcerptSources`.
- [x] A3. `ILibraryChunkRepository.ListByIdsAsync` / `ListByDocumentPagesAsync` (+ EF and
      in-memory fake); `LibraryDocumentRepository.DeleteAsync` detaches excerpts;
      `SourceRepository.GetByIdAsync` includes the document.
- [x] A4. `LibraryExcerptComposer` and its tests.
- [x] A5. `LibraryOptions.MaxExcerptChunks`; `ILibraryExcerptService` + `LibraryExcerptService`;
      tests including the sabotage.
- [x] A6. Prompt section and the retrieval skip; tests.
- [x] A7. `LibraryController` endpoints; `SourceResponse` fields; endpoint tests.

## Phase B — The pages

- [x] B1. Web contracts and client methods; `SourceTypeDisplay` label.
- [x] B2. `EnrichFromLibraryDialog` and the artifact page card.
- [x] B3. Library document page card.
- [x] B4. Source page `Library` row.
- [x] B5. Change log entry; `docs/features/README.md` row; `future-features.md` stale line.

## Verification

- [x] V1. Build, `dotnet test --solution Nornis.sln`, `dotnet format --verify-no-changes`, in
      the worktree.
- [x] V2. Migration applied 2026-09-10 (`AddLibraryExcerptSources`, additive, pre-deploy, with
      `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` — without it the credential probe times out
      and SqlClient reports only "The operation was canceled"); deploy watched below.
- [x] V3. Live: filed from the artifact page as GM; both batches reached Review; the source page
      shows the document link. The document-page range form was built and endpoint-tested but
      not walked live.

## Deferred (explicitly not this feature)

- Filing a whole document.
- Re-filing an excerpt when a document is re-uploaded.
- Players filing excerpts from the party shelf.
- Passages from image-only documents (maps, handouts) — nothing is indexed for them.
