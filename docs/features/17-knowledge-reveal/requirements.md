# Requirements Document

## Introduction

Nornis has a rich private layer: GMs already author `GMOnly` sources, artifacts, facts,
and relationships, and extraction already keeps them from leaking into party-visible canon
(see [`ExtractionService`](../../../src/Nornis.Application/Services/ExtractionService.cs)
— `EnforceVisibility`, scoped context, and GM-only reference grounding). What is missing is
the deliberate move in the other direction: **letting a GM promote GM-only knowledge to the
party when the fiction discloses it.**

Today that move is impossible by design. A source's visibility is frozen after extraction
(the 409 in [`SourceService`](../../../src/Nornis.Application/Services/SourceService.cs):
*"knowledge derived from this source keeps its original scope"*), and there is no
first-class action that flips a GM-only artifact, fact, or relationship to `PartyVisible`.
That lock is a placeholder. This feature is the sanctioned mechanism it was standing in for.

The feature is a **general reveal primitive over the knowledge graph**, not a bespoke tool
for any one artifact type. A map's locations are just artifacts; a storyline's true name is
just a fact; an NPC's real allegiance is just a relationship. Reveal treats them uniformly.
Scenarios (a bought map, a crystallized mystery, an unmasked villain) are worked examples,
never features.

The guiding principle mirrors the rest of the system: **the GM decides, the system records.**
Reveal never widens visibility on its own — it is always an explicit GM confirmation, applied
through the existing review-proposal machinery with full provenance, and it is one-way.

Delivered in two phases in this document, split on the architectural seam between the canon
graph and the source layer:

- **Phase 1 — Canon reveal:** promote artifacts, facts, and relationships. This is almost
  entirely assembly of existing mechanisms.
- **Phase 2 — Source reveal:** promote a GM-only source and its attachments (e.g. a map
  image), which requires lifting the post-extraction visibility lock in a controlled way.

## Requirements

### Requirement 1: Reveal Canon Knowledge (Phase 1)

**User Story:** As a GM, I want to promote a chosen set of my GM-only artifacts, facts, and
relationships to the party, so the players' record gains what the fiction has now disclosed.

#### Acceptance Criteria

1. THE system SHALL provide a GM-only operation that changes the visibility of a specified
   set of artifacts, facts, and/or relationships from `GMOnly` to `PartyVisible`.
2. THE operation SHALL be general across all `ArtifactType` values; it SHALL NOT special-case
   Locations, Storylines, or any other type.
3. WHEN a non-GM invokes the operation, THE system SHALL reject it with 403 and change
   nothing.
4. THE operation SHALL only ever raise visibility toward `PartyVisible`; it SHALL NOT reduce
   any element's visibility, and it SHALL NOT alter `Private` elements.
5. WHERE an element in the set is already `PartyVisible`, THE system SHALL treat it as a
   no-op rather than an error (idempotent reveal).
6. THE operation SHALL apply all elements in one transaction: either every element in the
   (closure-complete) set is revealed, or none is.

### Requirement 2: Reveal Is Reference-Closed

**User Story:** As a GM, I don't want a reveal to leave the players looking at a broken
graph — an edge to a node they can't see, or a fact on an artifact they can't see.

#### Acceptance Criteria

1. THE system SHALL treat a reveal set as valid only when, after it is applied, no
   `PartyVisible` element references a `GMOnly` element. Specifically:
   - a revealed fact's parent artifact SHALL be `PartyVisible` (already, or in the set);
   - a revealed relationship's two endpoint artifacts SHALL both be `PartyVisible`
     (already, or in the set).
2. WHERE the submitted set is not reference-closed, THE system SHALL reject it and return
   the exact set of additional elements required to close it, rather than silently revealing
   more than the GM asked for.
3. THE system SHALL NOT auto-expand a reveal beyond the GM's explicitly confirmed set; the
   GM re-submits with the dependencies included.
4. Derived, visibility-less artifacts of a revealed element (e.g. a `MapPlacemark`, which is
   gated at read time by its artifact) SHALL require no explicit handling — they follow their
   artifact automatically.

### Requirement 3: Provenance and Confirm-and-Apply

**User Story:** As a GM, I want every reveal to be recorded like every other change in
Nornis — who revealed what, when — and I want the players' new knowledge to carry honest,
player-visible provenance rather than a pointer to my prep.

#### Acceptance Criteria

1. WHEN a reveal is confirmed, THE system SHALL create a synthetic reveal `Source`
   (`SourceType.Reveal`, `PartyVisible`, `Processed`) and a `ReviewBatch`
   (`Kind = "Reveal"`), and apply the changes through the existing
   [`IProposalApplicator`](../../../src/Nornis.Application/Application/ProposalApplicator.cs)
   as accepted proposals — never as an unattributed direct mutation.
2. THE reveal SHALL reuse the existing `UpdateArtifact` / `UpdateFact` / `UpdateRelationship`
   change types carrying a `visibility` field; it SHALL NOT introduce a new applicator path.
3. AFTER a reveal, each revealed element SHALL carry at least one `PartyVisible`
   `SourceReference` (to the reveal source), and any pre-existing `GMOnly` source references
   SHALL remain but stay filtered from players by the standard visibility rules — so a player
   sees "learned via the reveal," never the originating GM-only source.
4. THE reveal SHALL record the acting GM and timestamp on the accepted proposals, consistent
   with the audit-trail rules.
5. THE operation SHALL NOT reveal anything without an explicit GM action; there is no
   automatic or scheduled reveal.

### Requirement 4: Selective — Reveal the Place, Not Its Secrets

**User Story:** As a GM, revealing that a location exists must not dump every secret I've
recorded about it onto the party.

#### Acceptance Criteria

1. Revealing an artifact SHALL NOT implicitly reveal that artifact's facts or relationships;
   only elements explicitly named in the reveal set change visibility.
2. THE reveal set SHALL be an explicit list of element ids per kind (artifacts, facts,
   relationships), so the GM controls exactly what is disclosed.

### Requirement 5: Optional Corrections (Believed → Learned)

**User Story:** As a GM, when a reveal contradicts something the players believed, I want to
mark the old belief as superseded in the same event, without deleting their record of having
believed it.

#### Acceptance Criteria

1. THE reveal request MAY include a set of corrections: existing `PartyVisible` facts whose
   `TruthState` is to be changed (e.g. to `Disputed` or `False`) as part of the reveal.
2. Corrections SHALL be applied in the same reveal batch and carry the same reveal
   provenance, via the existing `UpdateFact` path; the prior fact SHALL be re-stated, never
   deleted.
3. *Detecting* which prior belief a reveal supersedes is out of scope (see Out of Scope);
   Requirement 5 covers only applying corrections the GM has explicitly specified.

### Requirement 6: Reveal a Source (Phase 2)

**User Story:** As a GM, when the party comes into a GM-only document — the map they bought,
a handout they found — I want to make that source itself visible to them.

#### Acceptance Criteria

1. THE system SHALL provide a GM-only operation that changes a `GMOnly` source's visibility
   (and, with it, its attachments such as a map image) to `PartyVisible`.
2. THIS operation SHALL supersede the post-extraction visibility lock for the GM: the 409 in
   `SourceService` remains the guard for ordinary source edits, but the reveal path is the
   sanctioned exception that performs the change with provenance.
3. Revealing a source SHALL NOT implicitly reveal the canon derived from it; derived
   artifacts/facts are revealed (or not) via Requirement 1. (Consistent with Requirement 4:
   showing the players the map image is independent from revealing every location on it.)
4. THE operation SHALL be general over source types (prep note, handout, uploaded image,
   map), not map-specific.

### Requirement 7: Reuse, Not Reinvention

**User Story:** As the maintainer, I want reveal to compose the mechanisms already proven by
merge, the retrospective, and the wrap-up — a new surface and a new service, not a parallel
mutation pipeline.

#### Acceptance Criteria

1. Visibility changes SHALL go through the existing `Update*` proposals + `ProposalApplicator`,
   not a new change type or a direct repository write.
2. Provenance SHALL use the synthetic-source + `ReviewBatch.Kind` + confirm-and-apply pattern
   already used by [`ArtifactMergeService`](../../../src/Nornis.Application/Services/ArtifactMergeService.cs)
   and [`StorylineRetrospectiveService`](../../../src/Nornis.Application/Services/StorylineRetrospectiveService.cs).
3. All visibility reads SHALL go through `VisibilityFilter`; the service SHALL NOT hand-roll
   role→scope logic.

## Out of Scope (this feature)

- **Automatic duplicate detection / reconciliation.** When a revealed GM-only artifact
  duplicates a party-visible one the players already knew, folding them together uses the
  *existing* `ArtifactMergeService` (which already preserves per-fact visibility on merge).
  The GM points at the pair; the system does not yet *detect* it. Cross-visibility dedup
  detection is a later feature.
- **The convergence gauge / "what should I reveal now?" suggestions.** This feature builds
  the hands (the reveal mechanism); the brain that recommends reveals — ripeness scoring,
  earned-vs-unearned — is a separate later feature that will *call* this primitive.
- **The GM-only structural extraction pass.** Reveal operates on whatever GM-only canon
  exists, however it was authored; it does not depend on richer GM-truth extraction.
- **Per-player / per-character reveal.** Reveal is party-wide (`PartyVisible` = all members).
  Disclosing a secret to one PC and not another is not modeled.
- **Un-reveal / reversibility.** Reveal is one-way; a mistaken reveal is handled by a
  follow-up correction, consistent with the append-only posture — not by lowering visibility.
- **Library-document reveal.** A `LibraryDocument` carries its own `GMOnly`/`PartyVisible`
  flag and is a separate entity; applying the same reveal *concept* to it is a plausible
  future extension, not part of this feature.
- **Player-facing "what you learned" digest or notifications.** Revealed knowledge simply
  appears in party-visible views with its reveal provenance.
