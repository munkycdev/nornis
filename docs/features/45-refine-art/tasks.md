# Tasks

Ships as Nornis 3.27. No migration.

**Specified 2026-11-11** in the thread "Add GPT-Image-2.6 Sunburst and Flare models", after 3.26
(PR #140) swapped `gpt-image-2` for Sunburst and Flare. Dave agreed the shape: Flare drafts, a
Refine on Sunburst through the edit endpoint, not deterministic.

## Phase A — Refine

- [x] A0. `requirements.md`, `design.md` and this file; row 45 in the features README.
- [x] A1. `AiTask.ArtRefinement`, `AiTasks.IsImageTask`, the two operation types and their pools;
      catalog validation; appsettings order (Flare first, Sunburst also refines).
- [x] A2. `IImageArtGenerator.RefineAsync` and the OpenAI edit call.
- [x] A3. `ArtRefinePrompt`; `AdventureCoverService.RefineAsync`, `WorldKeyArtService.RefineAsync`.
- [x] A4. The two endpoints, the API client, the Refine buttons, the Models row's words.
- [x] A5. Tests; change log 3.27; version bump.

## Phase B — Live check (Dave)

- [ ] B1. Generate a cover on Flare, refine one option, and compare it with the draft.
