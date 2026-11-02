# Tasks

Ordered so that the two risks are retired before anything depends on them: whether the
editor holds the markup, and whether the SDK sends an output ceiling the deployment accepts.
After that the Adventure is built without AI, then drafting, then Print and Export. There is
one migration, and it is additive.

**Status: specified 2026-11-02; Phase A done 2026-11-02; Phase B done 2026-11-02; Phase C done 2026-11-02; Phase D done 2026-11-02; Phase E shipped 2026-11-02, live walk (E5) pending.** Ships as Nornis v3.0. The v3.1 items are listed
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

- [x] C1. `AiSpendPools`; `AiBudgetGuard.GetPrepStatusAsync` / `CheckPrepAsync`; the daily
      status reads the pool map. `AiBudgetGuardPrepTests`, including the pool sabotage.
- [x] C2. `World.PrepMonthlyBudgetUsd` through `WorldService` validation, the API contracts and
      the Settings card.
- [x] C3. `AdventureDraftPrompt`, with the cacheable prefix first.
- [x] C4. `IAdventureDraftingAiClient` plus its Infrastructure transport on the shared
      `IChatClient`, `Prep:Deployment`, the timeout and usage recording, with
      `CachedInputTokens` captured.
- [x] C5. `IAdventureDraftingService`: the gate, the 409s, the streaming, the transactional
      revision write and trim, Undo. Tests, including the Hidden-at-GM / not-at-Player case.
- [x] C6. The draft SSE endpoint and the Web's instruction box, Draft / Revise / Undo, and
      the Prep spend line on `/prep`.
      **Phase C notes (2026-11-02):** the deviations from design.md are in its Phase C
      amendment. Every test named under `AdventureDraftingServiceTests` and
      `AiBudgetGuardPrepTests` exists, each checked by breaking the code it covers. The Hidden
      case runs the real `KeywordKnowledgeRetriever`: the GM's prompt carries a Hidden fact, and
      the same retrieval text at Player role finds the artifact but not the fact. No migration.

## Phase D — Print and Export

- [x] D1. `AdventurePrintModel` (Markdig with the marker extension) and its tests.
- [x] D2. `/prep/{key}/print` and the print stylesheet. Check it against the synthetic fixture
      and the spa in Chromium print preview.
- [x] D3. File to Library: `adventureId` on the upload command, with a same-world check, a
      dated title, `Handout` and GMOnly, and `LastPrintedDocumentId` on confirm. The Library
      page's "Printed from Prep" line and the share confirmation.
- [x] D4. `AdventureExportComposer`, `AdventureExportService` (whole or one chapter), and tests.
- [x] D5. The `## Adventure Prep` section in `ExtractionPromptBuilder`, the retrieval skip, and
      tests.
- [x] D6. The Export button and its result (a link to the new source, then to Review).

      **Phase D notes (2026-11-02):** the deviations from design.md are in its Phase D
      amendment. The booklet was checked in headless Chromium against the synthetic fixture
      (`two-chapters.md`), printed to Letter: the cover, two columns, Chapter 2 on a new page,
      and each convention in its panel. The spa's check moves to E5's live walk, since its
      chapters stay out of the repo. Every new test was checked by breaking the code it covers:
      the retrieval skip, the prompt section, the export's GMOnly, both GM checks (the endpoint
      sweep), the print's adventure id, the share confirmation, the save before printing and
      the chapter choice.

## Phase E — Ship

- [x] E1. Amendments: `domain-model.md` (Adventure, sections, revisions, `AdventureExport`,
      `Conversation` status) and `ai-extraction.md` (drafting writes to an Adventure, never
      to canon). Both are drafted in this spec's PR and are re-checked against the build here.
      **Done 2026-11-02.** Both hold. Each carries a dated re-check note. `domain-model.md`'s
      sketch adds `World.PrepMonthlyBudgetUsd` and `AiOperationType.AdventureDrafting`. Two
      more steering docs gained a short amendment: `security-and-permissions.md` (Prep is
      GM-only by current role, and how print and export keep the boundary) and
      `observability-and-costs.md` (the monthly Prep pool among the guardrails).
- [x] E2. `Directory.Build.props` MajorVersion 3, MinorVersion 0; the changelog entry;
      `docs/features/README.md` row 29.
      **Done 2026-11-02**, plus the in-app Features page (a Prep section, Prep in the GM's
      role row, and a limit saying stat blocks are never computed), the README's feature
      table and AI configuration note (`Prep:Deployment`), and a changelog entry for the
      October fixes that had none.
- [x] E3. Build, `dotnet test --solution Nornis.sln`, and `dotnet format --verify-no-changes`
      in a clean worktree with an explicit restore.
      **Done 2026-11-02** on main after Phase D plus this phase: build 0 warnings and 0 errors;
      4,260 tests, 4,258 passed and 2 skipped (the Explicit live tests); format clean.
- [x] E4. Apply migration `AddPrep` before deploy, and watch the deploy.
      **Done in Phase B**, the PR that brought the migration: `AddPrep` was applied to
      production before #65 merged. Phases C to E add no migration.
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
