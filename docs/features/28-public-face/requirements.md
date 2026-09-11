# Requirements Document

## Introduction

The public world at `/w/{slug}` was built in July and has not moved since. Everything after
feature 22 changed the member-facing surface — the 2.0 page template with its rule and rail
(24), "Played by" on an entry (23, 25), the Library row on an excerpt (26), campaigns as a
first-tier page (August) — and none of it reached the public pages. The data does: the public
API projects `PlayedBy` and the library fields already, and the same services serve both
surfaces. Only the pages stopped.

There is also a leak of intent. Today's change to the public sources list (feature 27) made
it list every party-visible source, which is right for journals and excerpts and wrong for
**reveals**: a reveal record is a synthetic party-visible source minted when a GM discloses
something to the players, carrying the GM's note to them. It was always reachable by id; now
it is listed. The GM's note to the table is not a publication.

This feature brings the public world up to the current surface, adds the campaign pages the
member surface has had for a month, and keeps reveals off the public site entirely.

The governing constraints:

- **Same shape as the member pages.** Public entry, source and campaign pages use the
  `EntityPage` template — header, rule, body, rail — and read from the same responses.
- **Nothing new crosses the visibility boundary.** Every public read still runs as Observer
  with the anonymous identity; this feature adds pages, not permissions.
- **Reveals are not public**, listed or by id.
- **The public site has no Loremaster in the rail.** Ask lives on the overview under its
  monthly cap; the rail points there when it is on.

## Requirements

### Requirement 1 — Public entry pages on the current template

**User Story:** As a visitor, I want a public codex entry to read like the entry a member sees.

#### Acceptance Criteria

1. The public entry page SHALL use `EntityPage`: the name, type and status in the header, the
   rule beneath, the body beside a rail.
2. The rail SHALL count the entry's sources, facts and relationships and list up to eight
   connected entries, each linking to its public page.
3. WHEN the entry is played by one or more characters THEN the header SHALL say "Played by"
   with the players' display names, as the member page does. The names are the public-safe
   display names the API already sends; never a username.
4. Source references SHALL NOT include references to reveal records.

### Requirement 2 — Public source pages on the current template

**User Story:** As a visitor, I want a public session or excerpt to read like the source page a
member sees.

#### Acceptance Criteria

1. The public source page SHALL use `EntityPage`, with the title, type, date and campaign in
   the header and a rail counting what the source contributed.
2. WHEN the source is a library excerpt THEN the page SHALL say which document and pages it came
   from, without a link — the Library is not public.
3. The campaign chip SHALL link to the public campaign page.

### Requirement 3 — Campaigns are on the public site

**User Story:** As a visitor, I want to see the runs of play in this world and what each one
touched.

#### Acceptance Criteria

1. The public world SHALL have a Campaigns tab listing the world's campaigns in the GM's order
   with status and dates.
2. Each campaign SHALL have a public page with the GM's introduction, the party rendering of the
   recap (never the GM one), the cast by character name with who plays them, what the campaign
   touched (party-visible entries only, linking to their public pages), and its sessions
   (linking to their public pages).
3. Both SHALL be served by anonymous, output-cached endpoints under `/api/public/worlds/{slug}`,
   running as Observer with the anonymous identity, and SHALL answer 404 for a world that is
   not public.
4. The campaign page SHALL NOT offer anything a GM does: no filing, no cast editing, no recap
   generation, no unfiled-sources offer.

### Requirement 4 — Reveals stay off the public site

**User Story:** As a GM, I want what I disclosed to my players to stay between me and my players.

#### Acceptance Criteria

1. The public sources list SHALL NOT include sources of type `Reveal`.
2. The public source detail, knowledge and locations endpoints SHALL answer 404 for a reveal, the
   same 404 as for any unavailable source.
3. Public campaign session lists SHALL NOT include reveals.
4. The rule SHALL live in one place and every public source read SHALL go through it.

### Requirement 5 — The public copy says what is there

#### Acceptance Criteria

1. The "Sessions" tab and tile SHALL be called "Sources", since the list now carries journals
   and excerpts as well as sessions; the empty state SHALL say so.
