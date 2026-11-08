# Design Document

## Overview

```
ExtractionService.InvokeAiWithRetriesAsync
    ExtractionPromptBuilder.BuildSystemPrompt   ← Significance, Storylines, Retired Storylines
    ExtractionPromptBuilder.BuildUserMessage    ← "Status: Dormant" line under retired storylines
    IAiExtractionClient.ExtractAsync            ← schema gains proposal-level "significance"
    ExtractionService.ValidateResponse          (unchanged)
    ExtractionTriage.Apply                      ← the one place Incidental is dropped
    HandleSuccessfulResponseAsync               (unchanged; an empty result is an empty batch)
```

Four small changes along the seam that already exists. Nothing new is persisted, no entity
changes, no migration, no UI. The whole feature is prompt text, one schema field, one context
field and one pure function.

## Significance, and why it is not confidence

Confidence answers "how sure am I the source establishes this". Symbaroum's numbers show it
cannot carry the second question: 25 of the 31 proposals Dave rejected were at 0.9 or above,
because the model was quite right that the source said it. The question nobody asked the model
is "what will the table do with this", and that is a separate judgment with a separate field.

`significance` sits at the proposal level in the schema, beside `rationale`, `confidence` and
`quote`:

```json
"significance": { "type": "string", "enum": ["Core", "Supporting", "Incidental"] }
```

required and non-nullable, so it adds nothing to the union or optional counts the Anthropic
adapter fits (`AnthropicSchemaLimits`). It is deliberately not inside `proposedValue`: that
object is closed at sixteen nullable fields, each one counted, and significance describes the
proposal, not the thing proposed.

In Application it is `ProposalSignificance` (Core, Supporting, Incidental) on
`ExtractionProposal.Significance`, defaulting to Supporting. The default is the answer to the
"what does absent mean" question: a model that did not say is not a model that said incidental,
so an endpoint that ignores the schema (an OpenAI-compatible key without strict mode) loses
nothing. A value outside the enum is a parse failure like a bad `changeType`; the retry loop
already asks again for those.

## Triage

`ExtractionTriage.Apply(IReadOnlyList<ExtractionProposal>)` in `Nornis.Application.Ai` returns the
kept proposals and what it dropped. It is a pure function so the eval runner can call it on the
same parsed response the Worker sees, and the rule lives in one place (pre-implementation check
one).

The rule: drop every Incidental proposal, except a CreateArtifact whose proposed name a kept
proposal references by `artifactName`, `artifactAName` or `artifactBName`. Names are compared with
`ArtifactNameKey.AreEquivalent`, the same policy dedup and accept-time resolution use. Without the
exception a kept Core fact would arrive at accept time naming an artifact nothing proposed, and
the reviewer would meet the `artifact_name_near_match` ladder for a thing the model had actually
proposed and triage had thrown away. One pass suffices: a promoted create references nothing.

Triage runs in `InvokeAiWithRetriesAsync` after `ValidateResponse` and before
`HandleSuccessfulResponseAsync`, on a response that has already been paid for and validated.
Usage is carried through unchanged. A response triage empties takes the existing zero-proposal
branch: Completed batch, source Processed, call metered, replay nudged. That branch exists today
for a model that proposed nothing, and a model that proposed only texture is the same outcome.

Map extraction routes its proposals through `HandleSuccessfulResponseAsync` directly, never
through triage, and its proposals carry the default Supporting anyway.

Dropped counts are logged at Information with the names of dropped creates. Nothing is stored:
Dave chose to drop outright, and the source still holds the text for Ask and for reprocess.

## The prompt

Three sections change in `ExtractionPromptBuilder.BuildSystemPrompt`; the rest is untouched.

**What to Extract** loses "An Event that matters almost always belongs to an arc — leaving it
unlinked hides it from the storyline's timeline" and the storyline bullet that listed
"unresolved questions, emerging threats" among the triggers. It gains a closing sentence: a
proposal costs the reviewer a decision, so propose only what the record would be poorer without.

**Significance** is new. The three values are defined by what the table will do: Core is acted
on, changes what the party does next, names someone they will meet again, answers or raises a
question they are pursuing, or is a secret the GM keeps for a reason; Supporting is texture on
something Core; Incidental is mentioned once with no consequence. The section says Incidental is
discarded unseen, that a source of texture may yield nothing and that is correct, and that a
create a Supporting fact needs is itself Supporting. The Imported Notes section, which told the
model not to drop facts about linked terms "as incidental detail", now says a linked term is at
least Supporting because the user already decided it matters, so the two sections agree.

**Storylines** replaces the storyline bullet with the test from the requirements: an actor with
intent and a conceivable resolution, both required, then the list of what is not a Storyline
with the witch's warning as the example, then prefer advancing to creating, rarely more than one,
never more than two. **Open Questions** adds the pursuit condition.

**Retired Storylines** is new and explains the `Status:` line (below): the GM decided the table
is done with it; never a twin, never new open questions on it; if the source plainly reopens it,
UpdateArtifact to Active with the reason.

## Status in the context

`ArtifactContext.Status` (string, nullable) is set by `ExtractionService.AssembleContextAsync`
from `Artifact.Status`. `BuildUserMessage` renders `Status: Dormant` / `Status: Resolved` as its
own line under the artifact header, beside `Part of:`, and only when the status is not Active.
The header line itself is unchanged so `ExtractionPromptReader` and its thirty assertions keep
parsing; the reader learns the new line the way it learned `Part of:`.

Nothing changes in retrieval: `ListRecentByWorldAsync` and `ListByNamesInTextAsync` already
exclude only Archived, so retired storylines were in the prompt all along, indistinguishable
from live ones. Making them distinguishable is the whole change.

## What this does not do, on purpose

- No server-side cap on Storyline creates per source. The prompt says two; the triage drops
  incidental ones. If Symbaroum's next dump still shows a storyline a note, the cap is a
  ten-line follow-up in `ExtractionTriage`.
- No `Significance` column on `ReviewProposal`. The queue work (tiering, one-click reject,
  keeping the original payload on edit) will want it and will add the migration then.
- No change to the Retrospective's "when in doubt, StillActive", to the continuity threshold,
  or to what the wrap-up card proposes. The check-in and editorial brief are the next feature.
- No reprocess of Symbaroum. Existing artifacts stand; the change is to what arrives next.

## Tests

- `ExtractionPromptBuilderTests`: the Significance section and its discard sentence; the
  Storylines test and the witch example; the Event line gone; the pursuit condition on open
  questions; the Retired Storylines section; `Status:` rendered for Dormant and Resolved and
  absent for Active; the imported-notes sentence.
- `ExtractionPromptReader`: parses `Status:` into `ParsedArtifact.Status`.
- `ExtractionAiClientTests`: significance parsed; absent is Supporting; a value outside the
  enum names the field in the parse failure; the schema requires it at the proposal level.
- `AnthropicSchemaLimitsTests`: the fitted proposal's required list gains `significance`.
- `ExtractionTriageTests`: drops Incidental; keeps Core and Supporting; promotes an Incidental
  create a kept fact or relationship names, under whitespace and case differences; an
  all-Incidental list is empty; nothing dropped is a no-op.
- `ExtractionServiceContextAssemblyTests`: a Dormant storyline's context carries its status; an
  Active one carries none.
- `ExtractionServiceTriageTests`: Incidental proposals are not persisted and the batch holds the
  rest; all-Incidental completes an empty batch with the source Processed and usage recorded.
