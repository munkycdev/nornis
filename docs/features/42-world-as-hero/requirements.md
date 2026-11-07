# Requirements Document

## Introduction

On 2026-11-07 Dave said Nornis "is missing some visual impact" and asked for a UX review. The
review ([artifact](https://claude.ai/artifact/6XzEqnEse7emKkvJLQLyxS)) found the 2.0 design
system sound and the world missing from it: every page is the same white card on the same paper
with the same grey icon, so one GM's world looks like every other. The one page with impact was
the Map, because it carries the world's own art. Ten surface fixes shipped first as Nornis 3.14
(PR #123). For the rest, the review offered three directions; Dave chose **A, the world is the
hero** (22:26Z), with key art that comes from **upload or generate**, and said the UI steering
doc may change to fit it.

The direction in one line: each world gets key art, Home opens on it, and colour starts to mean
something (an entry's type) instead of decorating. The paper, ink and single terracotta accent
of 2.0 stay.

Ships as Nornis 3.15. One migration (`AddWorldKeyArt`).

## Glossary

- **Key art**: one landscape image that stands for a world, uploaded by the GM or generated on
  the world creator's image key. Shown to everyone who can see the world, including the public
  site when public access is on.
- **Hero**: the top band of Home (and of the public world page) that shows the key art behind the
  world's name.
- **Fallback art**: what the hero shows when a world has no key art: the world map the caller can
  already see on Map (feature 18's journey map), and failing that no image at all.
- **Type colour**: a muted hue per artifact type, used on that type's icon tile and kind label.

## Requirements

### Requirement 1: Key art

**User Story:** As a GM, I want to give my world a picture, so that opening it feels like opening
that world.

#### Acceptance Criteria

1. A GM SHALL be able to upload key art (PNG, JPEG or WebP, at most 10 MB) from the world's
   Settings. The API SHALL decode the upload to check that it is an image. Art narrower than 1600px
   is taken with a warning that it may look soft.
2. A GM SHALL be able to generate key art from a brief they write, on the world creator's image
   key, as two landscape options to choose from. With no key that can make images, Generate SHALL
   be off and say why. Upload still works.
3. Generated images SHALL be costed like covers (from the response's usage) and recorded against
   the world's monthly Prep pool as operation `KeyArt`. A world SHALL generate at most
   `KeyArtOptions.MaxImagesPerMonth` (8) images per calendar month, counted before the call; a
   failure or a short delivery gives the count back.
4. Choosing an option SHALL make it the key art. Generating again SHALL replace the unchosen
   options; the chosen art and at most one generation's options are kept per world.
5. The GM SHALL be able to set where the art is anchored vertically (top, centre or bottom of the
   picture), because the hero crops a landscape image to a wide band.
6. The GM SHALL be able to remove key art. Removing it deletes uploaded blobs; the hero falls back.
7. The Settings card SHALL say plainly that key art is seen by the whole party, and by anyone with
   the public link when public access is on. The brief SHALL be sent to the image model only; the
   Loremaster does not write key-art briefs in this feature.
8. Players and observers SHALL see the key art and SHALL NOT be able to change it (403 on every
   write, as with other world settings).

### Requirement 2: The hero on Home

**User Story:** As anyone at the table, I want Home to open on the world, so I know where I am at
a glance and can ask the Loremaster straight away.

#### Acceptance Criteria

1. Home SHALL open with a hero band: the art (or fallback art) behind the world's name in the
   display serif, a line of facts (game system, number of dated sessions, number of codex entries,
   when the last session took place), and the Ask box.
2. The art SHALL fade into the page so the text reads at AA in both themes. Where there is no art
   at all, the hero SHALL be typographic, with no empty image frame.
3. The fallback art SHALL come from the same read the Map page uses for the caller, so a GM-only
   map is never shown to a player through the hero.
4. The rest of Home SHALL keep its cards, under the hero, with the wrap-up, review queue, capture
   tiles and digest unchanged in behaviour.
5. The hero SHALL not delay the page: the facts and Ask box render first; the art loads lazily.

### Requirement 3: The world in the chrome

#### Acceptance Criteria

1. The world switcher SHALL show a small thumbnail of the key art (or fallback art) beside the
   world's name, and the terrain icon when there is none.
2. The public world page (`/w/{slug}`) SHALL open on the same hero when the world has key art.
   The image SHALL be served anonymously only for a world with public access on.

### Requirement 4: Colour that means something

**User Story:** As a reader of the Codex, I want to tell a person from a place from a plot at a
glance, without reading the label.

#### Acceptance Criteria

1. Each artifact type SHALL have one muted type colour, defined in `NornisTheme` for light and
   dark and mirrored into `app.css` as tokens. Unknown types use secondary ink.
2. A type colour SHALL appear only on that type's icon tile and on the small kind label, never on
   running text, buttons or links. Terracotta stays the one accent for action.
3. Each type colour SHALL reach 3:1 against the card surface in both themes (a non-text mark).
4. Codex cards, Storyline cards, Home's recent changes, the context rail's Connected list and an
   entry page's header SHALL use the type tile.

### Requirement 5: Pictures where the world already has them

#### Acceptance Criteria

1. The Prep list SHALL show adventures as book covers: the cover art when there is one (through the
   existing cover proxy), otherwise a typeset cover in ink with the title.
2. The Map page SHALL give the map the full content width, with the places panel beneath it on
   every width (or over it, on wide screens, as a panel that can be closed).

### Requirement 6: The steering doc

1. `ui-design-system.md` SHALL carry a dated amendment recording key art, type colours and the
   hero, with the original text left in place.
2. `security-and-permissions.md` needs no new anonymous family (the public image sits under
   `/api/public/worlds/{slug}/**`), but its public-surface paragraph SHALL name key art as
   something a GM publishes.

## Out of scope

- An image for each codex entry (portraits, place art). It needs its own visibility rules, since
  an image can give away what text hides. Candidate for a later feature.
- Deriving the accent colour from the key art.
- The Loremaster writing a key-art brief.
- Animated or parallax heroes.
