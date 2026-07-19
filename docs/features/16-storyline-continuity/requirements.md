# Requirements Document

## Introduction

Storylines drift out of play without anyone marking them finished, and new arcs open
without being nested under the broader arc they belong to. The record already has the
means to fix both — the [storyline retrospective](../../../src/Nornis.Application/Services/StorylineRetrospectiveService.cs)
proposes closures, the [relationship backfill](../../../src/Nornis.Application/Services/RelationshipBackfillService.cs)
and live extraction propose `PartOf` lineage — but every one of those is a manual button
a GM has to remember to press. The users who forget to note "we've moved on" are exactly
the ones who won't press it.

This feature makes the system notice the silence for the user. It adds two things:

1. **A staleness signal** — a deterministic (no-AI, no-cost) read model that flags Active
   storylines that have gone quiet, measured in sessions since their last development.
2. **A session wrap-up surface** — a compact, GM-facing card, shown at the natural cadence
   (after a session note is processed), that turns that signal plus existing pending
   proposals into a few one-click decisions: close what's finished, nest what's nested,
   dismiss what's still live.

The guiding principle is **nudge aggressively, mutate conservatively**: the system
surfaces candidates loudly, but every state change is an explicit GM confirmation with
full provenance. Nothing here auto-closes a storyline.

## Requirements

### Requirement 1: Storyline Staleness Signal

**User Story:** As a GM, I want the system to tell me which Active storylines have gone
quiet, so dropped arcs stop hiding among the live ones without my having to audit the
whole list.

#### Acceptance Criteria

1. THE system SHALL compute, for each Active storyline visible to the caller, the number
   of dated sessions that have occurred since that storyline's most recent development.
2. WHERE a storyline's sessions-since-last-development meets or exceeds the staleness
   threshold, THE system SHALL flag it as **quiet**.
3. THE signal SHALL be computed deterministically with no AI call and no cost.
4. THE signal SHALL report, per flagged storyline: name, status, last-development date (or
   none), sessions-since-last-development, count of unresolved open questions, and parent
   storyline (if any).
5. WHERE a storyline has no dated development at all, THE system SHALL NOT count it as
   quiet; it MAY be reported separately as **unanchored** (created but never advanced by a
   dated session).
6. THE signal SHALL honor visibility exactly as the storyline timeline does: a caller
   never sees storylines, facts, or sessions they could not see on the timeline, and
   Hidden truth states remain GM-only.
7. THE staleness threshold SHALL be a single configured value for the first cut (not
   per-world); see design.

### Requirement 2: Session Wrap-Up Surface

**User Story:** As a GM, after logging a session I want one screen that shows what moved,
what went quiet, and what could be organized, so closing out and nesting happen while I'm
already paying attention instead of never.

#### Acceptance Criteria

1. THE wrap-up SHALL present, for the world, these grouped sections:
   - **Advanced** — storylines the most recent session(s) touched (read-only recap).
   - **Gone quiet** — the quiet storylines from Requirement 1, each with a one-click
     action to propose closing it (Dormant or Resolved) or to dismiss it as still active.
   - **Could nest** — pending `PartOf` lineage suggestions for recent storylines, each
     acceptable/rejectable in place.
   - **Unparented arcs** — storylines created in recent sessions with no parent, each
     offering a one-click parent assignment.
2. THE wrap-up SHALL be GM-only.
3. WHEN the GM confirms wrap-up decisions, THE system SHALL apply them through the existing
   review-proposal machinery with provenance (a synthetic wrap-up source and review
   batch), never as an unattributed direct mutation.
4. THE wrap-up SHALL NOT auto-apply any status change or lineage link without an explicit
   GM action; an untouched wrap-up changes nothing.
5. WHERE there is nothing to act on (no quiet storylines, no pending lineage suggestions,
   no unparented recent arcs), THE wrap-up SHALL present a clean "nothing to wrap up" state
   rather than an empty scaffold.
6. THE wrap-up SHALL be reachable on demand by a GM, and SHALL additionally be surfaced
   when there is new session activity to review since the GM last acted on it.

### Requirement 3: Reuse, Not Reinvention

**User Story:** As the maintainer, I want the wrap-up to compose the services that already
exist, so we add a surface and a signal, not a parallel closure pipeline.

#### Acceptance Criteria

1. Closure decisions SHALL produce the same `UpdateArtifact { status }` proposals the
   storyline retrospective already produces.
2. Lineage decisions SHALL reuse the existing `PartOf` `AddRelationship` proposals /
   `SetStorylineParent` command; the wrap-up SHALL NOT introduce a second lineage model.
3. The "Advanced" and staleness reads SHALL reuse the storyline-timeline derivation of
   (storyline, session, developments), refactored into a shared component if needed rather
   than duplicated.
4. Wrap-up-originated review batches SHALL be tagged via `ReviewBatch.Kind` so they are
   distinguishable in the review queue, consistent with how the relationship backfill tags
   its batches.

## Out of Scope (first cut)

- Per-world configurable staleness thresholds (single configured value for now).
- AI inference computed live inside the card — the "Could nest" section surfaces
  *existing* pending proposals rather than running a fresh sweep on open. (Auto-triggering
  the existing sweeps to pre-populate those proposals is a separate, optional follow-on;
  see design.)
- Player-facing wrap-up. Players keep the read-only timeline; closure and curation are GM
  work.
- Dormancy of non-storyline artifacts when a parent storyline closes.
- Staleness measured in real-world days; the unit is sessions.
