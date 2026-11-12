# AI Extraction and Loremaster Behavior

> **Amendment (2026-11-12): the GM's standing instructions ride on every extraction.** Feature 44
> (Nornis 3.42) gives a world an editorial brief: up to 40 `EditorialRule` rows of 300 characters,
> written by the GM on the Check-in page or by answering a check-in question. `ExtractionService`
> reads them fresh on every attempt and `ExtractionPromptBuilder` renders them, through the one
> `EditorialBriefPrompt.Render`, as a "## Standing Instructions" section between the prompt's
> purpose and "## What to Extract", stating that they take precedence over the general guidance:
> a detail an instruction says is not tracked is Incidental however clearly the source states
> it. A world with no instructions sees the prompt it always saw. The check-in's own chooser
> call (`AiOperationType.CheckIn`, Ask model, daily budget) reads the same brief so it does not
> ask what the GM has settled. A check-in answer that changes the record does so as an accepted
> `UpdateArtifact` (status) on a `CheckIn` review batch, or through `IArtifactMergeService`, so
> it has the provenance and the undo of any reviewed change; nothing in the check-in writes to
> canon outside those two paths. The Loremaster and the retrospective do not read the brief.

## Core Rule

AI may propose changes. AI must not silently mutate accepted world knowledge.

All accepted artifact, fact, and relationship changes must come from user review actions or explicit trusted system operations.

> **Amendment (2026-08-05) — the summary refresh is a trusted system operation.** W1
> (accept-time summary maintenance) regenerates `Artifact.Summary` after accepted changes,
> and the policy decision its plan required is recorded here: the refresh runs under this
> rule's own trusted-operation carve-out, not through review. A summary is derived
> presentation over already-accepted facts and relationships — every claim it can make has
> already passed the gate once, and routing restatements back through review would roughly
> double review traffic to approve prose. The boundaries that keep "trusted" honest:
>
> - **Input is scope-filtered.** The generator reads only facts and relationships visible
>   at the artifact's own visibility (the same `ForSourceContext` gate extraction uses,
>   including the Hidden-truth-state rule) — a PartyVisible artifact's summary regenerated
>   from GM-only material would surface it to every player who can see the page.
> - **An explicit summary always wins.** An accepted proposal that carries a summary is
>   the reviewer choosing that text; the refresh never runs against an artifact whose
>   summary the same accept explicitly set.
> - **Provenance is recorded** on every refresh: an `AiUsageRecord` under the (previously
>   dormant) `ArtifactSummary` operation type, and `Artifact.SummaryRefreshedAt`.
> - **A per-world setting opts back into review** (`World.SummaryReviewRequired`, default
>   off): with it on, the refresh files a Pending `UpdateArtifact` proposal through
>   `SyntheticBatchWriter` (batch kind `SummaryRefresh`) instead of writing directly.
> - Budget-guarded and worker-side like every other AI call; never inline in the accept
>   request.

> **Amendment (2026-08-07) — an accept may create the artifact its proposal names.** When a
> fact or relationship references an artifact by a name nothing resolves, the accept works
> down a ladder rather than dead-ending: the batch's own undecided Create for that name,
> then the artifacts that resemble it, then creating it. The last rung grows canon by
> something the reviewer did not individually approve, which is why it is written down here.
> What keeps it inside the rule above:
>
> - **The reviewer's action is still the only trigger.** Nothing here runs on the extraction
>   path or in the worker; it happens because a person clicked accept on a proposal that
>   names the thing.
> - **Resemblance stops rather than guesses.** Deciding that "Voss" and "Captain Voss" are
>   one thing belongs to the GM (`ArtifactNameKey` says so, and says why). If anything the
>   reviewer has — or is about to have, counting undecided sibling Creates — resembles the
>   name, the accept refuses with `artifact_name_near_match` and lists what it found. Only a
>   name nothing resembles is created, because there is then no judgment to make.
>   `CreateMissingArtifact` on the command is the reviewer's answer coming back.
> - **A rejection is never undone by the side door.** If the batch's Create for that name was
>   rejected, the automatic path stops (`artifact_create_rejected`); only an explicit
>   `CreateMissingArtifact` proceeds.
> - **It goes through review, not around it.** The artifact is created by filing a
>   CreateArtifact proposal into the same batch and accepting it, so it gets the same apply
>   path, dedup, visibility and provenance as any other accepted Create, and the record
>   afterwards says where it came from. It is typed `Concept` — "we do not know yet" — with
>   the reason in its rationale, because nothing in a fact says whether the thing it names is
>   a person or a place.
> - **The reviewer is told.** `AcceptProposalResult.CreatedMissingArtifactNames` carries the
>   names back and the queue says so: an accept that grew canon past the card on screen must
>   not do it quietly.

> **Amendment (2026-11-02) — drafting writes to an Adventure, never to canon.** Feature 29
> (Prep) lets the Loremaster draft and revise sections of a GM's Adventure. That is AI writing
> text no review gates, so it is recorded here against the rule above. What keeps it inside
> that rule:
>
> - **The target is not world knowledge.** A draft replaces one `AdventureSection` body, a
>   plan the GM owns, and never an artifact, fact or relationship. The old body is kept as a
>   revision, so the GM can undo the write.
> - **The only way into canon is Export through review.** Export files the Adventure as an
>   `AdventureExport` source. It is extracted and reviewed like any source, and the prompt
>   for that type extracts world truths only, never planned scenes as Events.
> - **Context is scoped by the retriever, not the prompt.** Drafting asks `IKnowledgeRetriever`
>   at the caller's role, which is always GM, so Hidden truth is in context because the asker
>   may see it. That is the same rule Ask uses, and it isn't restated in the drafting code.
> - **Metered and capped on its own.** Every call is an `AdventureDrafting` usage record,
>   capped by the world's monthly Prep budget (separate from the daily world budget), stopped
>   by the global pause, and given an output ceiling.
>
> Re-checked against the build on 2026-11-02, when v3.0 shipped. It holds. The ceiling is
> 2,500 output tokens, sent as `max_completion_tokens`, and a draft the ceiling cuts off is
> refused rather than saved. An `AdventureExport` source, like a Library excerpt, retrieves
> no reference passages, because the source is the reference.
>
> v3.1 (feature 30, 2026-11-02) adds two things under the same rule. *Talk it through* is a
> GM-only conversation with the Loremaster per Adventure: its answers are stored only as
> conversation messages, which are never extracted or retrieved, and an answer reaches a
> section only when the GM chooses **Put in section**, which is a revision like a draft and
> undoes the same way. It retrieves at the caller's role, is metered as
> `AdventureConversation` in the Prep monthly pool, and an answer the ceiling cuts off is not
> saved. *Threads to use* makes no AI call: an Overview draft is given the convergence gauge's
> top five candidates, read at the GM's role, and the gauge's narration is never asked for.

## Extraction Goal

> **Amendment (2026-11-08) — extraction judges significance, and a Storyline needs intent and
> a resolution.** Feature 43 (`docs/features/43-reduce-noise`). Symbaroum's record after two
> years showed the prompt below tuned for recall: 1,962 proposals accepted against 31 rejected,
> 65 Storylines created and 56 of them closed by hand, and confidence unable to tell the piles
> apart (25 of the 31 rejections sat at 0.9 or above). Three things changed:
>
> - **Every proposal carries a `significance`** of Core, Supporting or Incidental at the
>   proposal level (beside `confidence` and `quote`, never inside `proposedValue`, whose
>   sixteen fields the Anthropic adapter counts). It answers "what will the table do with this",
>   which confidence does not. `ExtractionTriage` drops Incidental before any ReviewProposal is
>   written, keeping only an Incidental CreateArtifact that a kept proposal names. A response
>   triage empties completes as an empty batch. Absent means Supporting; nothing is stored.
> - **A Storyline is an arc the table is pursuing**: an actor with intent and a conceivable
>   resolution, both required. A character's development or nature, the world reacting to a
>   character, mood, foreshadowing and one-off warnings are facts and relationships on the
>   Character or Faction. A source rarely opens more than one Storyline, never more than two.
>   The line "an Event that matters almost always belongs to an arc" is gone. Open questions
>   need the party pursuing the answer.
> - **Retired storylines are shown as such.** `ArtifactContext.Status` renders as
>   `Status: Dormant` or `Status: Resolved` under the artifact in the Existing World Artifacts
>   list (Active renders nothing), and the prompt says never to open a twin or add open
>   questions to one; a source that plainly reopens it proposes UpdateArtifact to Active.
>
> The "Product Language" section below still reads as the storyline trigger it was; the
> prompt's definition is now the stricter one above.

Given a source, extract proposed updates to the world knowledge graph.

Input:

- Source text
- Source metadata
- Relevant existing artifacts/facts/relationships

Output:

- ReviewBatch
- ReviewProposal records

## Product Language

Use **Storyline** instead of **Thread** for narrative arcs, mysteries, quests, investigations, and unresolved developments.

The extraction model should create or update artifacts with `Artifact.Type == Storyline` when source material advances a narrative line.

Do not use `Thread` in user-facing proposal titles, UI copy, or artifact types.

## Structured Output Required

> **Amendment (2026-11-03) — every schema is strict, and extraction's payload is closed.**
> Feature 39 lets a world run on its creator's own OpenAI, Claude or Gemini key, and those
> providers' strict modes accept only one dialect: every object closed
> (`"additionalProperties": false`) and every property listed in `required`, optional values
> written as a `null` union. Every schema a client sends is now held to it
> (`StrictSchemaDialectTests`), and every call goes out strict. Extraction was the one
> exception: `proposedValue` below was an open object. It is now a closed object naming all
> sixteen payload fields (`name`, `type`, `summary`, `visibility`, `confidence`, `status`,
> `sourceArtifactId`, `predicate`, `value`, `truthState`, `artifactName`, `artifactAId`,
> `artifactBId`, `artifactAName`, `artifactBName`, `description`), each nullable and each
> required; `targetId` and `quote` are required too. The prompt tells the model to send null
> for every field its changeType doesn't use, and the parser drops the nulls before anything
> reads the payload, so `ReviewProposal` sees exactly the object it saw before. A new payload
> field has to be added to the schema, the prompt and this list together.
>
> Who pays for a call is decided per call, not here: services name an `AiCallScope` (the
> world, or the user when there is no world yet) and Nornis's own model, and the call is bound
> to the world creator's key, the trial, or Nornis's Azure key by `IAiFundingService`
> (feature 39, `docs/features/39-ai-keys/design.md`). Budget guards run first, unchanged.

Use structured AI outputs wherever possible. The extraction response must conform to a known schema and should not require fragile natural-language parsing.

Expected extraction result shape:

```json
{
  "proposals": [
    {
      "changeType": "CreateArtifact",
      "targetType": "Artifact",
      "targetId": null,
      "proposedValue": {},
      "rationale": "string",
      "confidence": 0.0
    }
  ]
}
```

The application layer converts each structured proposal into a `ReviewProposal` record.

## Proposal Granularity

Create one proposal per reviewable change.

Do not create one giant proposal containing many unrelated mutations.

Good:

- Add artifact: Captain Voss
- Add relationship: Captain Voss located in Black Harbor
- Add fact: Silver Key found in Voss's quarters
- Add storyline: Missing Caravan

Bad:

- One enormous proposal called "Update world" containing 17 different changes.

## Existing Context

The extraction process should include relevant existing artifacts to reduce duplicates.

For MVP, retrieval uses simple SQL search:

- Recently active artifacts in the world.
- Artifacts whose names appear in the source text (name-matched).

This is sufficient for early worlds. As worlds grow, a more sophisticated retrieval layer (e.g., Azure AI Search) may be needed. Defer that decision until scale requires it.

Vector search is not required for MVP.

## Imported Notes

Sources of type `ImportedNote` are notes exported from a previous note-taking system and
receive extra handling at extraction time (the stored source body stays raw):

- The body is normalized before extraction: YAML frontmatter is stripped, and wikilink
  markup (`[[[[uuid|Label]]]]`, block references, aliases) is reduced to plain
  `[[Label]]` markers.
- The prompt tells the model that `[[Label]]` terms were explicit links in the previous
  system — strong signals for CreateArtifact/AddFact/AddRelationship proposals.
- `{curly brace}` text is the user's own annotation; `{Question: ...}` routes into the
  open-question convention instead of becoming a world fact.

## Deduplication

Before proposing a new artifact, try to match against existing artifacts in the same world.

Suggested matching strategy for MVP:

1. Exact normalized name match.
2. Case-insensitive name match.
3. Simple fuzzy match or AI-suggested possible match.
4. If uncertain, create a proposal that asks whether to merge or create new.

## Confidence

All proposals should include confidence.

Use confidence to influence UI presentation, not to bypass review.

## Visibility

Extraction must respect source visibility.

A proposal derived from `GMOnly` source material should not create `PartyVisible` knowledge by default.

Default mapping:

```text
Private source      -> Private proposal
GMOnly source       -> GMOnly proposal
PartyVisible source -> PartyVisible proposal
```

Users may adjust visibility during review if authorized.

## Truth State Defaults

Default fact/relationship truth state should be conservative.

Suggested defaults:

- Direct observation in notes: `Likely` or `Confirmed`, depending on wording.
- Character claims: `Rumor` or `Disputed` unless corroborated.
- GM notes: `Hidden` or `Confirmed` depending on visibility and phrasing.
- Player theories: `Rumor`.

## Source Citations

Every accepted fact or relationship derived from AI extraction must cite the source that produced it.

Use `SourceReference` records to preserve traceability.

## Token and Cost Tracking

> **Amendment (2026-09-28) — a streamed answer cut short is estimated, not free.** Ask the
> Loremaster streams its answer, and a stream reports token usage only in its final chunk. An
> answer that ends early — the reader closes the page, the model times out midway — never hears
> it. Recording those at zero tokens would make "ask, then leave" free, public Ask included, so
> once any text has arrived the row is estimated at four characters per token from the prompt
> and the text received (the same rough ratio the context budget uses). Before the first word
> nothing is provably spent and the row carries zero tokens. A reader leaving is recorded with
> `ErrorCode = "Cancelled"` and `Succeeded = true`: the outcome monitor reads `Succeeded` as
> "is the provider up", and a reader walking away says nothing against it.

Every AI call must create an `AiUsageRecord`.

Capture:

- User
- World
- Operation type
- Model
- Input tokens
- Output tokens
- Estimated cost
- Source ID if applicable
- Review batch ID if applicable
- Duration
- Success/failure

## Loremaster Ask Behavior

The Ask interface should answer from structured world knowledge first.

Preferred grounding order:

1. Artifacts
2. Artifact facts
3. Artifact relationships
4. Source references
5. Raw source excerpts when needed

Answers should cite sources where possible.

The Loremaster must respect visibility and world membership.

**MVP Note:** The retrieval strategy for Ask is deferred. For MVP, use a simple approach (e.g., load relevant artifacts by name/keyword match). A production-grade retrieval layer (potentially Azure AI Search with vector embeddings) will be needed as worlds grow. Design the Ask interface so the retrieval mechanism is swappable behind an abstraction.

## Hallucination Guardrails

When the answer is not supported by world knowledge, the assistant should say so.

Do not invent canon.

Acceptable phrasing:

```text
I don't have a confirmed source for that yet.
```

or

```text
The world sources suggest this, but it is currently marked as rumor.
```

## MVP AI Operations

MVP should support:

- Extract proposals from text sources.
- Generate artifact summaries from accepted facts and relationships.
- Answer world questions from accepted artifacts and cited sources.

MVP should not support:

- Autonomous canon mutation.
- Long-running agent loops.
- Multi-step planning agents.
- Audio transcription.
- Complex OCR pipeline.
- Expensive whole-world reprocessing by default.
