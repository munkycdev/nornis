# Tasks

Ships as Nornis 3.15. One migration (`AddWorldKeyArt`), which Dave applies before the merge.

**Specified 2026-11-07 22:50Z. Dave chose direction A from the UX review (the world is the
hero), key art by upload or generate, and leave to amend the UI steering doc (22:26Z). The ten
surface fixes from the same review shipped separately as 3.14 (PR #123).**

## Phase A: Spec

- [x] A1. `requirements.md`, `design.md` and this file; row 42 in the features README; the
      amendment to `ui-design-system.md`.
- [ ] A2. Dave reviews the key decisions in `design.md` (one generator with a shape; Prep pool
      pays; 8 images a month; key art is party-visible; fallback decided on the server).

## Phase B: Colour and pictures already in the world (no migration)

- [ ] B1. Type colours: `NornisTheme` constants, `app.css` tokens, `ArtifactTypeDisplay.TypeClass`,
      `TypeTile`. Theme tests: table, 3:1, CSS mirror.
- [ ] B2. Type tiles on Codex and Storyline cards, recent changes, the context rail, entry headers.
- [ ] B3. Prep list as a shelf of covers.
- [ ] B4. Map at full width.

## Phase C: Key art

- [ ] C1. Domain: `World.KeyArt` (owned), the working columns; migration `AddWorldKeyArt`.
- [ ] C2. `IImageArtGenerator` with `ImageShape`; covers moved onto it unchanged. Tests.
- [ ] C3. `WorldKeyArtService`, `TryCountKeyArtImagesAsync`, `AiOperationType.KeyArt`. Tests.
- [ ] C4. API: `/key-art` endpoints, role matrix tests.
- [ ] C5. Web: `KeyArtCard` in Settings, `/world-art` proxy. bUnit tests.

## Phase D: The hero

- [ ] D1. `WorldHeroService` and `GET /hero` (members) and the public variant. Tests.
- [ ] D2. `HeroBand` on Home; the switcher thumbnail; the public world page. bUnit tests.

## Phase E: Ship

- [ ] E1. `MinorVersion` 15; change log, Features page; validate (clean build, full test run,
      `dotnet format`), light and dark screenshots of Home, Codex, Prep and Map.
- [ ] E2. Dave applies `AddWorldKeyArt`; merge; confirm the new revision serves.
