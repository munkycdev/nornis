# Design Document

> **Amendment (2026-11-03, Phase B):** what building it changed. The text below is left as
> written.
>
> - **The link's foreign key is `Restrict`, not `SetNull`.** Worlds cascade to both
>   `Adventures` and `LibraryDocuments`, so a second cascading path from `Adventures` is
>   refused by SQL Server. `AdventureRepository.DeleteAsync` clears the links itself
>   (`SetWhereAsync`) before deleting, in the same unit of work. The SQL-backed repository test
>   fails with a foreign key error when that line is removed.
> - **No composite index.** EF's index on the foreign key (`IX_LibraryDocuments_LinkedAdventureId`)
>   is enough; the Library is read whole per world and never filtered by the link in SQL.
> - **The class is `LibraryCategories`** (Application/Services), and a print is known by
>   `AdventureId` *or* a `PrintDetails` snapshot, so a print whose Adventure was deleted is still
>   a Print. `WhyNotOrganisable` is the one place that refuses a print or a Bibliotaph book.
> - **The API sends `linkedAdventureId`, not a `linkedAdventure` object**, and only to a GM. A
>   player's rows carry `onShelf: true` and no link. The Web names the Adventure from the Prep
>   list it already loads for the GM (`GetAdventuresAsync`).
> - **No `documents` on `GET prep/{id}`.** The Adventure page's Documents card reads the Library
>   list and keeps what `LibraryFilter.BelongsTo` says belongs, which is the same rule the
>   Library's Adventure menu uses, so the two can't disagree. The card also has an Upload
>   button that opens the upload dialog preset to the Adventure (the entry point feature 38 was
>   to add; it was one line here).
> - **The Adventure menu offers Shelf, Everything, then each Adventure with documents.**
>   Everything was added so a GM can search across the whole Library in one place.
> - **The chips' counts follow the Shelf or Adventure choice but not the search**, so chips
>   don't come and go while typing. The Prep page doesn't remember its chip after all; the
>   Library remembers its own in local storage (`nornis.library.category`), and a blocked store
>   just forgets.
> - **Rows show the category** where they showed the kind (a Bibliotaph row says Book rather
>   than Sourcebook), and the upload dialog's select is labelled Category with the same four
>   choices as the document page (`LibraryDisplay.ChoosableCategories`; Book is stored as the
>   kind Sourcebook).

## Overview

The Library gets two organising facts per document and a page that filters by them:

```
LibraryDocument
  Kind                 (exists)   Sourcebook | Map | Handout | Other      upload's category
  AdventureId          (exists)   set only on prints (feature 34)          "this is a print"
  LinkedAdventureId    (new)      set only on uploads, by the GM           "belongs to"
                     ↓
LibraryDisplay.Category(doc)  →  Book | Map | Handout | Print | Other     one place
LibraryDisplay.OnShelf(doc)   →  no LinkedAdventureId, or is a print       one place
                     ↓
Library page: chips (category) × Adventure menu × search × sort, all client-side
```

## Data model

- `LibraryDocument.LinkedAdventureId` (`Guid?`), a foreign key to `Adventures` with
  `OnDelete(SetNull)`. Deleting an Adventure returns its uploads to the Shelf (Requirement 2.5).
  An index on `(WorldId, LinkedAdventureId)`.
- A separate column, not a reuse of `AdventureId`, because `AdventureId` means "this is a
  print" and `PrepBoundaryTests` pins who sets it. Two meanings in one column would make a
  linked handout read as a print everywhere `AdventureId` is checked today (the Library badge,
  the share warning, the print snapshot).
- Migration `AddLibraryDocumentAdventureLink`, which Dave applies before the merge.
- No new enum value. The category Print is derived from `AdventureId is not null`, so it can't
  disagree with the print snapshot.

## Rules, one place each

- `LibraryCategory` (Application) has `Of(LibraryDocument)` and `IsOnShelf(LibraryDocument)`.
  The API returns both on each document (`category`, `onShelf`), so the Web never decides them
  (as `IsFilled` is decided by the API for sections).
- `LibraryService.SetAdventureLinkAsync(worldId, documentId, adventureId?, actor)` is the only
  writer of `LinkedAdventureId`. GM only. It refuses a print (`400 prints_belong_to_their_adventure`),
  a Bibliotaph row (`400 books_stay_on_the_shelf`), and an Adventure in another world (404).
  `PrepBoundaryTests` gains a scan that nothing else assigns it.
- `LibraryService.SetKindAsync` changes an upload's kind. It refuses a print and a Bibliotaph
  row, for the same reasons.

## API

- `GET /library` adds `category`, `onShelf` and, for GM callers only, `linkedAdventure`
  (`{ id, title, slug }` or null). Players get `linkedAdventure: null` (Requirement 2.6).
- `PUT /library/{id}/adventure-link` with `{ adventureId: Guid? }`.
- `PUT /library/{id}/kind` with `{ kind }`.
- `POST /library/uploads` (the upload request) takes an optional `linkedAdventureId`.
- `GET /prep/{adventureId}` adds `documents`: the Adventure's linked uploads and prints, each
  as `{ id, title, slug, category }`.

## Web

- **Library page.** Above the list:
  - the category chips with counts, as on the Prep page (`MudChipSet`, single selection),
    remembered in local storage under `nornis.library.category`;
  - for GMs, an **Adventure** menu ("Shelf", then each Adventure with documents);
  - a search box and a Newest/Title sort.
  
  "Shelf" (the default) shows `onShelf` documents. Choosing an Adventure shows that
  adventure's documents instead. Each row shows its category label, and for a GM, a small
  adventure tag when the document is linked.
- **Document page.** A GM gets a "Belongs to" select (None, or an Adventure) and, for an
  upload, a category select.
- **Upload dialog.** An optional "Belongs to" select. When the dialog is opened from an
  Adventure page (feature 38 adds that entry point), it is preset to that Adventure.
- **Adventure page.** A "Documents" card under Book details listing the Adventure's documents.

## Pre-implementation checks

1. **Where each rule lives.**
   - The category and the shelf rule live in `LibraryCategory`, and the API sends both.
   - The link has one writer, `SetAdventureLinkAsync`, pinned by a boundary scan.
   - What a print is stays `AdventureId`.
2. **Second caller.**
   - The Library page, the document page and the Adventure page all read `category` and
     `onShelf` from the API.
   - The upload dialog and the document page both go through `SetAdventureLinkAsync` (the
     upload service calls it after creating the row).
3. **Null, empty, zero.**
   - No link means the document is on the Shelf.
   - A world with no Adventures shows no Adventure menu.
   - A chip with a zero count is hidden.
   - An empty search shows everything the chip and menu allow.
4. **Unbounded.** The page already loads every document in one call. A world with hundreds of
   documents is the trigger to add server paging; this feature doesn't need it.
5. **Show each guard failing.**
   - Players never see `linkedAdventure`: an API test as a player.
   - A print can't be relinked and a book can't be linked: service tests.
   - Deleting an Adventure clears its links: a repository test against SQL.
   - The boundary scan fails when a second writer is added.

## Testing

- `LibraryCategoryTests`: every kind, prints, Bibliotaph rows, and the shelf rule.
- `LibraryServiceTests`: linking, unlinking, the refusals, the GM-only rule, and another world.
- An API test for the link and kind endpoints and a player's view.
- An Infrastructure test (SQL) for `SetNull` when an Adventure is deleted.
- bUnit tests:
  - the chips and their counts;
  - Shelf versus an Adventure;
  - search and sort;
  - the document page's selects shown only to the GM;
  - the Adventure page's Documents card.

## Out of scope

- Free-form tags and folders. Categories and the adventure link cover what Dave named; tags
  can come later if a world outgrows them.
- Linking one document to several Adventures.
- Server-side paging and search.
