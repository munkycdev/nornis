# Tasks

Three PRs: this spec; then the grammar, the booklet and writing them; then warnings, export and
the version bump. Ships as Nornis v3.6. There is no migration.

**Status: built 2026-11-03 (Phases B and C in one PR; C5, the live walk, is Dave's). Dave
chose a richer text grammar, parsed, over structured records. See the design's amendment for
what building it changed, including the GM callout and the adventure's own system in prompts.**

## Phase A — Spec

- [x] A1. `requirements.md`, `design.md` and this file; row 35 in the features README.

## Phase B — The grammar, the booklet, writing them

- [x] B1. `GameBlocks` (Application/Prep): the records, `Parse`, `InMarkdown`, `ExportLineFor`.
      Add `PrepMarkup.ItemBlockInfo`. `GameBlocksTests`.
- [x] B2. `BookletModel` reads game blocks through `GameBlocks`. `BookletStatBlock` is
      replaced by `BookletGameBlock`. Port the `BookletModelTests` stat cases.
- [x] B3. The renderer: the stat block's name bar, subtitle, groups and score row; the item
      card; keep-together. PdfPig tests. Sabotage: the score row and the item keep-together.
- [x] B4. `PrepMarkup.ConventionsForPrompt`, used by both prompts, with the worked example and
      the reuse and published-book rules. `PromptConventionsTests`.
- [x] B5. Web: the new Stat block template, the Item button and template, the
      `ItemBlockInfo` mirror. bUnit tests.
- [x] B6. A `PrepBoundaryTests` scan: only `GameBlocks` parses a game fence.
- [x] B7. Validate: a clean worktree build with 0 warnings, the full test run, `dotnet format`,
      sabotage each new guard, and render the Slurping Spa locally.

## Phase C — Warnings, export, ship 3.6

- [x] C1. `GameBlockWarnings`; `AdventureSectionResponse.BlockWarnings` on every response
      path. The Web alert under the editor. Tests at each layer.
- [x] C2. `AdventureExportComposer` writes one line per named game block, with the game system
      resolved by the export service. Composer tests.
- [x] C3. The extraction prompt's Prep paragraph files a block line as one fact. Test.
- [x] C4. Ship: `MinorVersion` 6; the change log, the Features page and the README; row 35;
      ticks here; the steering re-check.
- [ ] C5. Live walk (Dave): draft a scene that asks for a stat block and an item, print it,
      and export the chapter. Review should show one fact per block.
