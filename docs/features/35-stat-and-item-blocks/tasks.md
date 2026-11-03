# Tasks

Three PRs: this spec; then the grammar, the booklet and writing them; then warnings, export and
the version bump. Ships as Nornis v3.6. There is no migration.

**Status: specified 2026-11-03. Dave chose a richer text grammar, parsed, over structured
records.**

## Phase A — Spec

- [x] A1. `requirements.md`, `design.md` and this file; row 35 in the features README.

## Phase B — The grammar, the booklet, writing them

- [ ] B1. `GameBlocks` (Application/Prep): the records, `Parse`, `InMarkdown`, `ExportLine`.
      Add `PrepMarkup.ItemBlockInfo`. `GameBlocksTests`.
- [ ] B2. `BookletModel` reads game blocks through `GameBlocks`. `BookletStatBlock` is
      replaced by `BookletGameBlock`. Port the `BookletModelTests` stat cases.
- [ ] B3. The renderer: the stat block's name bar, subtitle, groups and score row; the item
      card; keep-together. PdfPig tests. Sabotage: the score row and the item keep-together.
- [ ] B4. `PrepMarkup.ConventionsForPrompt`, used by both prompts, with the worked example and
      the reuse and published-book rules. `PromptConventionsTests`.
- [ ] B5. Web: the new Stat block template, the Item button and template, the
      `ItemBlockInfo` mirror. bUnit tests.
- [ ] B6. A `PrepBoundaryTests` scan: only `GameBlocks` parses a game fence.
- [ ] B7. Validate: a clean worktree build with 0 warnings, the full test run, `dotnet format`,
      sabotage each new guard, and render the Slurping Spa locally.

## Phase C — Warnings, export, ship 3.6

- [ ] C1. `GameBlockWarnings`; `AdventureSectionResponse.BlockWarnings` on every response
      path. The Web alert under the editor. Tests at each layer.
- [ ] C2. `AdventureExportComposer` writes one line per named game block, with the game system
      resolved by the export service. Composer tests.
- [ ] C3. The extraction prompt's Prep paragraph files a block line as one fact. Test.
- [ ] C4. Ship: `MinorVersion` 6; the change log, the Features page and the README; row 35;
      ticks here; the steering re-check.
- [ ] C5. Live walk (Dave): draft a scene that asks for a stat block and an item, print it,
      and export the chapter. Review should show one fact per block.
