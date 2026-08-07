# Design Document

## Overview

A GM-only read model that ranks a world's hidden material by how ready it is to be revealed,
and a page that hands each suggestion to the reveal flow from feature 17 with its closure
pre-selected. Nothing here writes to the knowledge graph.

The whole of Phase 1 is a query, an arithmetic function, and a projection. Three of the five
signals come from data already on the entities; the fourth reuses
[`RevealClosure`](../../../src/Nornis.Application/Services/RevealClosure.cs) unchanged; the
fifth reads Continuity Health's existing findings rather than defining contradiction a second
time.

## Why the score is mechanical

Every signal below is a count, a date, or a one-pass graph walk. W2 established the rule this
follows: a model asked for a fact the system can count adds spend and one new way to be wrong
— there, the wrong way was merging two artifacts backwards. Here it would be a confident
ranking nobody can argue with, which is worse than a lopsided one the GM can see through.

Phase 2 buys the one thing arithmetic cannot supply — a sentence about dramatic timing — and
buys it *after* the ranking exists, over the top of it, never as an input to it.

## The candidate set

A candidate is one of:

| Kind | Condition | Anchoring artifact |
|---|---|---|
| Fact | `TruthState.Hidden` **or** `Visibility == GMOnly` | its `Artifact` |
| Relationship | `Visibility == GMOnly` | both endpoints |
| Artifact | `Visibility == GMOnly` | itself |

`Private` is excluded (Req 1.3): it is the GM's workspace, not a secret with an audience.
Archived artifacts and their facts are excluded — a reveal cannot be pending on something
removed from canon.

## Signals

Each yields a value in `[0, 1]`. The names below are the ones the read model carries, so the
UI renders a phrase per component without recomputing anything (Req 2.1, 2.2).

### 1. `ContradictionPressure` — the reveal with a deadline

The party believes something the GM's hidden record contradicts. Read from the latest
`HealthAssessment`'s **Open** findings of category `Contradiction`, matching a finding's
`ArtifactId` and resolved `EvidenceJson` ids against the candidate. Reused, not redefined
(Req 4.2) — a second definition of contradiction would drift from Continuity Health's within
a release.

Scored `1.0` on a match with `Severity` at the top band, scaling down by severity, `0` with no
match. When the world has no assessment, the component is `null` rather than `0`, and the read
model reports it unavailable (Req 4.3) — "we did not look" and "we looked and found nothing"
must not render identically.

### 2. `Dormancy` — how long it has sat

Days since the candidate's `CreatedAt`, saturating: `min(days / 180, 1)`. Six months of
silence is as dormant as the score needs to say; a two-year-old secret is not twice as ripe as
a one-year-old one.

### 3. `AnchorFamiliarity` — does the party know where to put this?

Count of `PartyVisible` facts on the anchoring artifact, saturating at a small number (5).

**This one multiplies rather than adds.** Requirement 5.4 says an entity the party has never
met must not rank as ready on age alone, and an additive term cannot express that — a
sufficiently old secret would climb regardless. As a multiplier with a non-zero floor, an
unknown entity's secrets sink without vanishing, which is the honest reading: not *un*ready,
just not yet legible to anyone.

### 4. `SelfContainment` — what it drags with it

`RevealClosure.MissingArtifactDependencies` over the single candidate. Because only artifacts
can be missing dependencies, this is cheap and bounded: a fact needs its parent artifact
visible (0 or 1 missing), a relationship needs both endpoints (0 to 2), an artifact needs
nothing. Scored `1.0` at zero missing — the flag Requirement 3.4 calls self-contained — and
falling off with each artifact that must come along.

### 5. `StorylineState` — has the moment passed?

The status of any storyline the anchoring artifact participates in: `Resolved` scores highest,
then `Dormant`, then `Active`, then no storyline. A secret still hidden under a storyline the
table has finished is the clearest case of a reveal that was missed rather than withheld.

## The score

```text
base  = wC·ContradictionPressure + wD·Dormancy + wS·StorylineState + wK·Selfcontainment
score = base × max(AnchorFamiliarity, familiarityFloor)
```

Weights and the floor live as constants in one place — `ConvergenceWeights` — so Requirement
2.3 is satisfied by construction and a tuning change is one file. Ordering is by descending
score, then by `CreatedAt`, then by `Id`, which makes Requirement 1.6's determinism a property
of the comparison rather than of the data.

## Service

`IConvergenceGaugeService.GetGaugeAsync(worldId, actingUserId, role, ct)` →
`AppResult<ConvergenceGauge>`.

GM-gated at the top (403 for anyone else, Req 1.5). Reads through the repositories with
`VisibilityFilter.All` — the gauge's entire subject is material below the party floor, so an
Observer-scoped read would return an empty gauge and look like a working feature.

`ConvergenceGauge` carries the candidates, the assessment id the contradiction component was
read from (or null), and the generated-at stamp. Each `ConvergenceCandidate` carries kind,
ids, name, the five component values, the total, the closure's missing artifact ids, and the
self-contained flag.

No caching in Phase 1. The query is per-world and GM-invoked, and a stale gauge is worse than
a slow one.

## API

`GET /api/worlds/{worldId:guid}/convergence` — GM only, returns `ConvergenceResponse`.

## UI

A GM-only page listing candidates highest first. Each row shows the name, the kind, the
component phrases, and a self-contained marker. Selecting a row opens the existing reveal
dialog with the candidate and its closure pre-selected (Req 6.1) and submits through
`IRevealService.RevealAsync` — the gauge contributes no write path of its own (Req 6.2).

## Phase 2 — narrated ordering

One AI call over the top N candidates, behind `IAiBudgetGuard` with usage recorded, following
the prompt seam: the Application layer owns the prompt text, the adapter owns transport and
parse. The prompt receives GM-scoped material and the rationale is GM-only (Req 7.5).

The call annotates and never reorders (Req 7.3). Failure, timeout, or an exhausted budget
returns the mechanical ranking unannotated (Req 7.4) — the gauge's value does not depend on
it.

## Correctness Properties

*A property is a characteristic that should hold across all valid executions — the bridge
between the spec and machine-verifiable tests.*

### Property 1: The gauge is read-only

*For any* gauge read, no artifact, fact, or relationship changes visibility, truth state, or
status, and no review proposal is written. **Validates: Req 1.4.**

### Property 2: Only hidden, non-private material is a candidate

*For any* world, every returned candidate is `GMOnly` or `TruthState.Hidden`, and no `Private`
or already-`PartyVisible` element appears. **Validates: Req 1.2, 1.3.**

### Property 3: Determinism

*For any* world state, two consecutive reads return the same candidates in the same order.
**Validates: Req 1.6.**

### Property 4: Closure agrees with the reveal primitive

*For any* candidate, the missing dependencies reported equal what `RevealClosure` returns for
that candidate — the gauge and the reveal cannot disagree about what a reveal costs.
**Validates: Req 3.1, 3.3.**

### Property 5: Familiarity gates, it does not merely add

*For any* two candidates identical but for anchor familiarity, the one on the better-known
artifact scores higher; and *for any* candidate on an artifact with no party-visible facts,
raising dormancy alone never lifts it above a familiar, contradicted candidate.
**Validates: Req 5.4.**

### Property 6: Revealed material leaves the gauge

*For any* candidate that is revealed, the next read does not contain it. **Validates: Req 6.3.**

### Property 7: The gauge survives a missing assessment

*For any* world with no health assessment, the read succeeds, every candidate's contradiction
component is reported unavailable, and the ordering is still total.
**Validates: Req 4.3.**

## Error Handling

- Non-GM → `403 insufficient_role`.
- ~~Unknown or non-member world → `404`, matching the rest of the world-scoped API.~~
  **Corrected 2026-08-06 during phase C:** this was wrong about the house convention.
  `WorldMemberActionFilter` answers a non-member `403` *regardless of whether the world exists*,
  so the status cannot be used to probe for one — a stronger guarantee than the 404 written
  here. The endpoint inherits it by carrying the filter.
- No candidates → `200` with an empty list. An empty gauge is a fact about the world, not an
  error.
- Phase 2 AI failure or budget exhaustion → `200` with the mechanical ranking and no
  rationales.

## Testing

Unit tests over the scoring function directly — it is pure, and every component has a boundary
worth pinning (zero days dormant, saturation, empty closure, missing assessment). Property
tests for Properties 2, 3, and 5, which are quantified over generated worlds rather than
examples. Authorization tests carry `TestCategory=Authorization` per the suite's convention.

Phase 2's prompt is asserted on the captured prompt, per the leak-surface pattern W1
established: the test's subject is what the prompt contains, not what the model returns.

## Design decisions to confirm before build

1. **Familiarity as a multiplier (chosen) vs an additive component.** Additive cannot satisfy
   Requirement 5.4 — age alone would eventually carry an unknown entity to the top. Leaning
   multiplier with a non-zero floor.
2. **Read the contradiction component from Continuity Health's stored findings (chosen) vs
   computing it live.** Stored reuses the definition and costs nothing, but goes stale between
   assessments; live doubles the definition. Leaning stored, with the unavailable state
   reported honestly.
3. **Artifacts as candidates in their own right (chosen) vs facts and relationships only.** A
   wholly GM-only NPC is a legitimate reveal, and excluding it would make the gauge silent
   about the largest secrets. Leaning include.
4. **No caching in Phase 1 (chosen).** Revisit only if the query shows up in the cost or
   latency dashboards.
5. **Weights.** The initial constants are a starting guess, not a finding. They want one real
   world's worth of use before anyone defends a number.

   **Answered 2026-08-06, against a real world.** Vespergale Reach returned 38 candidates
   scoring 31 down to 2. The arithmetic explains it: every candidate was 12 days old, so
   dormancy contributed almost nothing, and the world had no contradiction findings, so the
   heaviest signal (0.40) contributed zero everywhere — capping the achievable score near 31.
   The ordering was right; the magnitude read as "none of this matters".

   The fix was display, not weights. `ConvergenceDisplay.RelativeFill` draws each ring
   against the strongest candidate on show, so the top row is full, while the number stays
   absolute and the colour stays keyed to it. Normalising the *number* was the tempting
   version and the wrong one: it would show 100 for the best candidate of a world where
   nothing is ready. A full ring in a muted colour is the honest reading — best available,
   not urgent. The weights themselves are unchanged and still want a world with
   contradictions in it before anyone defends them.
