# Tasks

Three PRs: this spec; then the PDF booklet and the print page that shows it; then one-click
filing and the version bump. Ships as Nornis v3.5. Neither phase has a migration.

> **Amendment, 2026-11-03.** Phases B and C shipped as one PR: removing the HTML booklet and
> removing the print upload touch the same page, the same dialog and the same tests, and Dave
> is the only user, so a halfway state would only have been seen by him. The differences from
> the plan are listed in the amendment at the top of `design.md`. Only C6, Dave's live walk,
> is left.

**Status: specified 2026-11-03 after Dave's 3.4 walk with the Slurping Spa. Dave chose a .NET
PDF library in the API (over a Chrome print container) and two columns with continuous flow
(over one column).**

## Phase A — Spec

- [x] A1. `requirements.md`, `design.md` and this file; row 34 in the features README.
- [x] A2. Feature 30's design gains a dated amendment: the deferred one-click PDF is built
      here, with a library rather than the container it named.

## Phase B — The PDF booklet

- [x] B1. `BookletModel` and its records (Application, `Prep/`).
  - Move `MarkQuote`, the stat-block parse and the cover lines over from
    `AdventurePrintModel`.
  - Add task lists, the soft-break option, images as alt text and links as text.
  - `BookletModelTests`: port every `AdventurePrintModelTests` convention, then add the new
    ones.
- [x] B2. `NewlineConventionMirrorTests`: Web's `MarkdownRenderer` and `BookletModel` agree on
      soft breaks. Sabotage: drop the option from either side.
- [x] B3. Fonts in Infrastructure, `Printing/Fonts/`, as embedded resources with their
      licences:
  - Newsreader and IBM Plex Sans, 400 and 600, roman and italic, Latin WOFF from `@fontsource`
    5.3.0;
  - DejaVu Sans and DejaVu Sans Mono as the fallback.
- [x] B4. `IBookletPdfRenderer` (Application) and `QuestPdfBookletRenderer` (Infrastructure).
  - Cover page, chapters, flow, keep-together, lists, tables, boxes, stat blocks, footer.
  - Set the license once, and set `ThrowOnMissingTextGlyphs` to false.
  - `QuestPdfBookletRendererTests` with PdfPig: order, heading never last in a column, a small
    box kept whole, a large box flowing, the header repeated, bullets inside their column,
    emoji, the maximum-size fixture, the cover alone, a Chapter on a new page.
  - Sabotage: drop each keep-together rule in turn.
- [x] B5. `AdventureBookletPrinter` (Application): resolve the print details, build, render.
      Log `booklet_rendered`.
- [x] B6. `AdventureService.GetBookletAsync`; `GET prep/{id}/booklet` (PDF, GM); the route in
      the 403 sweep. Remove `GET prep/{id}/print`, `GetPrintAsync` and
      `AdventurePrintResponse`.
- [x] B7. Web:
  - the `/prep-booklets/{worldId}/{adventureId}` proxy;
  - the print page as an iframe, with Download and Open PDF;
  - remove `AdventurePrintDto`, `GetAdventurePrintAsync` and the booklet CSS;
  - bUnit tests.
- [x] B8. Retire `AdventurePrintModel` and its tests, and update the boundary tests that named
      it.
- [x] B9. Render a booklet inside the `api` Docker target. This proves the native library
      loads on the production base image.
- [x] B10. Validate:
  - in a clean worktree: build with 0 warnings, the full test run, and
    `dotnet format --verify-no-changes`;
  - sabotage each new guard.

## Phase C — One-click filing, and ship 3.5

- [x] C1. `LibraryService.FilePrintAsync` and `CompleteStoredAsync` (shared with
      `ConfirmUploadAsync`).
  - `LibraryFilePrintTests`.
  - Sabotage: a non-GM, another world, a render failure, oversize, the last print, the stored
    shape.
- [x] C2. Remove the `AdventureId` branch of `RequestUploadAsync`; an upload that names one
      gets 400 `prints_are_filed_from_prep`.
  - Move `PrepBoundaryTests`' single-writer scan to `FilePrintAsync`.
  - Web: the upload dialog loses its print parameters.
- [x] C3. `POST prep/{id}/booklet/file`; the route in the 403 sweep; endpoint tests.
- [x] C4. Web: the File to Library button, one click, with the snackbar and its link; bUnit
      tests.
- [x] C5. Ship:
  - `MinorVersion` 5;
  - the change log, the Features page and the README;
  - row 34 in the features README;
  - ticks here, and the steering re-check.
- [ ] C6. Live walk (Dave): open the Slurping Spa's print page and check the gaps, the bullets
      and the column order. File it with one click, open it from the Library, and see it in
      Bibliotaph.
