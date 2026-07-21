# Requirements Document

## Introduction

Nornis already answers *"where has the party been, and in what order?"* — that is the Journey
view (feature 18), a map with a time-scrubber that walks the party's sessions and lights up the
pins each one touched. Journey is **time-first**: you pick a session, the map answers.

This feature is its **place-first dual**. You pick a *place* — a pin on the map — and the record
answers with everything that has happened there: the dated sessions that visited it, newest
concern last, each expanding to the artifacts it introduced or advanced. Same three axes,
pivoted the other way:

> pinned [`Location`](../../../src/Nornis.Domain/Entities/MapPlacemark.cs) (a Location artifact
> fixed to a map image at X/Y) **⋈** the [`SourceReference`](../../../src/Nornis.Domain/Entities/SourceReference.cs)
> rows that tie sessions to it **⋈** the calendar
> ([`Source.OccurredAt`](../../../src/Nornis.Domain/Entities/Source.cs))

The read model that crosses these already exists —
[`JourneyMapService`](../../../src/Nornis.Application/Services/JourneyMapService.cs) returns the
pins plus the dated, caller-visible sessions, each carrying the pins it visited and the artifacts
to show. The uncommitted Journey rework even added the exact interaction this feature needs: click
a pin on [`JourneyMap.razor`](../../../src/Nornis.Web/Components/Shared/JourneyMap.razor) and the
right-hand panel becomes a tree of the dated notes that visited that place. This feature **promotes
that interaction to its own top-level surface** and gives it a first-class home in the navigation —
and then closes the one gap the derived data leaves.

That gap is the second half of the feature. Today "a session visited this place" is *inferred*:
the AI extraction pipeline is the only author of `SourceReference` rows, so a session counts as a
visit only when the extractor happened to link its notes to the pinned Location artifact. There is
no way for a person to say "this session took place here." This feature adds one — the **first
user-authored `SourceReference` in the system** — as a manual link set from the session, reusing
the existing reference plumbing rather than a new association type. Because it reuses
`SourceReference`, the same link a user draws here also enriches the Journey view's trail for free.

Delivered in two phases, split on read versus write:

- **Phase 1 — The Locations view.** A top-level `/locations` page: a map, its pins, and a
  per-pin tree of the dated sessions that visited it. Pure read over the existing Journey read
  model. No schema change, no new endpoint — useful the moment any map has pinned, visited places.
- **Phase 2 — Marking sessions with locations.** A user-initiated link from a session to a
  Location artifact, set on the session detail page, written as an ordinary `SourceReference`. It
  feeds this page and the Journey trail identically.

The guiding principle from Journey holds — **the record already knows this; the view only shows
it** — extended by one deliberate write: letting a person correct what the extractor could only
guess. The same visibility contract as the map viewer governs the read: a player and a GM see
different histories of the same place from the same request, and neither sees a pin or a session
the other's visibility hides.

## Requirements

### Requirement 1: Browse a Place's History (Phase 1)

**User Story:** As a world member, I want to click a place on the map and see everything my party
has done there, in the order it happened, so I can reconstruct a location's story at a glance.

#### Acceptance Criteria

1. THE system SHALL provide a **Locations** top-level navigation item that routes to a dedicated
   page showing the world's map with its caller-visible location pins.
2. WHEN a pin is selected, THE system SHALL present, beside the map, a tree whose top level is the
   dated sessions that visited that place in `Source.OccurredAt` order, each expandable to **every
   artifact that session touched** — its full caller-visible set, not filtered to the selected
   place (the same scope the timeline page's place-mode tree already uses).
3. Each artifact in the tree SHALL be a link to its artifact detail and SHALL carry the same hover
   summary card ([`ArtifactTip`](../../../src/Nornis.Web/Components/Shared/ArtifactTip.razor)) used
   everywhere else in the app; the tree SHALL require no additional fetch to render it (name, type,
   and summary travel in the page's initial load).
4. WHERE a selected pin has no dated visiting session, THE system SHALL say so plainly rather than
   render an empty tree, and selecting a different pin SHALL replace the tree without a page reload.
5. THE page SHALL be place-first and **not time-bound**: pins are the entry point, no session is
   selected by default, and the view SHALL NOT present a time axis in any form — no scrubber,
   playhead, or cumulative trail (those remain Journey's). A pin SHALL carry no temporal state
   (no traveled / current / not-yet-reached) — it is simply unselected or selected. The only role
   time plays is the `OccurredAt` sort *within* a selected pin's session list (Requirement 1.2).

### Requirement 2: The View Is Visibility-Honest

**User Story:** As a GM, this page must never leak a place or a session I've kept GM-only to a
player, and my own view should include everything I can see — from the same request.

#### Acceptance Criteria

1. A pin SHALL render only when the caller may see the `Location` artifact it points at, reusing
   the existing `MapViewService` gate (`VisibilityFilter` + archived/dangling drop) unchanged.
2. A session SHALL appear under a pin only when the caller may see that source, reusing the same
   `CanSeeSource` predicate the map viewer and Journey use; an artifact SHALL appear in a session's
   subtree only when the caller may see it.
3. THE system SHALL NOT hand-roll role→scope logic; every visibility read SHALL go through
   `VisibilityFilter` and the existing source-visibility predicate.
4. A GM and a player issuing the same request MAY receive different pins, sessions, and artifacts
   from identical stored data, and this SHALL NOT be treated as an error.

### Requirement 3: Choosing the Canvas

**User Story:** As a user, when I open Locations I want a sensible map chosen for me, but I want to
switch to another map when the world has more than one.

#### Acceptance Criteria

1. WHERE no specific map is requested, THE system SHALL auto-pick the world's map with the most
   caller-visible pins, breaking ties by recency, deterministically for a given world + caller —
   the same canvas rule as Journey.
2. THE system SHALL accept an explicit map selector and return that map's pins, or a `404` when the
   source is not a caller-visible map — never a different map.
3. WHERE the world has no map with caller-visible pins, THE system SHALL present a coming-soon
   state guiding the user to load a map and pin places, and SHALL NOT error.

### Requirement 4: What "Visited This Place" Means (Phase 1)

**User Story:** As a user, I want the sessions under a pin to reflect the sessions genuinely tied
to that place, and I don't want the page to invent an association the record doesn't hold.

#### Acceptance Criteria

1. A dated session SHALL appear under a pin when that session's source has a `SourceReference`
   (`TargetType = Artifact`) to the pin's `Location` artifact — the same "visited" signal Journey
   uses, sourced from the same read model.
2. Each session SHALL appear under a given pin at most once, however many times its notes reference
   that place.
3. Every pin the page can select SHALL be present in the same page's pin set (no session tree hangs
   off a place that is not drawn).
4. WHERE a session has a null `OccurredAt`, it SHALL NOT appear in any pin's tree (it cannot be
   placed in time), consistent with Journey's timeline.

### Requirement 5: Mark a Session with a Location (Phase 2)

**User Story:** As a world member curating the record, I want to say "this session took place here"
when the AI missed it, so the place's history is what actually happened, not only what the
extractor caught.

#### Acceptance Criteria

1. THE system SHALL let a permitted user link a session to one or more `Location` artifacts, and
   unlink them, from the session's detail page — not from the Locations page itself.
2. A link SHALL be stored as an ordinary `SourceReference` (`Source → Location artifact`,
   `TargetType = Artifact`), reusing the existing reference; the feature SHALL NOT introduce a new
   association entity or a "primary vs mentioned" discriminator.
3. Linking SHALL be idempotent: linking a session to a place it is already tied to (whether by a
   prior manual link or by extraction) SHALL NOT create a duplicate and SHALL NOT error.
4. A manually linked session SHALL immediately count as a visit wherever the derived signal is
   read — both this page's per-pin tree and the Journey view's trail — with no separate code path.
5. THE link SHALL add no new visibility surface: reads SHALL remain gated on the source's and the
   artifact's own visibility exactly as in Requirement 2.
6. WHERE the target is not a `Location` artifact in the same world, or the caller may not edit the
   source, THE system SHALL reject the link (`400`/`403`) rather than create it.

### Requirement 6: Reuse, Not Reinvention

**User Story:** As the maintainer, I want Locations to compose the Journey read model, the map pin
layer, and the artifact tree already in the codebase — a new page and (Phase 2) one small write
path, not a parallel map, timeline, or association stack.

#### Acceptance Criteria

1. THE pin layer (image URL + visible pins) SHALL reuse the `MapViewService` / `MapView`
   resolution, via the Journey read model, rather than re-querying placemarks and blob SAS
   independently.
2. THE per-pin session tree SHALL be the same date-ordered "notes that visited this place"
   projection the Journey component already computes, extracted so both surfaces render one
   implementation rather than two.
3. THE artifact rows SHALL reuse `ArtifactTip` and the existing artifact-link pattern; the map pin
   rendering SHALL reuse the percentage-positioning approach of
   [`MapViewer`](../../../src/Nornis.Web/Components/Shared/MapViewer.razor) / `JourneyMap`.
4. THE Phase 2 write SHALL reuse the `SourceReference` repository and be gated by the same
   source-edit permission the source update path already enforces.

## Out of Scope (this feature)

- **A dedicated Locations read endpoint (Phase 1).** The existing `/journey` response already
  carries pins + dated visiting sessions + highlights; Phase 1 re-pivots it client-side. A bespoke
  `/locations` endpoint is only warranted if the two views' data needs diverge.
- **The time-scrubber / playhead / cumulative trail.** Those are the Journey view's time-first
  interaction. Locations is place-first and not time-bound; it draws pins and a per-pin tree, not a
  walked path, and its pins carry no temporal state.
- **Location-scoped artifacts under a session.** A session's subtree is everything that session
  touched, not the artifacts specifically tied to the selected place — matching the timeline page's
  place-mode tree. Per-place artifact filtering is a different, heavier read and is not in scope; it
  is what keeps Phase 1 a verbatim reuse of the `/journey` read model.
- **A "primary location" vs "mentioned" distinction.** The chosen model is a single, undifferentiated
  `SourceReference`; the page does not rank or type a session's places. (This is a deliberate
  trade-off — see the design's "decisions to confirm" for its one consequence, link *removal*.)
- **Removing AI-authored references as a headline feature.** Phase 2 adds *linking*; how far
  *unlinking* may reach into extractor-authored references (given they are indistinguishable from
  user links without an origin marker) is a bounded design decision, not an open-ended edit surface.
- **Placing, moving, or relabeling pins.** Pins remain the map proposal/extraction flow; Locations
  is read-only over existing pins (Phase 2 writes a *reference*, never a placemark).
- **Locations that are not pinned on the chosen map.** A session may be linked to any `Location`
  artifact, but the map only surfaces places pinned on the current canvas — consistent with today.
- **Per-player / per-character histories.** The view is party-wide, gated only by visibility.
- **Stitching multiple maps into one place index.** One map image at a time; switching maps
  switches canvases, as in Journey.
- **In-world / fictional calendar.** Sessions are ordered by the real-world `Source.OccurredAt`.
