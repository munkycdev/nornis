# Tasks

Ships in two releases. Phase B, which needs no migration, is Nornis 3.15. Phases C and D carry
the one migration (`AddWorldKeyArt`, which Dave applies before the merge) and are Nornis 3.16.

**Amended 2026-11-07 23:40Z.** Dave said at 23:09Z to implement the whole spec and merge each PR
when it's done, so Phase B went out on its own rather than waiting for key art, and took 3.15.

**Specified 2026-11-07 22:50Z. Dave chose direction A from the UX review (the world is the
hero), key art by upload or generate, and leave to amend the UI steering doc (22:26Z). The ten
surface fixes from the same review shipped separately as 3.14 (PR #123).**

## Phase A: Spec

- [x] A1. `requirements.md`, `design.md` and this file; row 42 in the features README; the
      amendment to `ui-design-system.md`.
- [x] A2. Dave reviews the key decisions in `design.md` (one generator with a shape; Prep pool
      pays; 8 images a month; key art is party-visible; fallback decided on the server).

## Phase B: Colour and pictures already in the world (no migration)

- [x] B1. Type colours: `NornisTheme` constants, `app.css` tokens, `ArtifactTypeDisplay.TypeClass`,
      `TypeTile`. Theme tests: table, 3:1, CSS mirror.
- [x] B2. Type tiles on Codex and Storyline cards, recent changes, the context rail, entry headers.
      Also the campaign pages' entry lists, and the graph's nodes and legend (see design.md's
      amendment).
- [x] B3. Prep list as a shelf of covers.
- [x] B4. Map at full width.
- [x] B5. `MinorVersion` 15, change log, Features page.

## Phase C: Key art

- [x] C1. Domain: `World.KeyArt` (owned), the working columns; migration `AddWorldKeyArt`.
- [x] C2. `IImageArtGenerator` with `ImageShape`; covers moved onto it unchanged. Tests.
- [x] C3. `WorldKeyArtService`, `TryCountKeyArtImagesAsync`, `AiOperationType.KeyArt`. Tests.
- [x] C4. API: `/key-art` endpoints, role matrix tests.
- [x] C5. Web: `KeyArtCard` in Settings, `/world-art` proxy. bUnit tests.

## Phase D: The hero

- [x] D1. `WorldHeroService` and `GET /hero` (members) and the public variant. Tests.
- [x] D2. `HeroBand` on Home; the switcher thumbnail; the public world page. bUnit tests.

## Phase E: Ship

- [ ] E1. `MinorVersion` 16; change log, Features page; validate (clean build, full test run,
      `dotnet format`), light and dark screenshots of Home, Codex, Prep and Map.
- [ ] E2. Dave applies `AddWorldKeyArt`; merge; confirm the new revision serves.
