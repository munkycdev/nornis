# Requirements Document

## Introduction

Nornis 2.0 is the interface, rethought. Every feature since July got a page and a route and
nobody designed the paths between them: on 2026-09-09 a campaign page was reachable only through
a GM-only settings panel, a character page only from its owner's profile, and five routes had no
inbound link at all. The sidebar had grown to fourteen flat entries. The chrome — a navy block
beside cream cards with serif headings on every panel — read as a dashboard for everything,
including reading, which is what the record is mostly for.

The proposal in [proposal.md](proposal.md) argued for borrowing the **shell** of the knowledge
tools people already know — every name a link, a quick switcher, breadcrumbs, backlinks as a
panel, quiet chrome — while keeping the **model** untouched: sources → artifacts → ask, and no
way to write a page. David chose **Direction B** on 2026-09-09: the same structure on paper, the
brand rules loosened. This document specifies that.

**The governing constraints:**

1. **No new way for knowledge to enter.** Nothing here adds a page a user can write, a link a user
   can type, or a field the product interprets. `product-vision.md`'s "not a wiki" stands.
2. **No visibility change.** Every page and panel renders what the reader may already see, through
   the services that already decide it. The shell moves things; it does not widen them.
3. **The record is read in long form.** Its own text is set for reading, not editing.

Delivered in four phases, each shippable alone:

- **Phase A — Wayfinding.** The regrouped sidebar, breadcrumbs, the quick switcher, the missing
  list pages, the dead routes. No visual redesign.
- **Phase B — The page template.** One template for every entity page: properties strip, sections,
  context rail.
- **Phase C — Paper.** Direction B's surface: the tinted sidebar, one accent, the reading serif,
  the sans with a voice. The version becomes 2.0 here.
- **Phase D — Dark.** A second palette.

## Requirements

### Requirement 1: Every Page Has a Door

**User Story:** As a member, I want to reach any page from the sidebar or from the page I am on,
so that features exist for me rather than for whoever knows the URL.

#### Acceptance Criteria

1. THE sidebar SHALL be grouped as **Play** (Home, Capture, Review, What you learned), **World**
   (Codex, Storylines, Timeline, Map, Library, Sources), **Table** (Campaigns, Party, Members) and,
   for GMs only, **GM** (Reveal, World memory, Settings), in that order. *(Sources added
   2026-09-10: the ledger carries the processing badge and is the door to every session; its
   omission from the first draft was an oversight.)*
2. THE Review and What-you-learned entries SHALL carry their live counts as badges, as today.
3. THERE SHALL be a **Campaigns** page listing the world's campaigns with the current campaign
   first, readable by every member; and a **Party** page listing every member's characters with
   links to their pages, readable by every member.
4. **Members** SHALL be reachable by every member as a page under Table; changing membership
   SHALL stay GM-gated exactly as it is on the Admin page today.
5. **Storylines** SHALL be its own entry, opening the storyline view that today lives inside
   Timeline. **Map** SHALL open Locations, with the journey as a mode of it. **Reveal** SHALL open
   the reveal page with the convergence ordering as its default listing.
6. EVERY member-facing route SHALL be reachable within two clicks of the sidebar. Of the routes
   with no inbound link on 2026-09-09: `/canon`, `/graph` and `/costs` are redirect stubs kept for
   old bookmarks and SHALL stay as they are; `/storylines` becomes the Storylines entry; `/import`
   (alias `/extract`, the bulk import walk) already has a door from Sources and SHALL also be
   reachable from Capture, where a person with a vault to import will look for it.
7. WHEREVER the name of a campaign, character, session, artifact, storyline or library document
   appears in the member UI, it SHALL be a link to that thing's page. The one standing exception
   is the artifact page's *played by* names, declined in feature 23 because that model also serves
   the public page.

### Requirement 2: You Can Tell Where You Are

**User Story:** As a member, I want each page to say where it sits in my world, so I can move up
and across without going back to the sidebar.

#### Acceptance Criteria

1. EVERY member page SHALL show a breadcrumb in the world's own structure: world › group or
   campaign › page. A session under a campaign reads *World › Campaign › Session*; an entity reads
   *World › Codex › Type › Name* or *World › Party › Name*.
2. EACH crumb SHALL be a link, and the world crumb SHALL open the world switcher.
3. THE breadcrumb SHALL be derived from data the page already loads; it SHALL NOT add a request.

### Requirement 3: Jump to Anything by Name

**User Story:** As a member, I want to type a name and go there, so I never have to remember
which page something was on.

#### Acceptance Criteria

1. `Ctrl+K` (and the search box pinned under the world switcher) SHALL open a quick switcher
   from any member page.
2. IT SHALL search artifacts, campaigns, characters, sessions and library documents in the
   current world, grouped by kind, and show recent items when empty.
3. `Enter` SHALL open the selected item; `Ctrl+Enter` SHALL open it in the context rail (Phase B).
4. RESULTS SHALL be filtered to the reader's visibility by the same services the pages use. A
   GM-only artifact SHALL NOT appear for a player, and its absence SHALL be indistinguishable from
   its nonexistence.
5. THE switcher SHALL be bounded: at most a fixed number of results per kind, with a link to the
   kind's own page for the rest.

### Requirement 4: One Page Template

**User Story:** As a reader, I want every entity page to be built the same way, so I learn it once.

#### Acceptance Criteria

1. ARTIFACT, storyline, source, campaign, character and library-document pages SHALL share one
   template: breadcrumb bar with actions; title; a **properties strip** of type, status,
   visibility, campaign, owner and last-updated as applicable; body sections with typographic
   headers; and a **context rail**.
2. THE context rail SHALL show, for the page's subject: what cites it (sources), what connects to
   it (entries, storylines), recent changes, and the Loremaster's handle. The Loremaster panel
   SHALL open in the rail's place.
3. THE rail SHALL collapse below a tablet width into a tab strip under the body.
4. SECTIONS SHALL be typographic, not nested cards. A card SHALL be used only for an object that
   is a card — a proposal, a snapshot, a chip.
5. NOTHING in the template SHALL widen what a page shows: every rail count and list SHALL come
   from the same visibility-filtered detail the page already renders.

### Requirement 5: Paper

**User Story:** As a reader, I want the record to read like a book and the tool to get out of the
way, so that reading is the default state.

#### Acceptance Criteria

1. THE sidebar SHALL be a tint of the page (`#F0ECE3` on `#FAF8F3`), separated by a hairline,
   not a contrasting block.
2. THERE SHALL be one accent, terracotta ink `#8B4A3C`, used for exactly two things: the active
   navigation item's icon and the primary action. Badges, eyebrows and links SHALL NOT use it;
   links SHALL be ink with an underline on hover.
3. THE record's own text — summaries, facts, sheets, digest, answers — SHALL be set in
   **Newsreader** at 16px with a measure no wider than ~680px. Titles SHALL be Newsreader at
   display sizes. Everything else — navigation, labels, pills, buttons, tables — SHALL be
   **IBM Plex Sans** at 13px, ink `#1C1F24` with `#6B7079` for secondary.
4. Cormorant Garamond and Inter SHALL no longer be loaded.
5. THE stonemark, the warm palette, the tagline and the copy voice SHALL be unchanged. The
   topographic wash SHALL remain on Welcome and the public world only.
6. `NornisTheme.cs` SHALL be the single source of the new values and `app.css` SHALL read them;
   `ci/pages/` SHALL be updated by hand as its own note requires.
7. THE public pages (Welcome, Features, About, the public world) SHALL adopt the same type and
   palette in the same phase, so a visitor and a member see one product.
8. THE version SHALL become **2.0.0** with this phase, and the change log SHALL carry a 2.0 entry
   written for readers, not for the team.

### Requirement 6: Dark

**User Story:** As a member reading at night, I want a dark theme that is the same product.

#### Acceptance Criteria

1. A dark palette SHALL exist beside the light one in `NornisTheme.cs`, with the same accent
   rule and the same type.
2. A member SHALL choose light, dark or system in Your settings; the public pages SHALL follow
   the system preference.
3. CONTRAST SHALL meet WCAG AA for body text in both themes.

## Out of Scope (this feature)

- Any editor. No block editor, no wikilinks, no user-authored pages.
- Mobile-first layouts beyond the rail collapse in Requirement 4.3. The shell stays responsive
  as today; a mobile redesign is its own feature.
- Per-character anything. See the 2026-09-09 amendment to `product-vision.md`.
- Notifications leaving the app.
- Rewriting page copy. Labels change where the navigation renames a thing; prose does not.
