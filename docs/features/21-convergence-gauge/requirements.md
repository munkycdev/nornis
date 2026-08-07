# Requirements Document

## Introduction

Feature 17 gave the GM a way to reveal GM-only knowledge to the party. It answers *how*. It
does not answer *what*, and *what* is the harder question: a world that has run for a year
holds hundreds of hidden facts, and the GM is the only index into which of them the table is
ready for. The reveal page opens on a list of everything still secret, ordered by nothing.

This feature is that index. It reads the record and puts forward the hidden material whose
moment has arrived — the secret sitting under a storyline the party keeps circling, the truth
that contradicts something they currently believe, the reveal that costs nothing to make
because nothing else has to come with it.

**Convergence** is the word for what it measures: how far the party's knowledge has closed the
distance to the GM's. A gauge reading, not a verdict.

The scoring is **mechanical**. Every signal below is a count, a date, or a graph walk over data
the system already holds, and the reasoning that produces a ranking is shown to the GM in the
same terms. This is the W2 lesson applied before the fact: a model asked for a number the
system can count adds spend and one new way to be wrong. An optional second phase buys the one
thing counting cannot supply — a sentence of dramatic judgment about *why now* — and buys it
only after the mechanical shortlist exists.

The gauge **never reveals anything**. It ranks and explains; every reveal still goes through
`IRevealService.RevealAsync` with the GM confirming, which is where the review-gate invariant
already lives. A ranked list is a suggestion, and a suggestion the GM ignores must cost them
nothing.

Delivered in two phases:

- **Phase 1 — The mechanical gauge.** Candidate discovery, the score and its components, the
  read model, and a GM-only surface that hands each candidate to the existing reveal flow
  pre-filled.
- **Phase 2 — Narrated ordering.** An optional, budget-gated pass that reads the top of the
  mechanical shortlist and writes the *why now* for each, without changing the ranking's
  inputs.

## Requirements

### Requirement 1: Rank Hidden Knowledge by Readiness

**User Story:** As a GM, I want the hidden material in my world ordered by how ready it is to
be revealed, so I stop scanning a flat list of every secret I have ever written.

#### Acceptance Criteria

1. THE system SHALL provide a GM-only read operation that returns hidden candidates for a
   world, each with a convergence score and the components that produced it.
2. A **candidate** SHALL be an `ArtifactFact` whose `TruthState` is `Hidden` or whose
   `Visibility` is `GMOnly`, an `ArtifactRelationship` whose `Visibility` is `GMOnly`, or an
   `Artifact` whose `Visibility` is `GMOnly`.
3. THE system SHALL NOT treat `Private` elements as candidates; Private is the GM's own
   workspace and is not a party-facing secret awaiting its moment.
4. THE operation SHALL be read-only: it SHALL NOT change any element's visibility, truth
   state, or status, and it SHALL NOT write review proposals.
5. WHEN a non-GM invokes the operation, THE system SHALL reject it with 403.
6. THE score SHALL be deterministic for a given world state — two calls with no intervening
   writes return the same ordering.

### Requirement 2: The Score Is Legible

**User Story:** As a GM, I want to see why something is ranked highly, so I can disagree with
the gauge instead of obeying it.

#### Acceptance Criteria

1. EACH candidate SHALL carry its component signals individually, not only a total.
2. EACH component SHALL be expressible as a phrase the UI can render without recomputation
   (for example "hidden for 94 days", "contradicts a fact the party believes", "reveals
   cleanly on its own").
3. THE total SHALL be a documented function of its components, and that function SHALL live in
   one place in code.
4. WHERE a candidate scores highly on exactly one component, THE system SHALL still surface
   the other components so a lopsided score reads as lopsided.

### Requirement 3: Readiness Includes the Cost of Revealing

**User Story:** As a GM, I want to know what a reveal drags along with it before I open it, so
"what can I reveal tonight" is answerable at a glance.

#### Acceptance Criteria

1. THE system SHALL compute, for each candidate, the reference closure the existing reveal
   primitive would require — the set that must be revealed with it to leave no
   `PartyVisible` element pointing at a `GMOnly` one.
2. THE closure size SHALL be a component of the score, and a smaller closure SHALL raise
   readiness.
3. THE closure SHALL be computed by the same rule `IRevealService` enforces, not a
   reimplementation of it.
4. WHERE a candidate's closure is empty other than itself, THE system SHALL mark it as
   self-contained.

### Requirement 4: Contradiction Pressure Ranks Highest

**User Story:** As a GM, I want to be told when the party believes something my hidden record
contradicts, because that is the reveal with a deadline.

#### Acceptance Criteria

1. WHERE a hidden fact contradicts a `PartyVisible` fact on the same artifact, THE system
   SHALL raise that candidate's score above candidates with no contradiction.
2. THE system SHALL reuse the contradiction detection that Continuity Health already performs
   rather than introducing a second definition of contradiction.
3. WHERE Continuity Health has no current assessment for the world, THE system SHALL still
   return a ranking, with the contradiction component scored zero and reported as unavailable
   rather than as absent.

### Requirement 5: Dormancy and Anchoring

**User Story:** As a GM, I want secrets that have sat untouched for a long time on entities
the party knows well to rise, because those are the ones I have forgotten I wrote.

#### Acceptance Criteria

1. THE system SHALL score how long a candidate has been hidden, measured from its creation.
2. THE system SHALL score how well the party already knows the candidate's anchoring artifact,
   measured by the count of `PartyVisible` facts on that artifact.
3. WHERE the candidate's artifact participates in a storyline, THE system SHALL score that
   storyline's status, and a `Resolved` storyline holding an unrevealed secret SHALL rank
   above an `Active` one.
4. THE system SHALL NOT score an artifact the party has never encountered as ready merely
   because it is old.

### Requirement 6: Hand Off to Reveal, Pre-filled

**User Story:** As a GM, I want to act on a suggestion in one step, so the gauge is a way into
the reveal flow rather than a second place to read lists.

#### Acceptance Criteria

1. EACH candidate SHALL carry enough identity for the UI to open the existing reveal flow with
   that candidate and its closure pre-selected.
2. THE gauge SHALL NOT construct its own reveal path; it SHALL hand off to
   `IRevealService.RevealAsync`.
3. WHERE the GM reveals a candidate, THE next read of the gauge SHALL no longer return it.

### Requirement 7: Narrated Ordering (Phase 2)

**User Story:** As a GM, I want a sentence explaining why a suggestion is timely, because
timing is the part arithmetic cannot see.

#### Acceptance Criteria

1. THE system SHALL provide an optional pass that annotates the top N mechanical candidates
   with a short rationale.
2. THE pass SHALL be budget-gated through the existing AI budget guard and SHALL record usage.
3. THE pass SHALL NOT reorder candidates by any signal the mechanical score does not already
   hold; it annotates a ranking, it does not produce one.
4. WHERE the pass is unavailable, over budget, or fails, THE gauge SHALL return the mechanical
   ranking unannotated rather than an error.
5. THE prompt SHALL receive only GM-scoped material, and its rationale SHALL NOT be shown to
   any non-GM caller.

## Out of Scope (this feature)

- Revealing anything automatically, on a schedule, or on a threshold. The gauge suggests.
- Per-player or per-character readiness — the party is one audience here, as in feature 17.
- Un-reveal, or lowering visibility, which feature 17 ruled out deliberately.
- A player-facing view of the gauge. Its whole content is what players do not know.
- Learning from which suggestions the GM accepted or ignored. Worth wanting; it needs a
  feedback record that does not exist and would be its own feature.
- `LibraryDocument` readiness — library material is reference, never canon.
