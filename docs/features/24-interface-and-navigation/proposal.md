# Proposal: the interface, rethought

2026-09-09. A proposal, not yet a requirements doc — it exists to be argued with. If it is
accepted, `requirements.md`, `design.md` and `tasks.md` follow in the usual shape.

## What went wrong, measured

Every feature since July got a page and a route, and nobody designed the paths between them.
Mapping each member-facing route against what links into it (2026-09-09):

| Reachability | Routes |
| --- | --- |
| Only by URL, or through a GM-only panel | `/campaigns/{id}` (one link, inside Admin → Settings) |
| In the nav and nowhere else | Timeline, Locations, Convergence, What you learned, World memory |
| No inbound link at all | `/canon`, `/graph`, `/costs`, `/import`, `/extract`; `/storylines` only from Timeline |
| Well connected | Codex, Sources, Library, Capture, Review — the original loop |

Character pages had a single door (your own profile) until this morning. The original loop is
wired; everything built after it was bolted on beside it. The sidebar has thirteen entries in one
flat list, the steering doc's recommended nav is a memory, and five of the routes above are
probably dead code that nobody can reach to notice.

## What to borrow from Notion and Obsidian — and what not to

Both tools are wiki-first: the user authors pages, the pages *are* the knowledge, and the
sidebar is the page tree. `product-vision.md` says three times that Nornis is not that, and it
is right: in Nornis the pages are **derived** from sources, and the promise is that nothing on a
page exists without a source behind it. Borrowing their model would be a different product.

What they get right is not the model. It is the **shell** — how you move, how you find, how
much chrome stands between you and the thing you came for:

| Borrow | Why it fits Nornis | Leave behind |
| --- | --- | --- |
| **Everything is a page, and every name is a link.** A campaign, a character, a session, an entry, a storyline — each has one canonical page, and wherever its name appears it is a link there. | Nornis already has the pages. It lacks the links. | User-authored pages. There is no "new page" button; pages come from sources. |
| **A quick switcher** (`Ctrl+K`): type a name, jump to it. Search across everything, not per page. | The codex search exists; nothing jumps to a campaign, a character or a session by name. | — |
| **Breadcrumbs that say where you are** in the world's own structure: *Symbaroum › The Throne of Tears › 23 July session*. | Sources belong to campaigns; campaigns to worlds. The hierarchy exists in data and nowhere on screen. | A folder tree of arbitrary pages. The hierarchy is the domain's, not the user's. |
| **Backlinks as a first-class panel.** Obsidian's "linked mentions" is Nornis's provenance and relationships — *what cites this, what connects to this* — shown consistently, in the same place, on every page. | This is the product's core promise made visible instead of buried per page. | — |
| **Properties strip under the title.** Notion's typed properties: type, status, visibility, campaign, last updated, confidence — a scannable row, not a scattered set of chips. | Every entity has these already; each page shows them differently. | Editable free-form properties. Truth state and visibility change through their own gated flows. |
| **Quiet, dense chrome.** Small type, monochrome icons, tight rows, one column of navigation, content as a page with generous margin. | The current shell is card-in-card with serif headings on every panel. It reads as a dashboard for everything, including reading. | Their neutral-grey identity. The navy, the ivory, the gold and the stonemark stay. |
| **A collapsible right rail** for context (Obsidian's right sidebar; Notion's comments/updates). | The Loremaster panel already slides in from the right. Make the rail the home for Ask, provenance and related items, so it is one gesture on every page. | — |
| **Dark mode.** | Expected by anyone who has used either tool at night. The design system said light-first, not light-only. | — |

The line, stated once so nobody has to relitigate it: **borrow the shell, keep the model.**
Sources → artifacts → ask is untouched. Nothing here adds a way to write a page.

## The shell

### Navigation, regrouped

One sidebar, 240px, still navy. Grouped by what you are doing, with the group labels as
Notion-style eyebrows, and the two live counts (review, what you learned) as badges:

```text
[ World switcher ▾ ]                  ← name, role, "view as player"
[ ⌘K  Search or jump to…        ]     ← the quick switcher, always visible

PLAY
  Home                                ← the dashboard: digest, current campaign, what's waiting
  Capture
  Review                          3
  What you learned                2

WORLD
  Codex
  Storylines                          ← its own entry again; today it hides inside Timeline
  Timeline
  Map                                 ← Locations + journey; one place for "where"
  Library

TABLE
  Campaigns                           ← new list page; current campaign first
  Party                               ← new: every member's characters, with links
  Members                             ← moves out of Admin; it is a table fact, not a setting

GM                                    ← only rendered for GMs
  Reveal                              ← Convergence and the reveal page, one entry
  World memory
  Settings                            ← what is left of Admin: world config, costs, processing

Your settings · Sign out              ← pinned to the bottom
```

Fourteen entries becomes four groups of three to five, and the three routes that had no door
(campaigns, party, storylines) get one.

### The page template

Every entity page — artifact, storyline, source, campaign, character, library document — is
built on one template, so the eye learns it once:

```text
Symbaroum › Codex › Characters                              ← breadcrumb, in the world's structure
Fera                                                         ← title, serif, the one place serif lives on the page
Character · Active · Party visible · The Throne of Tears · updated 3d ago    ← properties strip
[ Ask about this ] [ Reveal… ] [ ⋯ ]                        ← actions, right-aligned, role-gated

── body ─────────────────────────────────────────  ── rail ──────────────────────────
Summary                                             Mentioned in          12 sources
Facts                                        29     Connected             8 entries
Relationships                                       Storylines            2
Played by  munkyc ↗                                 Recent changes
                                                    [ Ask the Loremaster  ▸ ]
```

The rail is the backlinks panel: provenance, connections, storylines, history, and the Ask
panel's handle. It collapses on narrow screens into a tab strip under the body. The body is
sections with small-caps headers, not nested cards; a card is used only when something is
genuinely a card (a proposal, a snapshot, a chip row).

### The quick switcher

`Ctrl+K` from anywhere. Types ahead across artifacts, campaigns, characters, sessions, library
documents and pages, grouped by kind, most recent first when the box is empty. Enter opens;
`Ctrl+Enter` opens in the rail. This is the single biggest wayfinding win and the cheapest to
build: the search endpoint exists for artifacts; the other four are small lists.

### Home

The dashboard becomes Home, and answers the five questions the design system already lists —
what world am I in, what should I review, what changed, what can I ask, what needs processing —
plus one the table added since: **what campaign are we playing.** The current campaign is a
card with a *Capture a session* action; the digest sits beside it; the counts are links.

## Brand and visual language

The identity holds. What changes is the amount of it on any one screen.

| Keep | Change |
| --- | --- |
| Navy `#0E2E4A` sidebar, ivory `#F5F1E9` body, aged gold accent, the stonemark. | The sidebar narrows and quietens: 13px Inter, monochrome outlined icons, 32px rows, group eyebrows in gold at 11px. |
| Cormorant Garamond for the world name, page titles and public pages. | Serif comes off panel headers, card titles and buttons. Body chrome is Inter throughout. |
| Warm cards with the soft double shadow. | Cards stop nesting. A page body is a page, sections are typographic, and cards mark real objects. |
| 12px radius, the topographic wash on the hero. | The wash comes off member pages entirely; it stays on Welcome and the public world. |
| Light theme as the default. | A dark theme arrives as its own phase: navy body, ivory text, the same gold — the sidebar barely changes, which is the tell that the palette was built for it. |

The result should read, side by side with the current app, as the same product with the volume
turned down: less framing, more content, the mythic identity concentrated in the title, the
mark and the gold rather than spread across every panel.

## What this deliberately does not do

- No block editor, no user-authored pages, no `[[wikilinks]]` typed by users. Sources remain the
  only way knowledge enters; the character sheet stays the one uninterpreted free-form field.
- No folder tree in the sidebar. The Codex keeps its tree as a view mode inside the Codex page.
  `product-vision.md`'s "no wiki tree as primary navigation" stands and this proposal complies
  with it, which is worth saying because the tools being borrowed from do the opposite.
- No re-architecture. Every change is in `Nornis.Web`; the API grows one search endpoint over
  four small lists and nothing else. No migration.

## Phasing

Each phase ships alone and leaves the app better than it found it.

- **A — Wayfinding.** Sidebar regrouped; breadcrumbs; the quick switcher; Campaigns and Party
  list pages; Members out of Admin; the five dead routes verified and removed. No visual
  redesign yet. This alone closes the reachability table above.
- **B — The page template.** Artifact, source, campaign, character and library pages onto the
  one template with the properties strip and the rail; the Loremaster panel moves into the rail.
- **C — The quiet shell.** The typography and chrome changes above; serif retreats to titles;
  cards stop nesting; sidebar density. `NornisTheme.cs` and `app.css` carry it, with the
  design-system steering amended to match.
- **D — Dark theme.** A second palette in `NornisTheme.cs`, a toggle in Your settings, the
  public pages following the system preference.

## Decisions to make before requirements

1. **Members in the Table group or left in Admin?** Proposed: Table. Everyone may see who is at
   the table; only GMs may change it, and the page already gates that.
2. **Map as one entry or Locations and Journey as two?** Proposed: one. Both answer *where*; the
   journey is a mode of the map.
3. **Reveal as one GM entry or Convergence and Reveal as two?** Proposed: one. Convergence is
   the index into the reveal page and has no life without it.
4. **Does dark mode wait for phase D, or ride with C?** Proposed: D. C is already the largest
   visual change and should be judged on its own.
5. **Steering.** `ui-design-system.md`'s Navigation section gets a dated amendment when A ships;
   its Product Vibe section is unchanged by any of this, which is the test the proposal has to
   pass.
