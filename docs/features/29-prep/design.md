# Design Document

> **Amendment (2026-11-02, Phase D):** what building Print and Export changed. The text below
> is left as written.
>
> - **The server sets a print's filing, not the Web.** When `RequestUploadAsync` carries an
>   `AdventureId`, the service checks the GM and the same world (403 and 404), then sets the
>   title to "{Adventure title}, printed {yyyy-MM-dd}" (cut to 200 characters), `Kind` to
>   `Handout` and `Visibility` to GMOnly, whatever the request said. The Web shows the same
>   title (the print route returns it as `LibraryTitle`) and offers only a PDF.
> - **File to Library reuses the Library's upload dialog** in a print mode: no title, kind or
>   visibility fields, a hint to choose *Save as PDF* in the print dialog, and `.pdf` only.
> - **Unwritten sections are left out** of both the booklet and the export, so an untouched
>   scene template never prints or reaches extraction. A Chapter always prints, as its own
>   heading. An export with nothing written is refused with 400 `nothing_to_export`.
> - **The export's filing line cuts each title to 100 characters**, and the source title is
>   "{Adventure} (prep)" or "{Adventure}: {Chapter} (prep)", within the 200-character bound.
>   The answer is `{sourceId, title, slug}`, so the Web links by slug.
> - **A chapter export is the Chapter and the sections after it up to the next Chapter.** A
>   `chapterSectionId` that is not a Chapter of this Adventure is a 404.
> - **The source page names an export's Adventure.** `SourceResponse` gains `AdventureId`; the
>   source page shows a "Prep" line linking back to it for the GM, or that it was deleted. The
>   sources ledger labels the type "Adventure Prep".
> - **"Printed from Prep" is shown to the GM only**, on the Library list and the document
>   page, because a player can't open Prep. The share confirmation applies to any document
>   with an `AdventureId`, from either page.
> - **The booklet stays paper in dark mode.** It is the printed page, so its colours are the
>   paper theme's own, not the palette's variables. On screen it sits as a page on the desk;
>   in print the drawer, top bar, footer, tutorial and toolbar are hidden.

> **Amendment (2026-11-02, Phase C):** what building drafting changed. The text below is left
> as written.
>
> - **`DraftAsync` returns `AppResult<IAsyncEnumerable<DraftEvent>>`**, as streaming Ask does,
>   not a bare stream. Everything that can refuse a draft (role, a stale stamp, a held gate, the
>   budget, an over-long instruction) answers with its status code before the first byte; only
>   what happens after the model starts rides in-band as an `error` event.
> - **A draft is saved only when it ends cleanly.** The stream's last chunk now says whether
>   the output ceiling cut the text off (`AiStreamChunk.CutOff`, from finish reason `Length`).
>   A cut-off draft, an empty one, or one that would break a Prep bound is refused with nothing
>   written. Usage is recorded for all of them; a refusal after a full answer counts as a
>   provider success, a GM stopping counts as `Cancelled`. The Web keeps the streamed text on
>   screen after a refusal so it can still be read or copied.
> - **An edit while a draft streams wins.** The accept is conditional on the stamp the draft
>   started from, so a save in between makes the draft a 409 and keeps the GM's text. The page
>   saves what was typed before it starts a draft and holds the editor read-only while one runs.
> - **The gate's lease lapses** after `DraftTimeoutSeconds + 60`, so a stream that is never
>   read can't hold its section until the API restarts.
> - **Retrieval also reads the section's own text** when it has some, and the Adventure's
>   title, as well as the pitch, section title and instruction: a revision of a scene that names
>   someone should bring that someone's record.
> - **An unwritten Overview with no pitch needs an instruction** (400 `instruction_required`),
>   which the Constraints table implies but the flow didn't say.
> - **The budget route is `GET api/worlds/{worldId}/prep-budget`**, not `prep/budget`: every
>   literal under `prep/` is also a slug an Adventure could have.
> - **The ceiling constant is `OutputCeilingTokens`.** `UnsupportedChatParameterTests` now
>   allows `MaxOutputTokenCount` in the drafting client's file only and still bans
>   `Temperature` everywhere; naming the constant `MaxOutputTokens` tripped the same scan.
> - **Usage carries reasoning tokens** (`AiUsage.ReasoningTokens`); the drafting service logs
>   them whenever they are above zero, as task A2 asked.
> - **`Prep:Deployment` names its pricing key too.** A deployment set there needs its own
>   `ModelPricing` entry, or drafting is recorded at $0 and the Prep pool never fills.

> **Amendment (2026-11-02, Phase B):** what building the Adventure changed. The text below is
> left as written.
>
> - **`MaxAdventureChars` is 97,500, measured over titles and bodies.** Requirement 2.5 says
>   100,000, but Export adds a filing line and a `##` heading per section, and the whole
>   Adventure must still fit `SourceService.MaxBodyChars` (100,000). `AdventureSize` holds the
>   overhead (8 characters a section, 300 for the filing line), and `PrepOptionsTests` pins
>   that the largest legal Adventure exports within the Source bound.
> - **Editing an Adventure's fields is `PUT`, not `PATCH`.** The request carries all three
>   fields as their new values, so a null pitch or campaign means "none" and there is no
>   "unchanged" to confuse with it.
> - **"Unfilled" includes an untouched Scene template.** A new Scene's body is the
>   `### Goal / Read-aloud / Likely questions / End state` scaffold, so counting it as written
>   would make every new Adventure look a third done. `AdventureSkeleton.IsUnfilled` is the one
>   rule; the list's count and the section response's `isFilled` both come from it.
> - **The skeleton has three Scenes**, as Requirement 2.3 says, not the worked example's five.
> - **World import has nothing to extend.** No user-facing world import exists in the tree;
>   `IWorldImportWriter` is used only by the demo clone, which maps Source fields one by one,
>   so neither Adventures nor `Source.AdventureId` can reach a demo world. Requirement 9.2's
>   export half ships (`prep.json`: Adventures, sections and revisions; `adventureId` on
>   sources and library documents; `prepMonthlyBudgetUsd` on the world), and
>   `DemoWorldTests` proves 9.3 against a template that carries Prep. The import half waits
>   for a world import to exist.
> - **The nav icon is `HistoryEdu`.** `EditNote` is already Capture's.
> - **Reordering has arrow buttons beside drag**, as the import backlog does, so the outline
>   can be reordered from the keyboard.
> - **A section's unsaved text is kept in the browser** under a per-section key, the
>   capture editor's safety net, and moving to another section saves the one being left. A
>   save that loses to a newer version (409 `section_changed`) offers "Save mine over it" or
>   "Load the saved version"; nothing is overwritten without that choice.

> **Amendment (2026-11-02, Phase A):** three corrections found while starting the build. The
> text below is left as written.
>
> - **The Web can't reference Application.** `Nornis.Web.csproj` deliberately has no project
>   reference, so the Web can't call `AdventurePrintModel` or read `PrepMarkup`. The booklet is
>   rendered by the API instead: `GET api/worlds/{worldId}/prep/{id}/print` (GM) returns the
>   HTML `AdventurePrintModel` builds, and `/prep/{key}/print` wraps it in the print
>   stylesheet. There is still one renderer, and v3.1's worker calls the same code. The editor
>   toolbar uses a Web mirror of `PrepMarkup` (`Nornis.Web.Services.PrepMarkup`), which
>   `PrepMarkupMirrorTests` pins to the Application file by reading its source.
> - **Markdig moves into Application.** The importer and the print model both parse Markdown
>   and run in the API (later the worker), so `Nornis.Application.csproj` references Markdig at
>   the Web's version. It is a pure parser with no I/O.
> - **A1's result.** TipTap 3.11.0 keeps all of it: the `[!PROP]` and `[!ASK]` lines (as text
>   followed by `<br>`), the `language-statblock` class and pipe tables. The losses were in
>   `HtmlToMarkdownConverter`, which wrote every fence bare (dropping `statblock`), ran a
>   blockquote's paragraphs together, and left each `<br>` line of a quote outside the `> `.
>   All three are fixed there, so the convention stands and the bold-label fallback isn't
>   needed. The paragraph merge affected every multi-paragraph quote in session notes too.

## Overview

The feature adds three tables, one source type, one AI seam, one budget pool, a print view and
two filing paths, Print and Export. Both filing paths end in machinery that already exists:
Print uploads through the Library's two-step upload, and Export creates a Source and hands it
to `MarkReadyAsync`. Nothing downstream of "a Library document exists" or "a source is
Queued" changes.

```text
                 ┌──────────── Prep (GM only) ────────────┐
 pitch / import ─▶ Adventure ── AdventureSection[] (ordered, kinded, Markdown)
                 │                 │  Draft / Revise (one section, streamed)
                 │                 │      IKnowledgeRetriever (GM scope, Hidden included)
                 │                 │      + outline + neighbours ─▶ IAdventureDraftingAiClient
                 │                 │  old body ─▶ AdventureSectionRevision (Undo, last 20)
                 └─────────┬───────┴──────────────┬──────────────────┘
                     Print │                      │ Export
                           ▼                      ▼
     print view ─▶ browser PDF ─▶ File to   Source { Type = AdventureExport, GMOnly,
     LibraryService.RequestUpload/Confirm   AdventureId, Body = snapshot }
     LibraryDocument { Handout, GMOnly,       │ MarkReadyAsync
                       AdventureId }          ▼
           │ SetVisibilityAsync          Extraction ▶ ReviewBatch ▶ accept ▶ canon (GMOnly / Hidden)
           ▼ (warns)                                                 ▶ Reveal (feature 17)
     party shelf (when the GM chooses)
```

## Where each rule lives (pre-implementation check 1)

| Rule | The one place |
| --- | --- |
| Who may use Prep | `AdventureService`: every operation checks that the caller's current role is GM, using the same role resolution every GM-gated service uses. Controllers carry no role logic of their own. |
| The house skeleton | `AdventureSkeleton` (Application, static), which Create uses. Nothing else builds sections from scratch. |
| Kinds and what each drafts | `AdventureSectionKind` (Domain enum) plus `AdventureDraftPrompt` (Application), the one map from kind to prompt focus. |
| The markup convention | `PrepMarkup` (Application, static). It holds the three markers (`[!PROP]`, `[!ASK]`, `statblock`) as constants, used by the import parser, the print renderer and the prompt that tells the model how to write them. |
| Size bounds | `PrepOptions` (`MaxSections` 200, `MaxSectionChars` 20,000, `MaxAdventureChars` 100,000, `MaxInstructionChars` 2,000, `MaxRevisionsPerSection` 20). `MaxAdventureChars` is checked against `SourceService.MaxBodyChars` by a test, so the two can't drift. |
| How an Adventure becomes a Source body | `AdventureExportComposer` (Application, static). Nothing parses the body back. |
| How a source reaches the queue | `SourceService.MarkReadyAsync`, unchanged. Export creates the row Draft and calls it, as feature 26 does. |
| Which spend pool an operation belongs to | `AiSpendPools` (Application, static): `AdventureDrafting` → Prep pool; everything else → the daily world pool. `AiBudgetGuard` and `AiUsageRecordRepository` both read it. The daily query doesn't hard-code an exclusion. |
| Visibility of a print | `LibraryService.SetVisibilityAsync`, unchanged, plus a confirm in the Web when the document has an `AdventureId`. The server needs no special case, because only a GM can call it. |

## What null, zero and empty mean (pre-implementation check 3)

| Value | Meaning |
| --- | --- |
| `World.PrepMonthlyBudgetUsd` null | Inherit `AiBudgetOptions.PrepMonthlyBudgetUsd` (default $10.00). |
| `World.PrepMonthlyBudgetUsd` 0 | Drafting is blocked. A zero is a ceiling of zero, as the guard already reads it. |
| `AiBudgetOptions.PrepMonthlyBudgetUsd` null | No configured ceiling: spend freely, matching how a null daily default reads today. |
| `Adventure.CampaignId` null | World-level prep, attached to no campaign. |
| `Adventure.Pitch` null or empty | No pitch. Drafting the Overview then needs an instruction. |
| `Adventure.LastPrintedDocumentId` null | Never printed, or the print was deleted from the Library. |
| `Source.AdventureId` null on an `AdventureExport` | The Adventure was deleted. The snapshot stands on its own. Always null on every other type. |
| `LibraryDocument.AdventureId` null | Not a Prep print, or its Adventure was deleted. |
| `AdventureSection.BodyMarkdown` empty | An unfilled section. **Draft** is offered and **Revise** is not. |
| `AdventureSectionRevision.Instruction` null | The revision was saved before a plain Draft, which has no instruction. |

## What is bounded (pre-implementation check 4)

- Sections, section size, Adventure size and instruction size: `PrepOptions`, enforced on
  every write (create, import, save, accept of a draft).
- Revisions: at most 20 per section. The insert trims the oldest in the same transaction.
- Drafting output: `MaxOutputTokens` 2,500 on the drafting client, set only after one real
  call confirms the OpenAI SDK sends `max_completion_tokens`. That is the open gap from the
  July outage; task A2 makes the call first.
- Drafting wait: `PrepOptions.DraftTimeoutSeconds` (90), an application timeout like every
  other AI path's.
- Concurrency: one draft in flight per section, held by an in-memory per-section gate
  (single API replica; a second request gets 409). A per-row lease isn't needed, because a
  lost gate only loses an in-flight draft, never data.
- Spend: the monthly Prep pool, on top of the global pause.

## Domain

```csharp
Adventure : ISlugged
- Id: Guid
- WorldId: Guid
- CampaignId: Guid?            // FK Restrict, detached on campaign delete
- Title: string                // max 200
- Slug: string?                // ISlugged; per-world, assigned once
- Pitch: string?               // max 4,000
- LastPrintedDocumentId: Guid? // FK Restrict, detached on document delete
- CreatedByUserId: Guid
- CreatedAt, UpdatedAt: DateTimeOffset

AdventureSection
- Id: Guid
- AdventureId: Guid            // cascade
- Kind: AdventureSectionKind   // Overview, SessionClock, Chapter, Scene, QuickReference, Notes
- Title: string                // max 200
- BodyMarkdown: string         // max PrepOptions.MaxSectionChars
- SortOrder: int
- UpdatedAt: DateTimeOffset    // concurrency token for draft-vs-edit

AdventureSectionRevision
- Id: Guid
- SectionId: Guid              // cascade
- BodyMarkdown: string         // the body as it was before the AI write
- Instruction: string?         // what the GM asked for, null for a plain Draft
- Model: string                // snapshot, as CampaignRecap does
- CreatedAt: DateTimeOffset

SourceType
+ AdventureExport              // GM's own prep filed as a source; extracted like any source

Source
+ AdventureId: Guid?           // FK Restrict; null = deleted Adventure (exports) or n/a

LibraryDocument
+ AdventureId: Guid?           // FK Restrict; set on a Prep print

World
+ PrepMonthlyBudgetUsd: decimal?

AiOperationType
+ AdventureDrafting
```

An Adventure carries no `Visibility` column. A column that could hold `PartyVisible` would be
a guard that must never fire, and leaving it out makes "GM-only" a fact of the schema rather
than a convention.

The new foreign keys are all Restrict, for the reason `Campaign` and `LibraryDocument` are:
SQL Server refuses a second cascade path from `Worlds`. Each delete detaches its dependents
with `SetWhereAsync` before deleting, as `LibraryDocumentRepository.DeleteAsync` already does
for excerpts.

Migration `AddPrep`: additive. It adds three tables, nullable columns on `Source`,
`LibraryDocument` and `World`, and their indexes. It is applied by hand before deploy.

## Application

### `IAdventureService`

```csharp
Task<AppResult<IReadOnlyList<AdventureListItem>>> ListAsync(WorldId, Actor, ct)
Task<AppResult<Adventure>> GetAsync(WorldId, key, Actor, ct)                  // slug or id
Task<AppResult<Adventure>> CreateAsync(CreateAdventureCommand, ct)            // from the skeleton
Task<AppResult<Adventure>> ImportAsync(ImportAdventureCommand, ct)            // Markdown files
Task<AppResult<Adventure>> UpdateAsync(UpdateAdventureCommand, ct)            // title, pitch, campaign
Task<AppResult<AdventureSection>> SaveSectionAsync(SaveSectionCommand, ct)    // GM edit
Task<AppResult<Adventure>> ReorderAsync(ReorderSectionsCommand, ct)
Task<AppResult> AddSectionAsync / DeleteSectionAsync / DeleteAsync(...)
Task<AppResult<AdventureSection>> UndoAsync(UndoSectionCommand, ct)           // latest revision
```

Each operation resolves the world role first and returns 403 for anything but GM. That
includes a view-as-player request, which arrives with an effective role of Player.

**Import** goes through `AdventureMarkdownImporter` (Application, static, Markdig). It splits
on `#` blocks. The first file's first `#` text is the title; a later `#` equal to it, ignoring
case and whitespace, starts a Chapter section whose title is the next `##` line, when there
is one. A `#` that starts "Scene" becomes a Scene, and anything else becomes Notes. Bodies keep
their Markdown byte for byte, apart from the heading line moved into `Title`.

### Drafting

```csharp
IAdventureDraftingService
  IAsyncEnumerable<DraftEvent> DraftAsync(DraftSectionCommand, ct)
    // DraftSectionCommand(WorldId, AdventureId, SectionId, Actor, BasedOnUpdatedAt, Instruction?)

IAdventureDraftingAiClient                     // Application/Ai; transport in Infrastructure
  IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest, ct)
```

`DraftAsync` runs in this order:

1. GM check, then a 409 if `BasedOnUpdatedAt` isn't the section's `UpdatedAt` or the section's
   gate is held.
2. `AiBudgetGuard.CheckPrepAsync(worldId)`, which checks the pause and the monthly pool.
3. Build the prompt with `AdventureDraftPrompt`. The order keeps the cacheable prefix first
   and stable:
   - system instructions, including `PrepMarkup`'s conventions;
   - the world context from `IKnowledgeRetriever.RetrieveAsync` at the GM's role, with the
     retrieval text being the pitch plus the section title plus the instruction, so Hidden
     facts are included because the asker is the GM (the rule lives in the retriever, not
     here);
   - the outline;
   - Overview and pitch;
   - the previous and next sections;
   - the section and the instruction.
4. Stream chunks to the caller as SSE, the same transport as streaming Ask.
5. On completion: in one transaction, insert the revision with the old body, trim to 20, write
   the new body and bump `UpdatedAt`. Then record usage under `AdventureDrafting`, including
   `CachedInputTokens`.
6. On cancel or failure, write nothing to the section. Usage is still recorded at about 4
   characters per token, as cancelled Ask streams are.

The client sits on the Loremaster's `IChatClient` (deployment `nornis-ask`, gpt-5.4) by
default, with `Prep:Deployment` to move it, and its pricing is looked up from the model name
as every other client's is. Prompt text lives in Application and transport in Infrastructure,
as for the other fourteen seams.

### Budget

`AiBudgetGuard` gains `GetPrepStatusAsync` and `CheckPrepAsync`, shaped like the public-Ask
pair: the month-to-date sum of `AdventureDrafting` rows for the world, against
`World.PrepMonthlyBudgetUsd ?? options.PrepMonthlyBudgetUsd`. `GetStatusAsync` (daily) sums
only the operations `AiSpendPools` puts in the daily pool. `CheckAsync` doesn't run for
drafting, so the daily cap never blocks Prep and Prep never fills it. `WorldService` validates
the new setting like the public-Ask one: 0.01 to 1,000, or null.

### Print and File to Library

`AdventurePrintModel` (Application) renders the sections to HTML through Markdig with one
extension that rewrites the three markers into classed elements (`read-aloud`, `prop`, `ask`,
`statblock`). The Web's print page and v3.1's worker renderer share it, so the booklet looks
the same either way. **File to Library** is the existing `RequestUploadAsync` →
browser PUT → `ConfirmUploadAsync`, with `Kind = Handout`, `Visibility = GMOnly`, a title of
"{Adventure title}, printed {yyyy-MM-dd}" and `AdventureId` on the command. Confirm then sets
`Adventure.LastPrintedDocumentId`. Indexing proceeds as for any PDF, so the GM's Ask can quote
the booklet, and a party's Ask can too once the GM shares it, which is what sharing means.

### Export

`AdventureExportService.ExportAsync(worldId, adventureId, chapterSectionId?, actor)` runs as
follows:

1. GM check.
2. Compose with `AdventureExportComposer`. Line 1 is a filing line, `Adventure "Everybody Dies
   at the Slurping Spa", exported 2026-11-14 by the GM as prep`, then each section as `##
   {Title}` and its body. Chapters become `#`.
3. Refuse with 400 `export_too_large` over `SourceService.MaxBodyChars`. This can only happen
   for a whole-Adventure export if the bounds drift, and a test pins that they don't.
4. Create the `Source` (Draft, `AdventureExport`, GMOnly, the Adventure's campaign,
   `ExtractionEnabled`, `AdventureId`, no `OccurredAt`).
5. Call `MarkReadyAsync`.

`ExtractionPromptBuilder` gains a `## Adventure Prep` section for this type, saying:

- This is the GM's own prep, so it is authoritative about the world.
- Extract people, places, factions, items, relationships and secrets.
- State what the text marks as hidden from players, or as the twist, as `Hidden` truth.
- Describe the world as it stands at the start of the adventure.
- Never propose scenes, beats or outcomes as Events. They are plans, and session notes record
  what happened.
- Facts default to `Confirmed`, like an excerpt.
- Stat blocks may be quoted onto the artifact they describe.

Like an excerpt, an export retrieves no reference passages: the source is the reference.

## API

```text
GET    api/worlds/{worldId}/prep                                   GM → list
POST   api/worlds/{worldId}/prep                                   GM {title, pitch?, campaignId?} → adventure
POST   api/worlds/{worldId}/prep/import                            GM {files: [{name, markdown}]} → adventure
GET    api/worlds/{worldId}/prep/{key}                             GM → adventure + sections
PATCH  api/worlds/{worldId}/prep/{id}                              GM {title?, pitch?, campaignId?}
DELETE api/worlds/{worldId}/prep/{id}                              GM
PUT    api/worlds/{worldId}/prep/{id}/sections/{sectionId}         GM {kind, title, body, basedOnUpdatedAt}
POST   api/worlds/{worldId}/prep/{id}/sections                     GM {kind, title, afterSectionId?}
DELETE api/worlds/{worldId}/prep/{id}/sections/{sectionId}         GM
PUT    api/worlds/{worldId}/prep/{id}/order                        GM {sectionIds[]}
POST   api/worlds/{worldId}/prep/{id}/sections/{sectionId}/draft   GM {instruction?, basedOnUpdatedAt} → SSE
POST   api/worlds/{worldId}/prep/{id}/sections/{sectionId}/undo    GM
POST   api/worlds/{worldId}/prep/{id}/export                       GM {chapterSectionId?} → {sourceId, title}
GET    api/worlds/{worldId}/prep/budget                            GM → {spentThisMonthUsd, monthlyBudgetUsd}
```

All endpoints live on `PrepController`, behind `WorldMemberActionFilter`. The draft endpoint
uses the SSE writer the streaming Ask endpoint uses. The Library upload command gains an
optional `adventureId`, which must name an Adventure of the same world (404 otherwise).
`WorldResponse` and `UpdateWorldRequest` gain `PrepMonthlyBudgetUsd`.

## Web

- **Nav.** `NavGroups`: `new("Prep", "/prep", Icons.Material.Outlined.EditNote …)` first in
  the GM group. Prep needs no `GmOnly` of its own, because the group already has it.
  `TutorialGuide` derives step locations from the sidebar, so it picks this up without
  changes. `ReachabilityTests` gains the two routes.
- **`/prep`**: the Adventures as cards (title, campaign, sections filled out of total, last
  printed), with **New adventure**, **Import Markdown** and the month's Prep spend against
  its cap.
- **`/prep/{key}`**: the outline on the left (drag to reorder, kind icon, filled or empty),
  and the selected section in the existing TipTap editor on the right. The toolbar adds
  Read-aloud, Prop, Ask and Stat block. Under the editor sit the instruction box with
  **Draft** or **Revise**, and **Undo** with the revision count. The page head holds
  **Print**, **Export to Nornis** and **Delete**.
- **`/prep/{key}/print`**: the print view, a `@media print` stylesheet in Nornis's paper
  theme. Newsreader for the body and IBM Plex Sans for labels, as feature 24 set. The page
  has two columns and chapter page breaks. Read-aloud gets a ruled box with a terracotta rule,
  props a parchment panel, questions a bold lead, and stat blocks a framed two-column grid of
  labels and values. The page has **Print** (`window.print()`) and **File to Library**.
- **Library.** A document with an `AdventureId` shows "Printed from Prep" with a link. Its
  visibility chip asks for confirmation before PartyVisible: "This booklet was drafted with
  your world's Hidden truth and may contain it. Players will be able to read all of it."
- **Settings.** The world settings card gets "Prep budget this month", next to the public-Ask
  cap.
- **Editor round-trip.** `HtmlToMarkdownConverter` must keep three things: `[!PROP]` and
  `[!ASK]` as the first line of a blockquote, with the brackets unescaped; a fenced block's
  `statblock` info string; and pipe tables. Task A1 proves this before anything else is built.
  If TipTap can't hold the alert line losslessly, the fallback convention is a bold first-line
  label (`> **Prop.**`, `> **Ask.**`), decided there, and `PrepMarkup` is the one place that
  changes.

## Cost

For gpt-5.4 at $2.50 per 1M input tokens and $15.00 per 1M output tokens (`appsettings.json`):

| Item | Value |
| --- | --- |
| One section call | ~15k tokens in (instructions 1.5k, world context 8k at `MaxContextTokens`, outline and neighbours 4k, section 1.5k), ~1.2k tokens out |
| Cost per call | $0.056 uncached; about $0.033 if the ~10k-token stable prefix is billed at a 90% cache discount (confirm on the pricing blade) |
| A spa-sized Adventure | ~25 sections, each drafted once and revised about twice, so ~75 calls |
| **Per Adventure** | **about $3 to $5** |
| Export | one ordinary extraction of ~55k characters, metered as `SourceExtraction` in the daily pool, the same as importing a long note |

A $10 default covers two or three Adventures a month. All of Nornis's AI spend today is about
$13 a month.

## Testing strategy

- **`PrepMarkupRoundTripTests` (Web)**: each marker, a stat block and a pipe table survive
  Markdown → TipTap HTML → `HtmlToMarkdownConverter` unchanged. **Sabotage:** escape the
  bracket in the converter and the `[!PROP]` case goes red.
- **`AdventureMarkdownImporterTests`**: the synthetic fixture (below) imports into the
  expected kinds and titles; a repeated title starts a Chapter; bodies are preserved byte for
  byte; over-bound input is refused.
- **`AdventureServiceTests`**: Player, Observer and view-as-player are refused on every
  operation; Create lays down the skeleton; bounds hold on save and import; Delete detaches
  prints and exports. **Sabotage:** drop the GM check from `SaveSectionAsync` and the Player
  case goes red.
- **`AdventureDraftingServiceTests`** (fake AI client):
  - completion writes one revision and the new body;
  - cancel writes nothing;
  - a stale `BasedOnUpdatedAt` is a 409;
  - a held gate is a 409;
  - the 21st revision trims the oldest;
  - Undo restores;
  - the prompt holds a Hidden fact for the GM, and the same retrieval at Player role does not
    (the retriever's rule exercised through this caller).
- **`AiBudgetGuardPrepTests`**:
  - null inherits the default;
  - zero blocks;
  - the month boundary resets;
  - drafting spend leaves the daily status untouched;
  - extraction spend leaves the Prep status untouched.

  **Sabotage:** map `AdventureDrafting` to the daily pool in `AiSpendPools` and the
  untouched-daily test goes red.
- **`AdventureExportComposerTests`** and **`AdventureExportServiceTests`**: the filing line,
  chapter-only export, GM-only visibility and campaign, Queued with one queue message, and the
  bound test that pins `MaxAdventureChars <= MaxBodyChars`.
- **`ExtractionPromptBuilderTests`**: the Adventure Prep section is present for
  `AdventureExport` and absent for `SessionNote`, and it names `Hidden` and the no-Events rule.
- **`PrepEndpointTests` (Api)**: Player 403 on every route; GM round-trip; the upload command
  rejects an `adventureId` from another world.
- **World lifecycle**: `WorldDeletionServiceTests` covers the three tables, and the export
  round-trip carries an Adventure.
- **Fixture**: `tests/Nornis.Application.Tests/Fixtures/Prep/two-chapters.md`, about 300
  words of original text with one of each shape (read-aloud, `[!PROP]`, `[!ASK]`,
  `statblock`, a pipe table, a scene with the four subheadings, and a repeated title starting
  chapter 2). The Slime Spa isn't committed. It lives in project files for the live walk.
- **Live walk**, as GM in *Gribbits Detective Agency* (production data, deployed worker):
  1. Import the five spa files.
  2. Check the five chapters and the 211 read-aloud lines.
  3. Revise one scene, then Undo it.
  4. Print and file to the Library.
  5. Share the print and see the warning.
  6. Export, and confirm the batch reaches Review with the cast and places and no Events.

  Acceptance is left to the GM.

## Design decisions

1. **The skeleton is a template, not a schema.** A fixed schema breaks on the first
   adventure that doesn't fit it. Kinds give the prompt what it needs, and the GM keeps the
   shape. A Scene's four parts are subheadings, not columns, so one editor stays one editor.
2. **Chapters are a section kind, not a parent table.** Reordering stays one `SortOrder`
   column, like `Campaign.SortOrder`.
3. **No visibility on an Adventure, and a print starts GM-only.** The booklet holds the
   answers next to the read-aloud. Sharing is a decision taken per print, with a warning,
   through the Library's existing visibility switch.
4. **Every print is a new document.** `LibraryDocument` is immutable. Replacing its blob
   would break that quietly, so a print keeps its date, and older prints stay until the GM
   deletes them.
5. **Export is a Source, the way an excerpt is.** Provenance, review, dedup, reveal and Ask
   already handle a source and nothing else. A second way into canon would mean touching all
   of them, and it would be the first thing to bypass review.
6. **Planned is not happened.** The export prompt keeps scenes out of Events, so canon
   doesn't record a fight that never took place. Session notes stay the record of play.
7. **Revisions only for AI writes.** The AI overwriting the GM's text is the one destructive
   event here. GM keystrokes already have the unsaved-edit backup, and a revision per save
   would be history nobody reads.
8. **A separate spend pool, defined in one place.** Prep spend is lumpy and discretionary,
   and extraction is neither. Sharing a $2 day would let one evening of prep stop that night's
   notes. `AiSpendPools` keeps the membership as one rule, not as an exclusion buried in a
   query.
9. **No side conversation in v3.0.** The instruction box, saved on each revision, is enough
   to write the spa. v3.1 builds the `Conversation` that `domain-model.md` already describes,
   with an `AdventureId`, rather than inventing a new noun.
10. **One booklet renderer.** `AdventurePrintModel` serves the browser view now and the
    worker's PDF in v3.1, so the two can't drift.
