# Tasks

Ordered so that the two risks are retired before anything depends on them: whether the
editor holds the markup, and whether the SDK sends an output ceiling the deployment accepts.
After that the Adventure is built without AI, then drafting, then Print and Export. There is
one migration, and it is additive.

**Status: specified 2026-11-02; Phase A done 2026-11-02; Phase B done 2026-11-02.** Ships as Nornis v3.0. The v3.1 items are listed
at the end so the v3.0 design leaves room for them.

## Phase A — Retire the risks

- [x] A1. `PrepMarkup` constants and `PrepMarkupRoundTripTests`. Prove that `[!PROP]`, `[!ASK]`,
      a `statblock` fence and a pipe table survive TipTap → `HtmlToMarkdownConverter`. If the
      alert line can't be held, switch `PrepMarkup` to the bold-label fallback here and amend
      design.md's markup section with the date.
      **Done 2026-11-02.** Checked against the real bundle: `tiptap.bundle.min.js` loaded in
      headless Chromium, each case's Markdig HTML through `setContent` and `getHTML()`. TipTap
      holds everything. `HtmlToMarkdownConverter` lost the fence's info string, merged a
      quote's paragraphs and dropped `> ` from `<br>` lines; all fixed, and the captured HTML
      is the input of `PrepMarkupRoundTripTests`. The convention stands, no fallback.
- [x] A2. One real gpt-5.4 call on `nornis-ask` with an output ceiling, confirming
      `max_completion_tokens` goes on the wire and is accepted. Record the result in this
      file. Until it passes, C4 ships without a ceiling, and the gap stays open in this file.
      **Wire half done 2026-11-02:** `OutputCeilingWireTests` reads the request body through
      `AzureOpenAiChatClient` (OpenAI 2.14.0, Microsoft.Extensions.AI.OpenAI 10.10.1), streamed
      and not: a ceiling goes out as `max_completion_tokens`, never `max_tokens`. **Live half:**
      `LiveOutputCeilingTests` (Explicit, needs the API's secrets), **passed 2026-11-02** on
      Dave's machine at bb733e1, against `nornis-ask` (gpt-5.4-2026-03-05):
      - ceiling 2500: finish Stop, 35 output tokens (0 reasoning), 151 chars;
      - ceiling 64 on a 400-word ask: finish Length at exactly 64 output tokens (0 reasoning).

      The deployment accepts `max_completion_tokens` and enforces it, so the gap is closed for
      the drafting client: C4 carries `MaxOutputTokens` 2,500. Reasoning tokens count against
      that ceiling on this model family and were 0 here; C4 records them if they ever aren't.

## Phase B — The Adventure (no AI)

- [x] B1. Domain: `Adventure`, `AdventureSection`, `AdventureSectionRevision`,
      `AdventureSectionKind`, `SourceType.AdventureExport`, `Source.AdventureId`,
      `LibraryDocument.AdventureId`, `World.PrepMonthlyBudgetUsd`,
      `AiOperationType.AdventureDrafting`. EF configurations, with every FK Restrict and
      detach-before-delete. Update the enum-definition tests.
- [x] B2. Migration `AddPrep`.
- [x] B3. `PrepOptions`, and the bound test pinning `MaxAdventureChars <= SourceService.MaxBodyChars`.
- [x] B4. `AdventureSkeleton`, `AdventureMarkdownImporter`, and the synthetic fixture
      `Fixtures/Prep/two-chapters.md` with its tests.
- [x] B5. `IAdventureService` and repositories, with tests including the GM-check sabotage.
- [x] B6. `PrepController` (all non-AI routes) and endpoint tests.
- [x] B7. World lifecycle: `WorldDeletionService` covers the three tables, world export and
      import gain the Prep category, and the demo clone skips it. Tests for each.
- [x] B8. Web: nav item, `/prep`, `/prep/{key}` with outline, editor toolbar buttons, reorder,
      Import Markdown dialog, and `Links.Adventure`. `ReachabilityTests`.
      **Phase B notes (2026-11-02):** the deviations from design.md are recorded in its Phase B
      amendment. B7's import half has no world import to extend, so it is open until one exists.
      The toolbar's inserts were captured from the real TipTap bundle and pinned in
      `PrepMarkupRoundTripTests`.

## Phase C — Drafting

- [ ] C1. `AiSpendPools`; `AiBudgetGuard.GetPrepStatusAsync` / `CheckPrepAsync`; the daily
      status reads the pool map. `AiBudgetGuardPrepTests`, including the pool sabotage.
- [ ] C2. `World.PrepMonthlyBudgetUsd` through `WorldService` validation, the API contracts and
      the Settings card.
- [ ] C3. `AdventureDraftPrompt`, with the cacheable prefix first.
- [ ] C4. `IAdventureDraftingAiClient` plus its Infrastructure transport on the shared
      `IChatClient`, `Prep:Deployment`, the timeout and usage recording, with
      `CachedInputTokens` captured.
- [ ] C5. `IAdventureDraftingService`: the gate, the 409s, the streaming, the transactional
      revision write and trim, Undo. Tests, including the Hidden-at-GM / not-at-Player case.
- [ ] C6. The draft SSE endpoint and the Web's instruction box, Draft / Revise / Undo, and
      the Prep spend line on `/prep`.

## Phase D — Print and Export

- [ ] D1. `AdventurePrintModel` (Markdig with the marker extension) and its tests.
- [ ] D2. `/prep/{key}/print` and the print stylesheet. Check it against the synthetic fixture
      and the spa in Chromium print preview.
- [ ] D3. File to Library: `adventureId` on the upload command, with a same-world check, a
      dated title, `Handout` and GMOnly, and `LastPrintedDocumentId` on confirm. The Library
      page's "Printed from Prep" line and the share confirmation.
- [ ] D4. `AdventureExportComposer`, `AdventureExportService` (whole or one chapter), and tests.
- [ ] D5. The `## Adventure Prep` section in `ExtractionPromptBuilder`, the retrieval skip, and
      tests.
- [ ] D6. The Export button and its result (a link to the new source, then to Review).

## Phase E — Ship

- [ ] E1. Amendments: `domain-model.md` (Adventure, sections, revisions, `AdventureExport`,
      `Conversation` status) and `ai-extraction.md` (drafting writes to an Adventure, never
      to canon). Both are drafted in this spec's PR and are re-checked against the build here.
- [ ] E2. `Directory.Build.props` MajorVersion 3, MinorVersion 0; the changelog entry;
      `docs/features/README.md` row 29.
- [ ] E3. Build, `dotnet test --solution Nornis.sln`, and `dotnet format --verify-no-changes`
      in a clean worktree with an explicit restore.
- [ ] E4. Apply migration `AddPrep` before deploy, and watch the deploy.
- [ ] E5. Live walk in *Gribbits Detective Agency*, as design.md lists it, recorded here.

## v3.1 (designed for, not built)

- A persisted side conversation: `Conversation` and `ConversationMessage` as `domain-model.md`
  describes them, plus `AdventureId`. Access follows the Adventure's rule (current GM). Never
  extracted.
- "Threads to use" from the convergence gauge on the Overview.
- The worker renders the PDF with headless Chromium from `AdventurePrintModel`, so File to
  Library takes one click.
- The booklet reaches Bibliotaph by whatever "Replace Nornis Library with Bibliotaph"
  settles. The current proposal is Nornis as a read-only storage provider inside Bibliotaph.

## Deferred (Later)

- Telling extraction which Adventure a session ran.
- Printing only the props as a player handout.
- Bring-your-own AI keys.
