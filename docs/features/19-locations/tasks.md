# Tasks

**Status: shipped July 2026.** Written 2026-08-06 from the tree — this feature had no tasks
file, so what follows records what is in it rather than what was planned.

## What is built

- [x] **Locations page** — `src/Nornis.Web/Components/Pages/Locations.razor`, reached from the
  **Locations** nav entry. Pick a pin, get the dated sessions that visited it, each expanding
  into the entries that session introduced or advanced.
- [x] **Public view** — `PublicWorldLocations.razor`, party-visible material only.
- [x] **Explicit visit records** — `SourceLocationService` / `ISourceLocationService` and
  `SourceLocationsController` at
  `api/worlds/{worldId:guid}/sources/{sourceId:guid}/locations`, with GET, POST, and DELETE.
  This is the half the requirements called out as missing: a visit is recorded, not only
  inferred from what the session mentioned.
- [x] **Extraction context** — location candidates reach the extraction prompt; see
  `ExtractionServiceLocationContextTests`.
- [x] **Tests** — `SourceLocationServiceTests`, `ExtractionServiceLocationContextTests`.

## Not built

- [ ] Per-place notes or GM annotations attached to the location itself.
- [ ] Editing pins from this view (pins stay in the map proposal/extraction flow).
