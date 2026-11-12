# Requirements Document

## Introduction

On 2026-11-11 OpenAI replaced `gpt-image-2` with GPT-Image-2.6 Sunburst ("detailed visual work")
and Flare ("fast iteration"); Nornis 3.26 offers both for Pictures. Dave then asked whether the
initial designs could come from Flare and the finished picture from Sunburst, and whether Sunburst
could reproduce exactly what Flare made.

It can't be exact: OpenAI's image API takes no seed, so the same prompt on another model paints a
new picture. What it can do is take the Flare draft as an input image to Sunburst's edit endpoint,
with an instruction to keep the composition and redraw the detail. Sunburst is the model OpenAI
pitches for consistency across edits. Dave agreed this shape on 2026-11-11 ("I like that, let's
do it and merge").

Ships as Nornis 3.27. No migration.

## Glossary

- **Draft**: a generated option, made from the brief on the Pictures model (Flare by default).
- **Refine**: one call that sends one option and its brief to the Refining pictures model
  (Sunburst) as an edit, and adds the result as a new option beside the draft.

## Requirements

### Requirement 1: Drafts on Flare, refinement on Sunburst

**User Story:** As a GM, I want quick drafts and a detailed finish, so I can explore cheaply and
print the one I like at its best.

#### Acceptance Criteria

1. THE Pictures task SHALL default to `openai/gpt-image-2.6-flare`; Sunburst SHALL stay pickable.
2. A new AI task, Refining pictures, SHALL default to `openai/gpt-image-2.6-sunburst`, under the
   same funding rule as Pictures: only the world creator's own OpenAI key pays, and neither the
   trial nor an OpenAI-compatible line can run it.
3. Profile → AI → Models SHALL show the new task as its own row.

### Requirement 2: Refine an option

**User Story:** As a GM, I want to refine the option I like, so the cover or key art keeps its
composition with more detail.

#### Acceptance Criteria

1. EACH generated option on the Cover card and the Key art card SHALL offer Refine.
2. WHEN the GM refines an option, THE system SHALL send that option's image and the current brief
   with the image rules, plus an instruction to keep composition, subjects, palette and lighting,
   to the Refining pictures model's edit endpoint, at high quality and the option's shape.
3. THE result SHALL be added as a new option, shown first; neither the draft nor the cover or key
   art SHALL change until the GM chooses.
4. A refine SHALL count as one image against the same cap as a generation (twelve per adventure;
   eight per world per month for key art), counted before the call and given back on failure,
   and SHALL be spent from the Prep pool.
5. Failures SHALL read as a generation's do (safety refusal, OpenAI's reason, no key, timeout).
6. THE refined option SHALL record AI use when chosen, as any generated option does.
