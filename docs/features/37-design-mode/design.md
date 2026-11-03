# Design Document

> **Amendment (2026-11-03, Phases B and C):** what building it changed. The text below is left
> as written.
>
> - **Bounds.** The draft's cap is `Adventure.MaxDraftChars` (60,000) and the brief's
>   `Adventure.MaxTableBriefChars` (2,000), constants on the entity rather than `PrepOptions`;
>   the Web mirrors them and `DesignModeMirrorTests` pins them. Draft revisions use
>   `PrepOptions.MaxRevisionsPerSection`.
> - **`AdventureSourceBook.AdventureId` is `Restrict`, not cascade.** Worlds cascade to both
>   `Adventures` and `LibraryDocuments`, so SQL Server refuses a second cascading path.
>   `AdventureRepository.DeleteAsync` and the world graph delete the rows themselves. The book
>   side cascades, which covers every way a book leaves.
> - **A controller of its own.** The draft, edits, split, gather, brief and source-book routes
>   are on `PrepDesignController`; the conversation's scope stays on `PrepController`. Put in
>   draft is `POST draft/put { messageId, mode, basedOnUpdatedAt }`.
> - **Answers carry their edits.** The API reads each answer with `DraftEdits.Parse` as it is
>   shown and sends `edits` and `prose` (the answer without its fences) on the message. The Web
>   never parses a fence.
> - **The chosen-books retriever** is a default interface method,
>   `IReferencePassageRetriever.RetrieveFromDocumentsAsync`, which filters the GM-scope search to
>   the chosen uploads; the Bibliotaph retriever overrides it to send the books' entry ids.
> - **The plan** is `POST draft/plan { useLoremaster }`, returning rows with their opening words
>   (read from the cut itself) and the draft stamp the split must be based on. The Loremaster's
>   plan falls back to the headings plan, with a note, when the call fails as well as when the
>   plan is refused. It is charged to the Prep pool as drafting; it is not on the conversation's
>   gate, so a plan and a turn can run together.
> - **The cut.** Only ATX `#`/`##` headings split; an underlined heading stays text. A start line
>   that is a heading leaves the body only when its text is the plan's title. A heading the GM
>   renamed stays in the body, so a rename can't take a word out of the sections.
> - **Split keeps the draft.** A split never changes the draft or its stamp. Replace keeps the
>   written sections' text as one gathered draft revision (reason `Split`), not one per section;
>   Undo on the draft brings it back into the draft. With nothing written, the cut replaces the
>   empty outline without asking.
> - **Gather keeps chapters.** An unwritten Chapter with written sections under it stays as its
>   heading alone, so a gathered draft splits back into the same sections.
> - **Import into the draft** is on the Design tab: the files are read in the browser and put
>   into the editor after what is there, for the GM to save. The Prep list's Import Markdown
>   still makes sections.
> - **The boundary scans** (`PrepBoundaryTests`) pin the draft's readers (nothing that prints or
>   exports names `DraftMarkdown`) and the cut (`AdventureDraftCutter.Cut` only in the design and
>   plan services, `TryWriteSplitAsync` only in the design service).
> - **For feature 38's AI-use flag:** Split, Gather and Import only structure the GM's text.
>   Applying a suggested edit and Put in draft are the Loremaster writing words.

## Overview

```
Adventure
  DraftMarkdown / DraftUpdatedAt     the design draft (new)
  TableBrief                         (new)
  SourceBooks → LibraryDocument      up to 5 (new join table)
AdventureDraftRevision               the draft's Undo history (new)

Design tab ──────────────► NotesEditor(draft) ◄── Apply suggested edit (server)
     │                                             ▲
     └─ side conversation (scope: draft) ──────────┘  answers carry ```edit fences

Split: plan (headings, or the Loremaster's line numbers) → AdventureDraftCutter (code) → word check → sections
Gather: sections → AdventureDraftGatherer → draft
Every Prep prompt: + table brief + reference passages (source books, or every book at GM scope)
```

## Data model (migration `AddPrepDesignMode`)

- `Adventure.DraftMarkdown` (`nvarchar(max)`, null means no draft), `Adventure.DraftUpdatedAt`
  (`datetimeoffset?`), and `Adventure.TableBrief` (`nvarchar(2000)`, null).
- `AdventureDraftRevision`: `Id`, `AdventureId` (cascade), `BodyMarkdown`, `CreatedAt` and
  `Reason` (`Edit`, `Put`, `Gather`, `Split`, `Import`). It has the same cap as section
  revisions (`PrepOptions.MaxRevisionsPerSection`). Undo restores the newest and deletes it.
- `AdventureSourceBook`: `AdventureId` and `LibraryDocumentId`, both cascade, as the primary
  key. At most five per Adventure, enforced in the service.
- **Why not a section of kind Draft.** Every reader of sections (booklet, export, outline
  counts, the skeleton test, reorder) would then have to skip one kind, and the next reader
  would forget. A draft that lives outside `Sections` can't leak into a print or an export.

## Suggested edits

The conversation prompt in Design mode adds one convention:

````
```edit
<<<
the exact passage, copied from the draft
===
its replacement
>>>
```
````

- `DraftEdits.Parse(answer)` (Application/Prep) is the one reader of these fences. An answer is
  stored as written, and the edits are parsed when it is shown.
- The Web shows each edit as a card: before and after, with Apply.
- `POST prep/{id}/draft/edits` with `{ messageId, index, basedOnUpdatedAt }` re-parses the stored
  message (so the client can't send its own text), finds the passage, and:
  - refuses when the passage is found zero times or more than once (409 `edit_no_longer_fits`);
  - otherwise replaces it, keeps a revision, and returns the draft.
- An empty find (`<<<\n===`) on an empty draft is the whole text.

## Split

- `AdventureDraftPlan`: a list of `{ StartLine, Title, Kind }`. Lines are 1-based in the draft
  with `\n` line ends.
- `AdventureDraftPlanner.FromHeadings(draft)` makes the default plan. A `#` or `##` heading line
  outside a fence starts a section. Text before the first heading becomes an Overview. Kinds come
  from titles (`Overview`, `Session clock`, `Quick reference`, `Notes`, otherwise `Scene`; `#`
  makes a `Chapter`).
- `AdventureDraftPlanPrompt` asks the Loremaster for a plan. The draft is sent with line numbers;
  the answer is JSON (`[{ "startLine": 1, "title": "…", "kind": "Scene" }]`).
  `AdventureDraftPlan.Validate` refuses:
  - lines that are out of range or not ascending;
  - an unknown kind;
  - more than `MaxSections`;
  - a title over 200 characters.
  
  A refused plan falls back to the headings plan, with a message.
- `AdventureDraftCutter.Cut(draft, plan)` is the only code that slices the draft. For each start
  line:
  - the body runs from that line up to the next start;
  - when the start line is a heading, it is removed and its text becomes the title.
  
  Then `WordsOf(draft) == WordsOf(titles from headings + bodies)` is checked. Words are split on
  whitespace and Markdown heading marks; nothing else is normalised. A failed check throws, and
  the endpoint returns 500 with nothing applied, because it can only mean a bug.
- `POST prep/{id}/draft/split` with `{ plan, mode: Replace | Append, basedOnUpdatedAt }` cuts,
  checks, and writes the sections in one transaction. Replace first stores each written section's
  text as a draft revision (reason `Split`).
- The Web shows the plan in a dialog: rows of title, kind and the first words, with rename,
  re-kind, merge-with-previous and drop. Drop means merge, because no text is ever dropped.

## Gather

`AdventureDraftGatherer.Join(sections)` makes `# Title` for a Chapter and `## Title` for the
rest, a blank line, then the text, with skeleton-unfilled sections left out.
`POST prep/{id}/draft/gather` stores the old draft as a revision.

## Table brief and source books in prompts

- `PrepPromptContext` (Application/Prep) is built once per call by one method,
  `PrepPromptContextBuilder.BuildAsync(adventure, query, caller)`. It holds:
  - the system line (feature 35);
  - the levels;
  - the table brief;
  - the world knowledge;
  - the reference passages.
  
  Drafting, the side conversation, design mode and the split planner all take it, so none can
  forget the brief or the books (pre-implementation check 2).
- Reference passages come from `IReferencePassageRetriever.RetrieveAsync` at the GM role. With
  source books chosen, a new overload takes the document ids and sends their Bibliotaph entry ids
  (or filters the indexed uploads). It is bounded by the retriever's `PassageLimit` (10) and
  formatted with the Loremaster's existing reference formatter, so citations look the same.
- The prompts' rules say reference passages are published text: cite them as
  "(*Book*, p. 12)", and never copy a stat block or more than a short quote.

## API

- `GET prep/{id}` adds `draft` (`{ markdown, updatedAt, revisionCount }` or null), `tableBrief`
  and `sourceBooks` (`[{ id, title }]`).
- `PUT prep/{id}/draft` saves the draft, based on `updatedAt`.
- `POST prep/{id}/draft/undo` steps back.
- The edits, split and gather endpoints described above.
- `PUT prep/{id}/brief` saves the table brief.
- `PUT prep/{id}/source-books` with `{ documentIds }`: at most five, each a world book the GM
  can see.
- The conversation's message request gains `scope: Section | Draft`.

## Pre-implementation checks

1. **Where each rule lives.**
   - Edits: `DraftEdits.Parse`.
   - The cut: `AdventureDraftCutter`.
   - The default plan: `AdventureDraftPlanner`.
   - The prompt context: `PrepPromptContextBuilder`.
   - A boundary scan: only the cutter writes sections from a draft.
2. **Second caller.** Every Prep prompt takes `PrepPromptContext`, and a test builds each prompt
   and asserts the brief and a reference passage appear.
3. **Null, empty, zero.**
   - No draft means the Design tab offers "Start a draft" (empty, or Gather).
   - An empty brief adds nothing to a prompt.
   - No source books means every book.
   - No reference passages means the prompt says so.
4. **Unbounded.**
   - The draft is capped at 60,000 characters.
   - Revisions are capped.
   - Plans are capped at `MaxSections`.
   - Passages are capped at 10.
   - Source books are capped at 5.
5. **Show each guard failing.**
   - The word check: sabotage the cutter so it drops a line.
   - The Apply refusal: a passage that has changed.
   - A plan with out-of-order lines.
   - The boundary scan.

## Out of scope

- Draft all empty sections, and revise selected sections. Design mode covers the same need
  without a batch AI job.
- Live co-editing of the draft by two people.
- Choosing page ranges within a source book (the search finds the pages).
