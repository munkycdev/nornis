# AI Extraction and Loremaster Behavior

## Core Rule

AI may propose changes. AI must not silently mutate accepted campaign knowledge.

All accepted artifact, fact, and relationship changes must come from user review actions or explicit trusted system operations.

## Extraction Goal

Given a source, extract proposed updates to the campaign knowledge graph.

Input:

- Source text
- Source metadata
- Relevant existing artifacts/facts/relationships

Output:

- ReviewBatch
- ReviewProposal records

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

Bad:

- One enormous proposal called "Update campaign" containing 17 different changes.

## Existing Context

The extraction process should include relevant existing artifacts to reduce duplicates.

For MVP, retrieval uses simple SQL search:

- Recently active artifacts in the campaign.
- Artifacts whose names appear in the source text (name-matched).

This is sufficient for early campaigns. As campaigns grow, a more sophisticated retrieval layer (e.g., Azure AI Search) may be needed. Defer that decision until scale requires it.

Vector search is not required for MVP.

## Deduplication

Before proposing a new artifact, try to match against existing artifacts in the same campaign.

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
- Campaign
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

The Ask interface should answer from structured campaign knowledge first.

Preferred grounding order:

1. Artifacts
2. Artifact facts
3. Artifact relationships
4. Source references
5. Raw source excerpts when needed

Answers should cite sources where possible.

The Loremaster must respect visibility and campaign membership.

**MVP Note:** The retrieval strategy for Ask is deferred. For MVP, use a simple approach (e.g., load relevant artifacts by name/keyword match). A production-grade retrieval layer (potentially Azure AI Search with vector embeddings) will be needed as campaigns grow. Design the Ask interface so the retrieval mechanism is swappable behind an abstraction.

## Hallucination Guardrails

When the answer is not supported by campaign knowledge, the assistant should say so.

Do not invent canon.

Acceptable phrasing:

```text
I don't have a confirmed source for that yet.
```

or

```text
The campaign sources suggest this, but it is currently marked as rumor.
```

## MVP AI Operations

MVP should support:

- Extract proposals from text sources.
- Generate artifact summaries from accepted facts and relationships.
- Answer campaign questions from accepted artifacts and cited sources.

MVP should not support:

- Autonomous canon mutation.
- Long-running agent loops.
- Multi-step planning agents.
- Audio transcription.
- Complex OCR pipeline.
- Expensive whole-campaign reprocessing by default.
