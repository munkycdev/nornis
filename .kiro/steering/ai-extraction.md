# AI Extraction and Loremaster Behavior

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

## Extraction Goal

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
