# Requirements Document

## Introduction

Nornis already stores everything a "where have we been?" view needs — it just never crosses
the two axes. Maps carry pinned places: a `MapImage`
[`SourceAttachment`](../../../src/Nornis.Domain/Entities/SourceAttachment.cs) with
[`MapPlacemark`](../../../src/Nornis.Domain/Entities/MapPlacemark.cs) rows that fix a
`Location` artifact to a normalized X/Y, surfaced by
[`MapViewService`](../../../src/Nornis.Application/Services/MapViewService.cs). Sessions carry
time: [`Source.OccurredAt`](../../../src/Nornis.Domain/Entities/Source.cs), the same calendar
the storyline timeline runs on. And every artifact knows which sessions touched it through the
[`SourceReference`](../../../src/Nornis.Domain/Entities/SourceReference.cs) rows the
[`ProposalApplicator`](../../../src/Nornis.Application/Application/ProposalApplicator.cs) writes
on apply.

What is missing is the **join**: place × session × date, drawn as a path. This feature is that
join and the view over it — a map with the party's trail, a session timeline beneath it, and a
draggable playhead that walks the trail one session at a time, surfacing each session's
artifacts and events as it passes.

Like Reveal (feature 17), this is **assembly of existing mechanisms**, not new machinery. Phase
1 introduces no new entity and no migration: a read model that joins pins to the sessions that
referenced them, and a page that renders it. The guiding principle holds — **the record already
knows this; the view only shows it** — and the same visibility contract as the map viewer
governs it: a player and a GM see different journeys from the same data, and neither sees a pin
or a session the other's visibility hides.

Delivered in two phases, split on whether the canvas map is chosen by heuristic or by the GM:

- **Phase 1 — The journey over one map:** the read model, the `/journey` view, an auto-picked
  canvas with a manual override. No schema change.
- **Phase 2 — Designated world map + richer visits:** a GM-set canonical map, and optionally
  widening "visited" to locations reached transitively through relationships.

## Requirements

### Requirement 1: See the Journey (Phase 1)

**User Story:** As a world member, I want to see the places my party has been laid out on the
map in the order we reached them, so I can grasp how we've moved through the world at a glance.

#### Acceptance Criteria

1. WHERE the world has at least one map with caller-visible pins, THE system SHALL present a
   view combining (a) the map image, (b) its location pins, and (c) a time axis of the sessions
   that visited those pins, ordered by `Source.OccurredAt`.
2. THE view SHALL draw a trail connecting the visited pins in session order, so movement through
   the world is visible as a path, not a scatter of points.
3. THE system SHALL provide a draggable playhead over the time axis; moving it SHALL select a
   session ("stop") and SHALL be the sole control needed to move through the journey.
4. WHEN a stop is selected, THE system SHALL (a) distinguish its pin(s) as the current location,
   (b) render the trail up to and including that stop, and (c) list that session's visible
   artifacts and events, each linking to its detail.
5. THE playhead SHALL be operable by keyboard as well as pointer (it is a slider over the
   ordered stops).

### Requirement 2: The Trail Is Visibility-Honest

**User Story:** As a GM, my players' journey must never leak a place or a session I've kept
GM-only, and my own journey should include everything I can see — from the same view.

#### Acceptance Criteria

1. THE journey SHALL be shaped by the caller's role and id; a pin SHALL render only when the
   caller may see the `Location` artifact it points at, reusing the existing
   `MapViewService` gate (`VisibilityFilter` + archived/dangling drop).
2. A session SHALL appear as a stop only when the caller may see that source, reusing the same
   `CanSeeSource` predicate the map viewer uses.
3. THE system SHALL NOT hand-roll role→scope logic; all visibility reads SHALL go through
   `VisibilityFilter` and the existing source-visibility predicate.
4. A GM and a player issuing the same request MAY receive different journeys (different stops
   and pins) from identical stored data, and this SHALL NOT be treated as an error.

### Requirement 3: Time Semantics

**User Story:** As a user, I want the journey ordered by when sessions actually happened, and I
don't want undated sessions to silently distort or vanish from the picture.

#### Acceptance Criteria

1. THE system SHALL order stops by `Source.OccurredAt` ascending, with a stable secondary
   ordering (e.g. `CreatedAt`) so equal dates render deterministically.
2. WHERE a session has a null `OccurredAt`, THE system SHALL exclude it from the trail (it
   cannot be placed in time).
3. WHERE any visiting session is excluded for lack of a date, THE system SHALL surface a count
   of such sessions ("N sessions not shown"), so the exclusion is visible rather than silent.
4. WHERE the world has a visible map but no dated visible sessions, THE system SHALL render the
   map statically with pins and the excluded-count note, and SHALL NOT error.

### Requirement 4: Choosing the Canvas

**User Story:** As a user, when I open the journey I want a sensible map chosen for me, but I
want to switch to another map when the world has more than one.

#### Acceptance Criteria

1. WHERE no specific map is requested, THE system SHALL auto-pick the world's map with the most
   caller-visible pins, breaking ties by recency, deterministically for a given world + caller.
2. THE system SHALL accept an explicit map selector (a source id) and return that map's journey,
   or a `404` when that source is not a caller-visible map — never a different map.
3. WHERE the world has no map with caller-visible pins, THE system SHALL return `404 no_map` so
   the client can present a coming-soon state guiding the user to load a map and pin places.

### Requirement 5: What "Visited" Means

**User Story:** As a user, I want the trail to reflect where sessions genuinely took place, and
I don't want the view to invent an order of movement it cannot actually know.

#### Acceptance Criteria

1. A pinned `Location` SHALL count as visited by a session when that session's source has a
   `SourceReference` (`TargetType = Artifact`) to that Location artifact.
2. Each visited-location membership SHALL be de-duplicated: a session that references a location
   more than once SHALL contribute that location once.
3. WHERE a single session visited several pinned locations, THE system SHALL present them as one
   stop and SHALL NOT assert an order among them (sessions have no sub-`OccurredAt` granularity);
   only the order *between* stops is asserted.
4. Every location id the journey plots SHALL correspond to a pin present in the same journey's
   location set (no dangling references in the trail).

### Requirement 6: Reuse, Not Reinvention

**User Story:** As the maintainer, I want the journey to compose the map viewer, the session
walk, and the timeline axis already in the codebase — a new read model and a new page, not a
parallel map or timeline stack.

#### Acceptance Criteria

1. THE "where" layer (image URL + visible pins) SHALL reuse the `MapViewService` /
   `MapView` resolution rather than re-querying placemarks and blob SAS independently.
2. THE "which sessions touched this artifact" walk SHALL reuse the `SourceReference` pattern the
   continuity, wrap-up, and development readers already use.
3. THE calendar X-axis math (min/max date, date→x) SHALL be shared with
   [`StorylineTimelineChart`](../../../src/Nornis.Web/Components/Shared/StorylineTimelineChart.cs)
   rather than duplicated, and the pin layer SHALL reuse
   [`MapViewer`](../../../src/Nornis.Web/Components/Shared/MapViewer.razor)'s
   percentage-positioning approach.
4. THE draggable playhead SHALL follow the pointer-drag lifecycle already established by
   `nornis-timeline.js` (init / move / release / destroy), not a new interaction framework.

## Out of Scope (this feature)

- **A GM-designated world map.** Phase 1 auto-picks the richest map and allows a manual
  override. An explicit "set as world map" action (a nullable `World.PrimaryMapAttachmentId` or
  a flag on the attachment) is Phase 2 — a stability refinement, not the primitive.
- **Relationship-expanded visits.** Phase 1 counts a location as visited via a *direct*
  `SourceReference`. Reaching a location transitively (a session's `Event` that has an
  `ArtifactRelationship` to a `Location`) is a Phase 2 enrichment; it is noisier and depends on
  relationship coverage.
- **In-world / fictional calendar.** The axis is the real-world session calendar
  (`Source.OccurredAt`), consistent with the storyline timeline. Mapping to a homebrew in-world
  calendar is a separate concern.
- **Editing the map from this view.** Placing, moving, or relabeling pins remains the map
  proposal/extraction flow. The journey is read-only over existing pins.
- **Autoplay / animated playback.** The playhead is user-dragged. A "play" button that advances
  it automatically is a possible later nicety, not a requirement here.
- **Stitching multiple maps into one continuous journey.** The journey is over a single map
  image at a time; switching maps switches canvases. Cross-map continuity is not modeled.
- **Per-player / per-character trails.** The journey is party-wide, gated only by visibility —
  it does not model one character's path distinct from another's.
- **New provenance or mutation.** The journey writes nothing; it introduces no `Source`,
  `ReviewBatch`, or proposal. It is a pure read model. (Contrast Reveal, which mutates.)
</content>
