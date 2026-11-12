# Design Document

> **Amendment (2026-11-12, Nornis 3.37): set-off blocks get a line of space.** Asked for by Dave:
> "extra margins above and below the special content areas … maybe a full line break's worth".
> The text and the earlier amendments are left as written.
>
> - **Which blocks.** Every box (read-aloud, prop, ask, GM), stat and item blocks, and code
>   (`QuestPdfBookletRenderer.IsSetOff`, the one place the list lives). Tables, lists, headings
>   and rules stay in the flow at the usual 6 pt.
> - **How much.** One body line (`Typeset.LinePt`: the GM's size × line spacing, 13 pt at the
>   default) on top of the 6 pt block spacing, above and below. Between two set-off blocks it is
>   one gap, not two. None before a chapter's first block or after its last. Chapter flow only:
>   inside a box, a list item or a prop page the spacing is unchanged.
> - **Labels stay with their block.** A heading group that introduces a set-off block takes the
>   space above the heading, so the heading stays tight to its box. So does a lead-in: a
>   paragraph of up to 80 characters ending in a colon ("Opening line:"), straight before one.
> - **Never blank space at a column's top.** The space is a spacer item that takes what is left
>   of the column up to a line and no more (`MaxHeight` + `ExtendVertical`), so at a column's foot
>   it is cut short and the block starts the next column flush. The near-foot tests pin this.

> **Amendment (2026-11-08, Nornis 3.17): the GM chooses the type.** Asked for on the Book tab:
> "specify the font name, font size, and line spacing". The text and the earlier amendment are
> left as written.
>
> - **A curated list, not any font.** The PDF is laid out on the server, so a face on the GM's
>   PC can't be used. `BookFont` lists seven open-licence serifs: Newsreader (the default),
>   EB Garamond, Literata, Crimson Pro, Source Serif 4, Lora and Alegreya, each embedded as four
>   Latin WOFFs (400/600, roman/italic, from Fontsource) beside its OFL text.
>   `BookTypography.NameOf` is both the name the GM reads and the family the WOFFs register under.
> - **Stored per Adventure** as `BookTypography` (an owned type: `Typography_Font`, `_Size`
>   decimal(3,1), `_LineSpacing` decimal(3,2); migration `AddBookTypography`). Every field null
>   is the default (Newsreader, 10 pt, 1.3), so an untouched Adventure prints as before. Size
>   8–14 pt in half points; line spacing 1.0–2.0 in twentieths. `PUT .../typography` sends all
>   three; a default is sent as null. A filed print keeps the type it was printed in.
> - **What follows it.** The renderer resolves a `Typeset` (face, size, line height) and passes
>   it down. The body, read-aloud and props take all three. Every other body size (chapter
>   title, headings, stat blocks, tables, code, list markers and checkboxes) was set for the
>   10 pt body and scales by size / 10 through `Typeset.Pt`; stat blocks and tables keep their
>   own 1.25 line height. The heading keep-with-next estimate (`HeadingRoomPt`, line height,
>   characters per line) scales the same way. The cover, rights page, handout pages and the
>   props sheet take the face only, at their own sizes.
> - **Booklet** gains `Typography` (and `BookletProps` too), the one thing in it that names
>   type; it still carries no point sizes.
> - **The Book tab's Loremaster** sees the type and the faces on offer, so it never suggests a
>   face Nornis can't print.
> - **The Typography card** shows a sample in the chosen type at a booklet column's width
>   (252 pt), loading the six new faces from Google Fonts for the card only. Its list and bounds
>   are mirrors pinned by `TypographyMirrorTests`.

> **Amendment (2026-11-03, Phases B and C):** what building the booklet changed. The text
> below is left as written.
>
> - **One PR for B and C.** Retiring the HTML booklet and retiring the print upload touch the
>   same page, dialog and tests, so they shipped together (see `tasks.md`).
> - **Record names.** The blocks are `Booklet`-prefixed throughout (`BookletSectionHeading`,
>   `BookletHeading`, `BookletParagraph`, `BookletListItem`, `BookletTable`, `BookletBox`,
>   `BookletStatBlock`, `BookletCode`, `BookletRule`, `BookletRun`, `BookletBreak`, and the
>   enums `BookletListStyle`, `BookletTask`, `BookletBoxKind`, `BookletRunStyle`), so no name
>   collides with QuestPDF's or the Domain's. A table cell is a plain list of inlines.
> - **The renderer returns `BookletPdf(Bytes, Pages)`**, not bare bytes; the page count is
>   read back with PdfPig and logged in `booklet_rendered`.
> - **The printer also resolves the details for the page.** `AdventureBookletPrinter` has
>   `ResolveDetailsAsync` beside `PrintAsync`, so the Adventure's "Book details" preview and the
>   cover can't resolve them two ways. `PrintAsync` returns `PrintedBooklet(Pdf, Details,
>   FileName)`; the file name is the Adventure's title plus `.pdf`.
> - **Headings.** `EnsureSpace` in QuestPDF only acts on content that doesn't fit whole, so a
>   heading on its own was still placed at a column's foot (the renderer test found it). A
>   heading run is now always grouped with the block it introduces. The group needs room for
>   27 pt per heading plus that block's first unbreakable piece (two lines of a paragraph, a
>   short first list item, a table's header and first row), and is kept whole when the block is
>   small. `KeepWithNextPt` is gone.
> - **Short paragraphs and list items** (up to 500 characters) are kept whole; longer ones flow.
>   A table with up to four rows and 500 characters is kept whole with its heading.
> - **Table columns.** A column whose cells are all 16 characters or fewer is fixed to fit them
>   (so "0:00-0:20" never wraps), when those columns take at most half the width. The rest share
>   by text length, clamped to one to three times the narrowest, and none is narrower than its
>   longest word. Every row is `ShowEntire`.
> - **Code uses IBM Plex Mono** (one Latin 400 WOFF), not DejaVu Sans Mono. DejaVu Sans stays as
>   the one fallback.
> - **The print page shows an inline alert, not a snackbar**, for filed and failed: it stays
>   until the GM leaves, and its link to the document doesn't vanish. There is no Print button:
>   the browser's PDF viewer prints. Download is `?download=true`, because minimal APIs bind
>   `bool` with `bool.TryParse`, which refuses "1".
> - **Tests.** The filing tests stay in `LibraryPrintTests` (older docs name it); the endpoint
>   tests add an upload that names an Adventure (400 `prints_are_filed_from_prep`).

## Overview

The booklet becomes a PDF that Nornis lays out itself. It takes three pieces, each in one
place:

1. **The booklet model** (Application, `Prep/`): a pure function from the Adventure and its
   `PrintDetails` to typed blocks. Every `PrepMarkup` convention lives here, as it lived in
   `AdventurePrintModel`: the read-aloud, prop and ask boxes, stat blocks, chapters, and
   leaving out unwritten sections. It knows nothing of fonts or pages.
2. **The PDF renderer** (Infrastructure, `Printing/`): lays the model out with QuestPDF. Every
   layout rule lives here: page size, fonts, the two-column flow, and keep-together. It knows
   nothing of Markdown or Adventures.
3. **The printer** (Application, `Prep/AdventureBookletPrinter`): resolves the print details,
   builds the model and calls the renderer. It is the only caller of the other two. The preview
   route and one-click filing both go through it, so the GM files exactly what they previewed
   (pre-implementation check 2).

```
Adventure ──► AdventurePrintDetails.ResolveAsync ──► PrintDetails
     │                                                    │
     └──────────────► BookletModel.Build ◄────────────────┘
                              │ Booklet (typed blocks)
                              ▼
                 IBookletPdfRenderer.Render  (QuestPDF, Infrastructure)
                              │ byte[] (PDF)
              ┌───────────────┴────────────────┐
     GET prep/{id}/booklet            POST prep/{id}/booklet/file
     (preview, download)              (LibraryService.FilePrintAsync)
```

### Why the library, and why not Chrome

Feature 30 recorded the alternative: a separate scale-to-zero container running headless
Chrome. Dave chose the library on 2026-11-03. It adds no new Azure resource, and Chrome's
memory never lands on the API or the Worker.

The cost is that the layout is C#, not CSS. That cost is acceptable for two reasons:

- The HTML booklet is retired, so the C# layout is the only one, not a second copy.
- The browser's layout is what produced the gaps and the hanging bullets. Owning the layout is
  how Requirement 2 is met.

### Why QuestPDF

- QuestPDF 2026.9 has a newspaper-style `MultiColumn` element, which provides the flow, and
  `PreventPageBreak`, which keeps a block whole unless it is taller than the space it starts
  in.
- `EnsureSpace` moves an element on when too little room is left. Table headers repeat on
  every page.
- Its Community License covers individuals and organisations under USD 1M annual revenue.
- MigraDoc (MIT) was the fallback. Its keep-together works per paragraph, and it has no
  column flow, so it does not meet Requirement 2.3.

A spike on 2026-11-03 rendered synthetic Slime Spa-shaped content with the rules below. Dave
saw the sample. It confirmed:

- the flow;
- keep-together for headings and boxes;
- a long box continuing into the next column;
- bullets and checkboxes sitting inside the column;
- the site's fonts loaded from WOFF.

It also found two things this design handles:

- **Fonts.** QuestPDF 2026.9 does not use system fonts. It fails a document on an
  unregistered font family, and by default it fails on any glyph that no registered font has.
- **WOFF.** WOFF files are not picked up by font discovery. They load through
  `FontManager.RegisterFontFromStream`.

## The booklet model

`Nornis.Application.Prep.BookletModel.Build(Adventure, PrintDetails) → Booklet`:

```csharp
public sealed record Booklet(BookletCover Cover, IReadOnlyList<BookletChapter> Chapters);

public sealed record BookletCover(
    string Title, string? SystemLine, string? LevelsLine, string AuthorLine,
    string? Pitch, string CreditsLine);

// Title is null for the sections before the first Chapter.
public sealed record BookletChapter(string? Title, IReadOnlyList<BookletBlock> Blocks);

public abstract record BookletBlock;
public sealed record SectionHeading(string Text, AdventureSectionKind Kind) : BookletBlock;
public sealed record Heading(int Level, IReadOnlyList<BookletInline> Text) : BookletBlock;  // ## to ######
public sealed record Paragraph(IReadOnlyList<BookletInline> Text) : BookletBlock;
public sealed record BookletList(ListStyle Style, int Start, IReadOnlyList<ListItem> Items) : BookletBlock;
public sealed record ListItem(TaskState Task, IReadOnlyList<BookletBlock> Blocks);       // Task: None, Open, Done
public sealed record Table(IReadOnlyList<TableCell> Header, IReadOnlyList<IReadOnlyList<TableCell>> Rows) : BookletBlock;
public sealed record Box(BoxKind Kind, IReadOnlyList<BookletBlock> Blocks) : BookletBlock; // ReadAloud, Prop, Ask
public sealed record StatBlock(IReadOnlyList<StatEntry> Entries) : BookletBlock;
public sealed record CodeBlock(string Text) : BookletBlock;
public sealed record Rule : BookletBlock;

public abstract record BookletInline;
public sealed record Run(string Text, RunStyle Style) : BookletInline;   // Bold | Italic | Strike | Code flags
public sealed record Break : BookletInline;
```

### Rules

- **Pipeline.** Markdig, with pipe tables, emphasis extras, task lists and
  `UseSoftlineBreakAsHardlineBreak`, and with HTML disabled. The soft-break option is what the
  Web's `MarkdownRenderer` uses, so a newline means the same thing in the editor and in the
  booklet (Requirement 3.1).
  - That convention spans a deploy boundary: Web cannot reference Application. A mirror test
    reads both pipeline builders and fails if one has the option and the other does not.
- **Boxes.** The marker logic moves unchanged from `AdventurePrintModel`:
  - `MarkQuote` reads the first line;
  - `[!PROP]` makes a Prop and `[!ASK]` makes an Ask, with the marker removed;
  - any other quote is ReadAloud.
- **Stat blocks.** The `statblock` fence keeps its label/value parse, unchanged.
- **Chapters and sections.**
  - A Chapter section starts a new `BookletChapter`. Its body's blocks follow its title.
  - Every other section adds a `SectionHeading` and then its blocks.
  - Unwritten non-Chapter sections are skipped (`AdventureSkeleton.IsUnfilled`).
- **Cover.** Feature 32's cover lines move here as strings: system and edition joined, "Levels
  …", "By …", and the credits joined by " · ".
- **Images** become a `Run` of their alt text (Requirement 3.4). **Links** become their text.
- **Raw HTML** is already disabled, so it arrives as literal text (Requirement 3.5).
- **Headings inside a body** (`#` to `######`) become `Heading` levels 1 to 6. The renderer
  sizes levels 1 to 3, and draws 4 to 6 like 3.

## The PDF renderer

`IBookletPdfRenderer` (Application) has one method:

```csharp
byte[] Render(Booklet booklet);
```

`QuestPdfBookletRenderer` (Infrastructure, `Printing/`) implements it. QuestPDF is referenced
by Infrastructure only, and a boundary test enforces it.

### Page

- US Letter, 0.6 in margins.
- The footer is centred: the adventure title, a middle dot, and the page number. The cover has
  no footer.
- Body text is Newsreader 10 pt with line height 1.35. Headings and table text are IBM Plex
  Sans. These are the site's own fonts.

### Flow (Requirement 2)

- **Cover.** A page of its own, holding the `BookletCover` lines.
- **Sections before the first Chapter.** A chapter with no title on a new page, laid out like any
  other chapter.
- **Each chapter.** A new page.
  - The chapter title and its rule are in the page header with `ShowOnce`, so they span both
    columns on the first page only.
  - The content is `MultiColumn(2)` with a 0.3 in gap, a hairline rule as the spacer, and
    `BalanceHeight(false)`.
- **Block spacing.** 6 pt between blocks. A list gets the same spacing after it as a
  paragraph, so the paragraph that follows reads as its own.

### Keep-together

| Element | Rule |
| ------- | ---- |
| `SectionHeading`, `Heading` | `EnsureSpace(KeepWithNextPt)`. A heading with less than about three lines of room left moves on. The heading and its first block are also wrapped together in `PreventPageBreak` when that block is small (below). |
| `Box`, `StatBlock` | `PreventPageBreak` when small, so it moves whole to the next column. Otherwise it flows and splits, so a box longer than a column never leaves one empty. |
| `Table` | The header repeats in each column. Each row is `PreventPageBreak`. |
| Paragraph, list item | Flow freely. Short paragraphs (under `KeepWholeLines`) are kept whole. |

"Small" is an estimate made before layout, because QuestPDF cannot report height before
placing:

- **Boxes:** text length up to `KeepWholeMaxChars` = 1,200 characters, about half a column.
- **Stat blocks:** up to 24 entries.

Both constants live in the renderer. Their tests pin the behaviour on each side of the line.

### Lists

- A row with a fixed-width marker cell and a relative content cell, so the marker sits inside
  the column (Requirement 2.8).
- The markers are "•" for bullets, "1." for numbers, and a drawn 8 pt square for a task, with
  a tick for a done one.
- Nested lists indent 12 pt per level.

### Fonts

Infrastructure embeds the fonts, which are registered once on first render:

- Newsreader 400, 400 italic, 600 and 600 italic.
- IBM Plex Sans 400, 400 italic, 600 and 600 italic.
- The Latin subsets of each, as WOFF from `@fontsource` 5.3.0.
- The fallback is DejaVu Sans, plus DejaVu Sans Mono for code.

Every text style lists the fallback after its own family. `Settings.ThrowOnMissingTextGlyphs`
is false, so a glyph no font has draws as a blank instead of failing the document
(Requirement 3.6). The OFL and DejaVu licence texts sit beside the files.

The fonts are embedded resources, not files on disk, so the build output and the container
image need no font directory. `UseSystemFonts` stays false, so the output does not depend on
what the image has installed.

### License setting

`QuestPDF.Settings.License = LicenseType.Community` is set in the renderer's static
initialiser. It is the one place QuestPDF is configured.

## API

| Route | Who | Returns |
| ----- | --- | ------- |
| `GET api/worlds/{w}/prep/{id}/booklet` | GM | `application/pdf`, `Content-Disposition: inline; filename="{title}.pdf"` |
| `POST api/worlds/{w}/prep/{id}/booklet/file` | GM | `201`, the `LibraryDocumentResponse` of the filed print |

Both join the 403 sweep. The old `GET prep/{id}/print`, which returned the HTML booklet, is
removed in Phase B, together with `AdventurePrintResponse`.

`AdventureService.GetBookletAsync(worldId, adventureId, role, ct)` checks the GM, loads the
Adventure (404 for another world's) and calls the printer.

## One-click filing

`LibraryService.FilePrintAsync(FilePrintCommand(WorldId, ActingUserId, Role, AdventureId), ct)`:

1. A non-GM gets 403. An Adventure that is missing or in another world gets 404.
2. The printer renders the PDF. A renderer exception is logged and returned as 500
   `print_failed`, with nothing stored.
3. If the PDF is larger than `LibraryOptions.MaxUploadSizeBytes`, the result is 400 and
   nothing is stored (Requirement 4.6).
4. A `LibraryDocument` is created in `PendingUpload`. This is the same shape the upload path
   made:
   - a Handout, GM-only, titled `PrintTitle(title, now)`;
   - `AdventureId` and `PrintDetails` set;
   - file name `{slug}.pdf`.
5. The PDF is uploaded to the document's blob path.
6. The document is finished by the same private method the upload confirm now uses
   (`CompleteStoredAsync`). That method sets the size, sets the status (with indexing queued
   for a PDF, as for any PDF until feature 31's Phase H), and records the Adventure's last
   print.

Steps 4 to 6 keep the upload path's failure behaviour. If the blob upload or the finish fails,
the row is left in `PendingUpload`, and the existing `PendingUploadSweeper` removes it and any
blob.

### One writer of a print's details

`RequestUploadAsync` loses its `AdventureId` branch. A Library upload that names an Adventure
gets 400 `prints_are_filed_from_prep` (Requirement 4.7).

`PrintDetails` and `AdventureId` on a `LibraryDocument` are then set in one place,
`FilePrintAsync`. Feature 32's `PrepBoundaryTests` scan moves to that method. Retiring the
upload path removes the second caller rather than sharing code with it.

The Web's File to Library dialog keeps ordinary uploads, but loses its `AdventureId` and
`PrintTitle` parameters.

## Web

The print page (`/prep/{key}/print`):

- **Header.** The breadcrumb and a back link, as now. The title and slug come from the
  Adventure the page already loads.
- **Toolbar.**
  - **Download** is a link to the booklet proxy with `?download=1`.
  - **File to Library** is one button. It is disabled while filing.
  - When filing succeeds, a snackbar says "Filed to the Library" and links to the document.
  - When filing fails, the snackbar shows the API's message.
- **Body.** An `<iframe>` of the booklet proxy, filling the page's width at a sensible height.
  Under it is an **Open PDF** link, for browsers that do not show PDFs inline (iOS Safari
  shows only the first page).

The booklet proxy is a Web endpoint, `GET /prep-booklets/{worldId}/{adventureId}`:

- It calls the API with the user's token and returns the bytes.
- It sets `Cache-Control: private, no-store`, because the booklet changes with every edit.
- It sets the content disposition from `download`.
- It needs authorisation, like `/library-covers`.

Retired in Web:

- `AdventurePrintDto`;
- the booklet CSS (the "Prep booklet" block in `app.css`, including the `@page` rules);
- `GetAdventurePrintAsync`;
- the print parameters of the upload dialog.

## Null, empty and zero (check 3)

- **An Adventure with no written sections** gives the cover and one blank page per Chapter
  title. That is the same as today. It is not an error, because a GM may print the outline.
- **Empty print details** (`Levels` null, and so on) leave their cover lines out, as in
  feature 32.
- **An empty table cell** prints empty. **An empty list item** prints its marker alone.

## Bounds (check 4)

- **Input.** An Adventure is bounded by `PrepOptions`: `MaxSections` 200, `MaxSectionChars`
  20,000 and `MaxAdventureChars` 97,500. A maximum-size fixture renders in the tests, and its
  page count and byte size are asserted to stay under the Library limit.
- **Time.** Rendering is synchronous and CPU-bound on the request thread. It is bounded by the
  same input limits. The duration is logged (`booklet_rendered`: pages, bytes, ms). There is no
  queue, because there is one GM and one click.
- **Output.** `MaxUploadSizeBytes` (200 MB) is far above any booklet. It is checked anyway,
  because a check that cannot fire on today's data still states the contract.
- **Fonts.** Eight WOFF files of roughly 30 KB each, plus the two DejaVu files. They are loaded
  once per process.

## Pre-implementation checks

1. **Where each rule lives.**
   - Markup conventions live in `BookletModel`.
   - Layout lives in `QuestPdfBookletRenderer`.
   - The order of resolve, build and render lives in `AdventureBookletPrinter`.
   - The shape of a print lives in `FilePrintAsync`.
   - The newline convention is shared with Web by a mirror test.
2. **Second caller.** Preview and filing share the printer. The upload path for prints is
   removed, not shared.
3. **Null, empty and zero.** See above.
4. **Unbounded.** See Bounds. No URL is fetched (images become their alt text), so there is no
   external wait.
5. **Shown failing.** Each guard in Testing gets a sabotage pass before its PR.

## Testing

- **`BookletModelTests`** (Application). These are the old `AdventurePrintModelTests`
  conventions, re-pinned on the model:
  - boxes by marker;
  - the stat block parse;
  - chapters and unfilled sections;
  - the cover lines (feature 32's tests moved);
  - a newline as a `Break`;
  - task items;
  - an image as alt text;
  - raw HTML as text.
- **`QuestPdfBookletRendererTests`** (Infrastructure). These use PdfPig, already a dependency,
  to read text and positions back out of the PDF:
  - The reading order matches the model order (flow).
  - A heading is never the last line of a column.
  - A small box near a column's foot lands whole in the next column.
  - A box over the threshold starts where it falls.
  - A table header repeats.
  - Each bullet's x lies inside its column.
  - An emoji renders without throwing.
  - The maximum-size fixture stays under the bound.
  - The cover is page 1 alone.
  - A Chapter starts a page.
- **`NewlineConventionMirrorTests`**: the Web and Application pipelines agree on soft breaks.
- **`LibraryFilePrintTests`** (Application):
  - GM only, and 404 across worlds;
  - the stored shape (Handout, GM-only, title, `AdventureId`, `PrintDetails`);
  - the blob holds the rendered bytes;
  - the last print is recorded;
  - a render failure stores nothing;
  - over the size limit stores nothing;
  - a renderer stub proves the printer is the path.
- **`LibraryPrintTests`**: an upload naming an Adventure is refused.
- **`PrepBoundaryTests`**:
  - one writer of `PrintDetails` and `AdventureId`;
  - QuestPDF only in Infrastructure;
  - `IBookletPdfRenderer` called only by the printer.
- **Endpoint tests**: the booklet route returns `application/pdf` to a GM and 403 to a player;
  file returns 201. Both are in the 403 sweep.
- **Web (bUnit)**:
  - the print page shows the iframe at the proxy URL;
  - Download carries `download=1`;
  - File to Library posts once and shows the link;
  - a failure shows the message.
- **The container.** QuestPDF ships a native library. The renderer tests run on the Linux CI
  runner, and Phase B also renders a booklet inside the `api` Docker target before merge. The
  aspnet base image is what production runs.
