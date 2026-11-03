# Tasks

Ships as Nornis 3.8, after feature 36. One migration (`AddPrepDesignMode`), which Dave applies
before the merge.

**Status: specified 2026-11-03. Dave asked for an interactive design mode over one Markdown
document, decomposed into sections at the end, and for all Prep work to use what the world
already knows.**

## Phase A — Spec

- [x] A1. `requirements.md`, `design.md` and this file; row 37 in the features README.

## Phase B — The draft, the brief and the books

- [ ] B1. Entities, configuration and the migration; the draft's save, undo and revisions; the
      brief; source books (max five). Repository and service tests.
- [ ] B2. `PrepPromptContextBuilder` with the brief and reference passages; drafting and the
      conversation switch to it. The retriever overload for chosen books. Tests that every
      prompt carries the brief and a passage.
- [ ] B3. Web: the Design and Sections tabs, the draft editor, the brief card, and the source
      books picker. bUnit tests.

## Phase C — Iterating and splitting

- [ ] C1. The conversation's Draft scope; `DraftEdits.Parse`; the Apply endpoint and its
      refusal; Put in draft. Tests.
- [ ] C2. `AdventureDraftPlanner`, `AdventureDraftPlan.Validate`, `AdventureDraftCutter` with
      its word check, and the Loremaster's plan prompt; the split and gather endpoints. Tests,
      including sabotage of the word check.
- [ ] C3. Web: suggested-edit cards, the split plan dialog, and Gather. Import into the draft.
      bUnit tests.
- [ ] C4. Ship: `MinorVersion` 8; the change log, the Features page and the README; row 37;
      ticks; the steering re-check. Validate: a clean build with 0 warnings, the full test run,
      `dotnet format`, and sabotage of each new guard.
- [ ] C5. Live walk (Dave): paste the Slurping Spa into a new Adventure's draft, split it at its
      headings, check no word changed, then iterate on one scene in Design mode with the
      Spa's source book chosen.
