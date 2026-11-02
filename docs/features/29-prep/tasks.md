# Tasks

Ordered so that the two risks are retired before anything depends on them: whether the
editor holds the markup, and whether the SDK sends an output ceiling the deployment accepts.
After that the Adventure is built without AI, then drafting, then Print and Export. There is
one migration, and it is additive.

**Status: specified 2026-11-02, not started.** Ships as Nornis v3.0. The v3.1 items are listed
at the end so the v3.0 design leaves room for them.

## Phase A — Retire the risks

- [ ] A1. `PrepMarkup` constants and `PrepMarkupRoundTripTests`. Prove that `[!PROP]`, `[!ASK]`,
      a `statblock` fence and a pipe table survive TipTap → `HtmlToMarkdownConverter`. If the
      alert line can't be held, switch `PrepMarkup` to the bold-label fallback here and amend
      design.md's markup section with the date.
- [ ] A2. One real gpt-5.4 call on `nornis-ask` with an output ceiling, confirming
      `max_completion_tokens` goes on the wire and is accepted. Record the result in this
      file. Until it passes, A6 ships without a ceiling, and the gap stays open in this file.

## Phase B — The Adventure (no AI)

- [ ] B1. Domain: `Adventure`, `AdventureSection`, `AdventureSectionRevision`,
      `AdventureSectionKind`, `SourceType.AdventureExport`, `Source.AdventureId`,
      `LibraryDocument.AdventureId`, `World.PrepMonthlyBudgetUsd`,
      `AiOperationType.AdventureDrafting`. EF configurations, with every FK Restrict and
      detach-before-delete. Update the enum-definition tests.
- [ ] B2. Migration `AddPrep`.
- [ ] B3. `PrepOptions`, and the bound test pinning `MaxAdventureChars <= SourceService.MaxBodyChars`.
- [ ] B4. `AdventureSkeleton`, `AdventureMarkdownImporter`, and the synthetic fixture
      `Fixtures/Prep/two-chapters.md` with its tests.
- [ ] B5. `IAdventureService` and repositories, with tests including the GM-check sabotage.
- [ ] B6. `PrepController` (all non-AI routes) and endpoint tests.
- [ ] B7. World lifecycle: `WorldDeletionService` covers the three tables, world export and
      import gain the Prep category, and the demo clone skips it. Tests for each.
- [ ] B8. Web: nav item, `/prep`, `/prep/{key}` with outline, editor toolbar buttons, reorder,
      Import Markdown dialog, and `Links.Adventure`. `ReachabilityTests`.

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
