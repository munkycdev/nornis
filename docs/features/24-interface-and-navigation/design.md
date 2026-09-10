# Design Document

## Overview

Four phases, all in `Nornis.Web` except one small read endpoint. The order is deliberate:
wayfinding first because it is the whole problem; the page template second because it is what
the paper surface is applied to; the surface third because it is judged best on a settled
structure; dark last because it is a second palette over a finished one.

The mockups are on the canvas *Nornis Interface Proposal* (Direction B board). The one-line rule
that governs every decision below: **borrow the shell, keep the model.**

## Where each rule lives (pre-implementation check 1)

| Rule | The one place |
| --- | --- |
| What the sidebar contains, for whom, in what order | `NavMenu.razor` — already the authority per `ui-design-system.md`'s 2026-09-09 amendment; the groups are data in one array, not markup repeated per entry |
| What a breadcrumb for a given page is | `Shared/Breadcrumb.razor`, fed a small `BreadcrumbTrail` built by the page from data it already holds; no page hand-writes crumbs |
| What the quick switcher searches and how results are gated | `GET /api/worlds/{id}/jump?q=` in a new `JumpController`, composing the existing services (`ArtifactService.SearchAsync`, `CampaignService.ListAsync`, `CharacterService.ListByWorldAsync`, `SourceService.ListAsync`, `LibraryService.ListAsync`) at the reader's role. It contributes **no visibility rule** — every list arrives filtered, like the dossier's record. |
| The page template | `Shared/EntityPage.razor` with `Title`, `Properties`, `Body` and `Rail` render fragments; pages fill fragments, never rebuild the frame |
| What the rail shows | `Shared/ContextRail.razor`, fed a `RailModel` the page builds from its already-loaded detail — counts and lists, never ids the page did not have |
| Colours, type, radii | `NornisTheme.cs` (MudBlazor) and the `:root` tokens at the top of `app.css` that mirror it — the one "mirrors" comment that stays legitimate, since MudBlazor's theme object and CSS custom properties cannot share a source |
| The version | `Directory.Build.props` `MajorVersion`/`MinorVersion` |

## Phase A — Wayfinding

### Sidebar

`NavMenu.razor`'s links become a list of `NavGroup { Label, Items }` records rendered by one
loop, with `RequiresGm` on the group. Fourteen `MudNavLink`s in a row become four groups. The
world switcher and the search box sit above the groups; *Your settings* and *Sign out* pin to the
bottom. Badges keep their current sources (`ReviewCountState`, the learned count).

Nothing routes anywhere new except the three additions below. Map → `/locations` (the journey is
already rendered there and on Timeline); Storylines → `/timeline` in storyline mode via a query,
which the page already accepts as `/storylines`; Reveal → `/convergence`, which lists the reveal
page's candidates in convergence order and links to the reveal itself.

### New list pages

- `/campaigns` — `Campaigns.razor`: the world's campaigns from `GET campaigns`, current first
  (the world's `CurrentCampaignId`), each a link to its page, with the GM's edit affordances
  reused from `WorldSettingsPanel` rather than copied.
- `/party` — `Party.razor`: every member's characters from `GET characters` (already reader
  projected — `CharacterView` hides what the reader may not see), grouped by member, each a link
  to `/characters/{id}`. Claim and create stay on the profile page; this page only reads.
- `/members` — `Members.razor`: the members list lifted from `WorldSettingsPanel` into its own
  page, the mutation controls rendered only for GMs as they are today. The Admin page keeps its
  Settings, Costs and Processing tabs and loses the members section.

### Breadcrumbs

`Breadcrumb.razor` renders a `BreadcrumbTrail` (`IReadOnlyList<(string Label, string? Href)>`).
Each page builds its trail from the detail it already has: a source with a `CampaignId` reads
*World › Campaign › Title*; an artifact *World › Codex › Type › Name*; a character *World › Party
› Name*. The world crumb opens the switcher. No page issues a request to build its trail (Req 2.3)
— if a page lacks the campaign name it needs, the API response gains it, as `SourceListItem`
already carries `CampaignName`.

### The quick switcher

`QuickSwitcher.razor` is a `MudDialog` opened by `Ctrl+K` (a single `keydown` listener in
`MainLayout`, registered through JS interop once) and by the sidebar's search box. It debounces
input, calls `GET /api/worlds/{id}/jump?q=`, and renders results grouped by kind. Empty query shows
the last eight things this member opened, kept in `localStorage` under a world-scoped key the
way `WorldState.StorageKey` is — per-viewer convenience, not state.

`JumpController` returns `JumpResponse { Groups: [{ Kind, Items: [{ Id, Name, Detail, Href }] }] }`,
at most eight per kind, each group built by the existing service at the reader's role. The
property worth a test: a `GMOnly` artifact absent for a player produces a response identical to
one where the artifact never existed (the feature-23 lesson — compare the whole response).

### Every name a link

A sweep, not a mechanism: each place a campaign, character, session, artifact, storyline or
library document name is rendered as text becomes a link to its page. The 2026-09-09 reachability
script (in the proposal) is re-run at the end of the phase and its table goes into `tasks.md`.

## Phase B — The page template

`EntityPage.razor`:

```text
<EntityPage Trail="@_trail" Title="@name" Properties="@props" Actions="…">
    <Body>   sections   </Body>
    <Rail>   <ContextRail Model="@_rail" />   </Rail>
</EntityPage>
```

The frame owns the breadcrumb bar (with the action slot right-aligned), the title, the properties
strip, and the two-column body/rail layout with the rail collapsing to a tab strip under
`Breakpoint.Md`. Pages supply content. Artifact, source, campaign, character and library-document
pages migrate one at a time, each its own commit, each visually checked.

`ContextRail` shows what its `RailModel` holds: *Mentioned in* (source count with a link to the
provenance list), *Connected* (entries and storylines, from the page's own detail), *Recent
changes* (the page's own history where it has one), and the Loremaster handle, which opens the
existing `LoremasterPanel` in the rail's column rather than as an overlay.

The rail widens nothing (Req 4.5): its model is built from the detail the page already rendered,
so a count on the rail can never exceed what the body may show. This is the same construction the
dossier's projector used, and for the same reason.

## Phase C — Paper

The values, all in `NornisTheme.cs` first and mirrored into `app.css`'s tokens:

| Token | 1.x | 2.0 |
| --- | --- | --- |
| Page | `#F5F1E9` | `#FAF8F3` |
| Sidebar | `#0E2E4A` block | `#F0ECE3` tint, `1px #E3DED3` hairline |
| Ink | `#16293B` | `#1C1F24` |
| Secondary | `#5E6B7A` | `#6B7079` |
| Lines | `#E7E0D3` | `#E3DED3` |
| Accent | gold `#C4A15A` (badges, active nav, eyebrows, links) | terracotta ink `#8B4A3C` (active icon, primary action only) |
| Display and reading serif | Cormorant Garamond | Newsreader |
| Chrome sans | Inter | IBM Plex Sans |
| Radius | 12px (cards 16px) | 6px chrome, 10px cards |

Gold survives in one place: the stonemark and the public hero, where it is the brand's own
colour rather than the interface's. `MudChip`, `MudButton` and `MudNavLink` are restyled through
the theme's typography and palette, not per-component overrides; where a component fights the
look, the shared `nornis-*` class in `app.css` wins, and the count of such classes is a number
the phase should leave smaller than it found.

Reading text is a class, `nornis-reading`, applied by the template's body slot and by the digest,
the answer and the sheet renderer — Newsreader 16px/1.55, max-width 680px. Everything else
inherits Plex from `body`.

`App.razor`'s font link changes to the two new families; the old ones are not loaded. The
version bump is one edit to `Directory.Build.props` and a change-log entry; `AppVersion` needs
nothing.

The steering amendment for `ui-design-system.md` records the palette table above and rewrites
the "Product Vibe" bullets that no longer hold ("Deep blue sidebar/navigation") with a dated
note, original text kept, as the file already does for its palette.

## Phase D — Dark

A second `PaletteDark` in `NornisTheme.cs`: page `#151719`, sidebar `#1B1E22`, ink `#ECE8E0`,
secondary `#9A9FA6`, lines `rgba(255,255,255,0.10)`, the same terracotta accent (checked for AA
on the dark page; lightened if it fails). `MudThemeProvider`'s `IsDarkMode` bound to a preference
stored per member (a new nullable `ThemePreference` on `WorldMember`? — no: on `User`, since a
theme is a person's, not a table's; one additive migration) with *system* as the default reading
`prefers-color-scheme`. Public pages read the system preference only.

## Testing strategy

- Phase A: `JumpController` authorization tests in the role matrix, plus the whole-response
  indistinguishability test. `NavMenu` render tests for the group set per role. A reachability
  test that walks every `@page` route in `Nornis.Web` and asserts each is linked from the nav or
  from at least one other page — the mechanical guard for Req 1.6, so the table cannot decay
  silently again.
- Phase B: bUnit render tests for `EntityPage` slots and the rail collapse; one migrated page's
  existing tests keep passing per migration.
- Phase C: `NornisTheme` tests pinning the token table; a test that `App.razor` loads exactly
  the two families.
- Phase D: contrast assertions over the two palettes' text/background pairs.

## Correctness properties

1. **Nothing widens.** For every page, the set of ids rendered anywhere in the template (body,
   rail, breadcrumb, switcher results) is a subset of the ids the page's visibility-filtered
   detail already contained.
2. **Indistinguishable absence.** A jump query whose only would-be hit is above the reader's
   visibility returns a response identical to the same query in a world where the hit does not
   exist.
3. **Every route has a door.** Every member `@page` route is reachable from the sidebar within
   two hops, mechanically checked.
4. **One template.** No entity page renders a title, breadcrumb or rail outside `EntityPage`.

## Design decisions (taken from the proposal's list, 2026-09-09)

1. Members under Table, GM-gated controls kept — everyone may see who is at the table.
2. Map is one entry; the journey is a mode of it.
3. Reveal is one entry; convergence is its ordering.
4. Dark waits for Phase D.
5. The steering amendment ships with Phase A (navigation) and again with Phase C (vibe/palette).
6. Theme preference lives on `User`, not `WorldMember`.
7. The stubs `/canon`, `/graph`, `/costs` stay as redirects; they cost nothing and keep bookmarks.
