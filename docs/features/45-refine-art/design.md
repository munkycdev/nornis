# Design Document

## Overview

A refine is an image edit: the option's PNG goes to OpenAI's `images/edits` with a prompt, on the
model the key holder's Refining pictures choice names. It reuses everything a generation uses
(funding, key binding, failure translation, pricing, caps, options storage) and adds one task, two
operation types, one generator method and two service methods. No schema change.

## Model choice: a task, not a flag

A refine needs a different model from the drafts. Two ways were weighed:

- A catalog flag naming the refine model, forced past the user's choice. This puts a second model
  rule beside the funding rule, which the pre-implementation checks call out (one place per rule).
- A new `AiTask.ArtRefinement`. The funding rule, the Models card and the ledger already handle a
  task; the only new rule is the catalog's order.

The task wins. Catalog: Flare serves `CoverArt` and is listed first, so it is the Pictures
default; Sunburst serves `CoverArt` and `ArtRefinement`, so it is the only (and default) refine
model. `UserAiModelChoices.Task` is a string of at most 16 characters; `ArtRefinement` is 13.

What generalises from `CoverArt` to "an image task":

- `AiTasks.IsImageTask(task)`: `CoverArt` or `ArtRefinement`. `OnlyKeysRun` is that set.
- Catalog validation: an image model may serve only image tasks, and an image task only image
  models (a chat model listed for `ArtRefinement` would bind a chat client to an image call).
- `AiModelsOptions.EstimateFor` excludes image models, as before.

## Operations and pools

`AiOperationType.CoverArtRefinement` and `KeyArtRefinement` map to `AiTask.ArtRefinement` and the
Prep monthly pool. Costs shows them as their own rows, so the GM can see what refining costs.

## Generator

`IImageArtGenerator.RefineAsync(scope, sourcePng, prompt, shape, timeoutSeconds, operation, ct)`
binds `ArtRefinement` and calls `GenerateImageEditsAsync(stream, "draft.png", prompt, 1, options)`
with the shape's size, `High` quality and PNG. `input_fidelity` and `complexity` are not sent: the
2.6 models' support for them is not documented, and sending an unsupported parameter fails the
call. The failure path is the generation's, shared.

## Prompt

`ArtRefinePrompt.Instruction`: "Refine this picture. Keep its composition, framing, subjects,
poses, palette and lighting as they are; redraw it with sharper, more careful detail in faces,
hands, textures and edges." Then the brief (when the GM has one), then the same image rules as a
generation, so a refine can't drop the no-text rule.

## Services

`AdventureCoverService.RefineAsync` and `WorldKeyArtService.RefineAsync`, each:

1. GM only; the option must be on offer (404 otherwise).
2. Cap: one image, through the same `TryCount…`, released on failure or an empty answer.
3. Budget: `CheckPrepAsync`.
4. Read the option's blob; gone is a 404 and releases the count.
5. Refine; record usage under the refinement operation, as a generation records.
6. Store the result as a new option: a cover appends it (the card shows newest first); key art
   puts it first and keeps the others. The next key-art generation clears all non-chosen options,
   as today, so key art storage stays bounded by the monthly cap.

## API and Web

`POST …/cover/refine` and `POST /api/worlds/{id}/key-art/refine`, body `{ optionId }` (the Choose
request). Each option tile gets a Refine button under it; the working line reads "Refining with
more detail. This can take a couple of minutes…".

## Testing

Application: the task map and pools; catalog validation; funding picks Flare for Pictures and
Sunburst for refining; both services' refine (adds an option first, counts and releases the cap,
records the operation, missing option and missing blob). Api: the Models list has the new row and
defaults. Infrastructure: the edit request carries the image, size, quality and model.
