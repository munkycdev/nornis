# Tasks

Ordered so the read model lands and is provable before any pixels, and Phase 1 (the journey
over one auto-picked map) ships independently of Phase 2 (designated world map + richer visits).
Tests accompany each service/endpoint per repo rules; visibility and read-model shape are the
priority coverage.

**Status: shipped July 2026** — corrected 2026-08-06, when this file still read "Not started"
against a feature that had been live for two weeks. What is in the tree:

- **Phase B and C built.** `JourneyMapService` / `IJourneyMapService`, `JourneyResponse` and its
  nested DTOs, and `JourneyController` at `api/worlds/{worldId:guid}/journey`. Covered by
  `JourneyMapServiceTests`, `JourneyEndpointTests`, and `PublicJourneyEndpointTests`.
- **Phase D built, in a different place than planned.** `JourneyMap.razor` is a shared
  component rendered on **Timeline** and on the public world's timeline. There is no `/journey`
  page and no Journey nav entry; the nav entry that exists is **Locations** (feature 19).
- **Phase A not done.** The date-axis math still lives in `StorylineTimelineChart.razor`; it was
  never extracted into a shared helper.
- **Phase E not done.** `World.PrimaryMapAttachmentId` does not exist, so map selection is still
  the auto-pick heuristic, and visits are not relationship-expanded.

The checkboxes below are the original plan and were not kept current. This status is the
authority; the boxes record what was intended.

## Phase A — Shared calendar axis (Phase 1 foundation, Req 6.3)

- [ ] A1. Extract the date-axis math from `StorylineTimelineChart` (`MinDate`/`MaxDate`/
  `SpanDays`/`PxPerDay`/`X(date)`, month ticks) into a small reusable helper both the storyline
  timeline and the journey scrubber call — no behavior change to the existing chart.
- [ ] A2. Unit-test the helper in isolation (date→x monotonic, month-tick generation, single-
  session and empty ranges).

## Phase B — JourneyMapService + read model (Requirements 1–5)

- [ ] B1. Models (Nornis.Application): `JourneyMap`, `JourneyLocation`, `JourneyStop`,
  `JourneyHighlight`; `IJourneyMapService` with `GetJourneyAsync(worldId, mapSourceId?, userId,
  role, ct)`.
- [ ] B2. Canvas picker — pure, testable function: given the world's `MapImage` attachments and
  each one's caller-visible pin count, pick most-pins-then-most-recent; honor an explicit
  `mapSourceId`; `no_map` when none visible (Req 4).
- [ ] B3. `JourneyMapService.GetJourneyAsync`: resolve canvas via B2; build the Locations layer
  by reusing `MapViewService` pin resolution (image SAS + `VisibilityFilter` pins); walk each
  pinned Location's `SourceReference`s to the sessions that touched it; keep caller-visible,
  dated sessions (`CanSeeSource`, non-null `OccurredAt`); order by `OccurredAt` then `CreatedAt`;
  assemble one `JourneyStop` per session with de-duped `VisitedLocationIds` and visible
  `Highlights` (Location/Event/Item/Character the session introduced or advanced, `FirstSeen`
  flag). DI registered.
- [ ] B4. `JourneyMapServiceTests` (per Design "Testing"): auto-pick picks richest visible map;
  `mapSourceId` override resolves or 404s; player vs GM get different stop/pin sets from
  identical data; stops `OccurredAt`-ordered with stable tiebreak; undated sessions excluded +
  counted; location referenced twice in one session → one membership; dangling/archived pin
  drops; `FirstSeen` marks earliest visible referencing session.
- [ ] B5. Property tests (FsCheck) for Correctness Properties 2, 4, 5 over random visible
  subgraphs of pins × sessions.

## Phase C — API (Requirements 1–4)

- [ ] C1. `JourneyResponse` contract (+ nested location/stop/highlight DTOs) in
  `Nornis.Api/Contracts/Responses`; client `Contracts.cs` mirror + `NornisApiClient.GetJourneyAsync`.
- [ ] C2. `GET /api/worlds/{worldId}/journey?mapSourceId={optional}` on the sources/world
  controller; any world member; maps `no_map` → 404; role + userId flow into the service.
- [ ] C3. Controller/authorization tests: non-member 404; `no_map` 404 when a world's only pins
  are GM-only and the caller is a player; GM round-trip proving a GM-only stop is present for the
  GM and absent for a player on the same map; `mapSourceId` to a non-map/invisible source 404s.

## Phase D — UI (Requirements 1, 3, 4)

- [ ] D1. `JourneyMap.razor`: map layer = `MapViewer`'s image + percentage-positioned pins,
  extended with traveled/current/not-yet-reached pin states driven by `_stopIndex`, plus an SVG
  `<polyline>` trail through visited-pin centroids for stops `0..i`. Pure projection helpers
  (`TrailThrough(i)`, `PinState(id, i)`) for unit-testability.
- [ ] D2. Scrubber: single-lane SVG on the shared calendar axis (Phase A); session ticks; a
  draggable playhead (`role="slider"`, `aria-valuenow`, arrow-key stepping) that snaps to the
  nearest stop, using the `nornis-timeline.js` drag lifecycle.
- [ ] D3. Stop panel: `Stops[i].Highlights` as a compact linked list; `FirstSeen` renders the
  "first visit" affordance; empty/undated states + "N sessions not shown" note (Req 3.3/3.4).
- [ ] D4. `/journey` page wired to `WorldState` + `NornisApiClient`; a **Journey** entry point
  from `SourceDetail`'s map view; `no_map` → coming-soon state.
- [ ] D5. Live-app verification behind Auth0 (or a controller round-trip stand-in, as the wrap-up
  feature did, if the preview can't reach a seeded world with a pinned, dated map).

## Phase E — Designated world map + richer visits (Phase 2)

- [ ] E1. Nullable `World.PrimaryMapAttachmentId` (additive migration) + GM "set as world map"
  action; canvas picker prefers it over the heuristic when set.
- [ ] E2. Widen "visited" (opt-in) to locations reached transitively via an `ArtifactRelationship`
  from a session-touched `Event`/`Character` to a `Location`; guard against noise.
- [ ] E3. Tests for both: designated map wins over heuristic; relationship-expanded visits appear
  only when enabled and stay visibility-honest.

## Deferred (explicitly not this feature)

- [ ] In-world / fictional calendar mapping (axis stays real-world `OccurredAt`).
- [ ] Editing pins from the journey view (stays in the map proposal/extraction flow).
- [ ] Autoplay / animated playback of the playhead.
- [ ] Stitching multiple maps into one continuous journey.
- [ ] Per-player / per-character trails.

## Decisions (resolved in design)

1. Canvas — **auto-pick richest visible map** + `mapSourceId` override now; GM-designated world
   map deferred to Phase 2. No migration in Phase 1.
2. "Visited" — **direct `SourceReference`-to-Location** now; relationship expansion in Phase 2.
3. Trail — **cumulative** (all stops up to the playhead), current stop emphasized; optional
   "just this session" toggle.
4. Multi-location stops — **one cluster, no fabricated intra-session order** (Correctness
   Property 6).
5. Home — **dedicated `/journey` page**, with an entry point from `SourceDetail`'s map; not a
   third mode on `/timeline`.
6. Nature — **pure read model**; the journey writes nothing (no source, batch, or proposal).
</content>
