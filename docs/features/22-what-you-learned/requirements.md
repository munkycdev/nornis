# Requirements Document

## Introduction

Feature 17 gave the GM a way to disclose hidden knowledge to the party. Feature 21 made it
easy to decide what to disclose. Neither did anything for the people on the receiving end:
a reveal lands silently, the codex quietly grows a fact, and a player who was not paying
attention on the night never learns that they learned something.

The record already knows. Every reveal writes a **party-visible `SourceType.Reveal` source**
with a `Reveal` batch whose accepted proposals name exactly what was promoted, carrying the
GM's own note when they wrote one. Nothing new has to be recorded for a player to be told
what they now know — the disclosure is already a first-class, dated, party-visible entry in
their own record. What is missing is the reader's half.

This feature is that half: **a player-facing view of what the GM has disclosed since they
last looked**, and a marker so "since they last looked" means something.

The governing constraint is the mirror image of the convergence gauge's. The gauge exists to
tell a GM what is still hidden. This must never tell a player that *anything* is — no count
of remaining secrets, no "more to come", no shape of the unrevealed. A player reading this
should be unable to distinguish a world with nothing left to disclose from one with a hundred
secrets in it. Reveal is one-way, and so is knowing that a reveal happened.

Delivered in phases:

- **Phase 1 — What you learned.** Reveals since the reader's marker, rendered for the party,
  and the marker itself.
- **Phase 2 — The unseen count.** A nav signal so the page is discovered without being
  remembered.
- **Phase 3 — The wider delta.** Party-visible knowledge that arrived through ordinary
  extraction, not only through deliberate reveal. Separate because it answers a different
  question, and because Phase 1 is provable without it.

## Requirements

### Requirement 1: What the Party Has Been Told

**User Story:** As a player, I want to see what the GM has disclosed since I last looked, so
returning after a fortnight does not mean re-reading the whole codex to find what changed.

#### Acceptance Criteria

1. THE system SHALL provide a read operation returning the reveals in a world that the calling
   member has not yet seen, newest first.
2. EACH entry SHALL carry its date, the GM's note where one was written, and the artifacts,
   facts, and relationships that reveal promoted.
3. THE operation SHALL be available to every world member, including Observers.
4. THE operation SHALL be read-only: it SHALL NOT change any element's visibility, and it
   SHALL NOT mark anything seen as a side effect of reading.
5. WHERE a member has seen everything, THE system SHALL return an empty result rather than an
   error.

### Requirement 2: Nothing Hidden Is Implied

**User Story:** As a GM, I want the players' view to be silent about what I have not told
them, so the page never becomes a map of where my secrets are.

#### Acceptance Criteria

1. THE view SHALL contain only elements that are `PartyVisible` at the time of reading.
2. THE view SHALL NOT report any count, total, or indication of material that remains
   `GMOnly`, `Private`, or `Hidden`.
3. WHERE a revealed element has since been archived or removed from canon, THE system SHALL
   omit it rather than render a gap that invites a question.
4. THE reveal's own source body SHALL NOT be rendered verbatim where it names counts of what
   was withheld; only the GM's note and the resolved elements are shown.
5. A player and an Observer SHALL see the same view, since both read at the party floor.

### Requirement 3: "Since I Last Looked" Means Something

**User Story:** As a player who reads on my phone and then on my laptop, I want the app to
remember what I have already seen, not the browser.

#### Acceptance Criteria

1. THE system SHALL record, per world member, the point up to which they have seen reveals.
2. THE marker SHALL be stored server-side against the membership, not in browser storage.
3. THE system SHALL provide an explicit operation to mark reveals seen up to a given point.
4. Marking seen SHALL be idempotent, and SHALL NOT move the marker backwards.
5. WHERE a member has never marked anything seen, THE system SHALL return a bounded first
   view rather than the world's entire reveal history.
6. A member's marker SHALL be their own; marking seen SHALL NOT affect any other member.

### Requirement 4: The Unseen Count (Phase 2)

**User Story:** As a player, I want to notice there is something to read without having to
remember to go and check.

#### Acceptance Criteria

1. THE system SHALL expose the number of unseen reveals for the calling member.
2. THE count SHALL be bounded for display, in the manner the review-queue badge already caps
   its own.
3. WHERE the count is zero, THE UI SHALL render no badge at all.
4. THE count SHALL obey Requirement 2: it counts what the member may see, and never indicates
   anything withheld.

### Requirement 5: The Wider Delta (Phase 3)

**User Story:** As a player returning after missing a session, I want to see what entered the
record while I was away, not only what was deliberately disclosed.

#### Acceptance Criteria

1. THE system SHALL additionally report party-visible knowledge that arrived since the
   marker through ordinary extraction.
2. THE two kinds SHALL remain distinguishable: what the GM chose to tell the party is not the
   same event as what a session note happened to record.
3. Requirement 2 SHALL hold unchanged for this material.

## Out of Scope (this feature)

- Push, email, or any notification that leaves the app. The count is the signal.
- Per-character views. The party is one audience, as in features 17 and 21.
- A GM-facing "what have I told them" audit. The reveal sources already answer it in the
  sources ledger, and a second surface would be a second definition.
- Un-reveal, or removing something from this view once shown. Reveal is one-way; so is having
  been told.
- Marking individual entries seen. The marker is a point in time, not a per-row state — a
  per-row state is a read receipt, and nothing in the product needs one.
