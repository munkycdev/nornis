# Tasks

Nornis 2.0. Four phases, each shipping alone; A closes the wayfinding problem by itself, C is
where the version turns over. Guards are watched failing before they are trusted, as always.

## Phase A — Wayfinding

**Built 2026-09-10.** What the build changed from the spec:

- **Sources stayed in the sidebar.** Requirement 1.1's group list omitted it — an oversight in
  the spec, since the ledger carries the failed/in-flight badge and is the door to every
  session. It sits last under World. The requirement is amended in place.
- **The World Memory ring left the sidebar.** It was a GM-only badge dressed as a gauge; the
  score has its page (GM › World memory) and the sidebar is quieter for it.
- **Breadcrumbs are two layers, not one.** The layout sets a route default on every navigation
  (from `NavGroups.FindByPath`), and a page that knows more replaces it from data it already
  holds. So every page has a trail without every page writing one; the entity pages get their
  richer trails as they migrate onto the template in Phase B.
- **The top-bar artifact search is gone.** The switcher covers it from anywhere; two search
  boxes would have been one too many. `GlobalSearch.razor` is deleted.
- **The jump query is a service, not controller code.** `JumpService` composes the five owning
  services at the reader's role and is tested with substitutes, including the whole-response
  indistinguishability test. Sabotaged by returning a hidden artifact at the player's role;
  failed as intended.
- **The reachability guard counts the switcher's "see all" links as doors** — they are within
  two clicks of anywhere. The first sabotage (removing Party from the nav) therefore did not
  fire, correctly; the second (removing World memory, which only the nav reaches) failed the
  test naming `/world-memory`. A guard whose sabotage does not land is indistinguishable from
  one that works, so the second run is the one that counts.
- **Campaigns and Members are moved, not copied.** `CampaignsPanel` and `MembersPanel` are the
  settings panel's sections lifted whole; the settings tab now points at the pages. The
  member-name helpers (`MemberDisplay`) became the one place three surfaces read from.
- **Theme preference (Phase D) will not get a migration.** Applying one to production is a
  step this session cannot take unattended (see O3's close-out); the preference will live in
  the browser, per device, which is also where a theme belongs.

- [x] A1. `NavMenu.razor`: groups as data (`NavGroup` records), rendered by one loop; the four
      groups in the required order; `RequiresGm` on the GM group; badges kept. Render tests per
      role.
- [x] A2. `Campaigns.razor` at `/campaigns`: current campaign first; GM edit affordances reused
      from `WorldSettingsPanel`, not copied.
- [x] A3. `Party.razor` at `/party`: every member's characters, grouped by member, linked. Reads
      the already-projected `CharacterView`s — no new visibility logic.
- [x] A4. `Members.razor` at `/members`: lifted from `WorldSettingsPanel`; Admin loses the section.
- [x] A5. Storylines, Map and Reveal entries pointing at the existing pages; `/storylines` alias
      kept.
- [x] A6. `Breadcrumb.razor` + `BreadcrumbTrail`; every member page supplies its trail from data
      it already has. Where a page lacks a campaign name, the response gains it.
- [x] A7. `JumpController` + `GET /api/worlds/{id}/jump?q=`; role-matrix tests; **the
      whole-response indistinguishability test, sabotaged and watched failing first.**
- [x] A8. `QuickSwitcher.razor`: `Ctrl+K`, the sidebar search box, debounce, grouped results,
      recents in world-scoped `localStorage`, `Ctrl+Enter` reserved for the rail.
- [x] A9. The every-name-a-link sweep, page by page; the reachability script re-run and its table
      recorded here.
- [x] A10. **The reachability test**: walks every `@page` in `Nornis.Web`, asserts each member
      route is linked from the nav or another page. Break it by removing a link; watch it name the
      route.
- [x] A11. Import gets its door from Capture.
- [x] A12. `ui-design-system.md` Navigation section: dated amendment recording the four groups.
- [x] A13. Change log entry, written for readers.

## Phase B — The page template

**Built 2026-09-10.** Notes for the reader:

- **Three slots, not six.** `EntityPage` takes `Header`, `Body` and `Rail`. The trail is set
  through `BreadcrumbState` from each page's load, not passed as markup, and the header slot
  holds title, properties and actions together because every migrated page already rendered
  them as one block. Splitting that block into three parameters would have been a rewrite
  for no reader-visible change.
- **The rail model is two properties on the page**, `RailRows` and `RailRelated`, computed
  from the page's own loaded detail. There is no `RailModel` type: the rail is the projection
  of what the page holds, and a separate model would have been a second place for it to drift.
- **Property 1 is held by a source scan** (`RailNarrowerThanPageTests`): each migrated page's
  rail members may read only the page's own state fields and may not call the API or await.
  Sabotaged by putting `Api.` into the artifact rail; failed naming the page and the member.
  The design's "rail ids ⊆ detail ids" is what the construction guarantees; the test guards
  the construction rather than sampling it.
- **Under the tablet breakpoint the rail drops beneath the body** as a stacked column rather
  than a tab strip. A strip needs state and a click to reach the Loremaster; a stack needs a
  scroll. The scroll won.
- **Back buttons are gone from the five pages.** The breadcrumb is the way back.

- [x] B1. `EntityPage.razor` with `Trail`, `Title`, `Properties`, `Actions`, `Body`, `Rail`
      slots; rail collapses to a tab strip under `Breakpoint.Md`. bUnit render tests.
- [x] B2. `ContextRail.razor` + `RailModel`; the Loremaster panel opens in the rail's column.
- [x] B3. Migrate `ArtifactDetail` — the richest page, so the template is proven on the hardest
      case first. Visually checked live.
- [x] B4. Migrate `SourceDetail`.
- [x] B5. Migrate `CampaignDetail`.
- [x] B6. Migrate `CharacterDetail`.
- [x] B7. Migrate `LibraryDocumentDetail`.
- [x] B8. **Property 1 guard**: for each migrated page, a test that the rail model's ids are a
      subset of the page detail's ids.
- [x] B9. Change log entry.

## Phase C — Paper

**Built 2026-09-10.** Notes for the reader:

- **The palette is constants on `NornisTheme`** (`Paper`, `Ink`, `Accent`, …) and the theme is
  built from them, so the design table has one home and `ThemeTests` pins it. MudBlazor's
  `Typography` carries the two families; headings are Newsreader through the theme, not
  through a class, though `nornis-serif` stays for the places that opt in below heading level.
- **`Color.Secondary` was the gold role in 247 places.** Rather than sweep them, the palette's
  Secondary is now the secondary ink, so every quiet icon and caption went quiet at once and
  the filled "secondary" buttons on Import read as neutral actions. The stylesheet's own
  `--mud-palette-secondary` uses were reassigned one by one: hover borders and quiet icons to
  the secondary ink, the drag marker and the focus ring to the accent, the landing tagline to
  `--nornis-gold`.
- **Links are ink with an underline** — `nornis-link` and MudLink both. The accent is on the
  primary button and the active nav icon; the tabs slider and the journey map's visited
  markers were the two places that argued, and the slider lost (ink) while the markers kept
  the accent as data, not chrome.
- **Cards stopped nesting by one rule, not five migrations.** Inside `EntityPage` a direct
  `nornis-card` child of the header or body loses its box and gains a rule beneath it. The
  count of `.nornis-*` selectors in `app.css` went **612 → 573**: forty-seven classes nothing
  rendered any more (the old top-bar search, the world-memory ring, the canon page, the AI
  summary block) were deleted with their rules.
- **The `onnavy` tokens became `onside`** with light values; the on-navy whites and navies
  hard-coded through the sheet were swept to palette variables, and the topographic wash came
  off the Ask hero and stayed on the public pages.
- **`nornis-reading` is applied by selector**, to every markdown surface, the digest, the
  Loremaster's answer and the editor, rather than by the template's body slot — the body also
  holds chips, switches and buttons, which stay in Plex.
- **Public pages, `ci/pages`, licences and the footer version** all follow; the footer reads
  2.0.x from `Directory.Build.props`. The Welcome hero copy needed no line.
- **Live check** on the laptop width: Home, Codex entry, Source, Campaign, Character, Library
  document, Welcome; on the phone width: the character page. The phone trail had no room for
  the world's name, so on phones the world crumb is hidden.

- [x] C1. `NornisTheme.cs`: the 2.0 palette and typography; `app.css` tokens mirrored; theme
      tests pinning the table in `design.md`.
- [x] C2. `App.razor`: Newsreader + IBM Plex Sans; Cormorant and Inter no longer loaded; a test
      that asserts exactly the two families.
- [x] C3. The sidebar as a tint with a hairline; active item = accent icon + paper background;
      badges in ink.
- [x] C4. The accent rule: primary action and active icon only. Links become ink with hover
      underline; eyebrows and badges lose gold. Sweep `app.css` for `--mud-palette-secondary`
      uses and reassign each.
- [x] C5. `nornis-reading` applied to the record's own text: summaries, facts, sheets, digest,
      answers. Measure 680px.
- [x] C6. Cards stop nesting: the template's sections are typographic; `nornis-card` reserved for
      proposals, snapshots, chips and the Home cards. The count of `nornis-*` overrides in
      `app.css` recorded before and after, and smaller after.
- [x] C7. Public pages (Welcome, Features, About, public world) on the same type and palette; the
      topographic wash stays there and comes off member pages.
- [x] C8. `ci/pages/` dashboard colours updated by hand, per its note.
- [x] C9. `Directory.Build.props`: `MajorVersion` 2, `MinorVersion` 0. The footer reads 2.0.x.
- [x] C10. `ui-design-system.md`: dated amendment over "Product Vibe" and "Visual Tokens" with the
      2.0 table; `product-vision.md` needs nothing — the vibe words ("solid, calm, durable") still
      hold, which is the test.
- [x] C11. The 2.0 change-log entry, and a line on Welcome if the hero copy needs one.
- [x] C12. Live check: every page walked once in the new surface, on a phone and a laptop.

## Phase D — Dark

**Built 2026-09-10.** Notes for the reader:

- **No migration, no endpoint.** The design put the preference on `User`; it lives in
  `localStorage` (`nornis:theme`, per device) instead. Half of the reason is that applying a
  migration to production is a step this session could not take unattended (see O3's
  close-out). The other half is that it is the better home: a theme belongs to the screen it
  is read on, and a phone in bed and a laptop at the table can differ. `ThemeState` holds
  the preference and the device's report and resolves them; the layouts bind the provider
  to the answer.
- **The provider's own system-watching is off** (`ObserveSystemDarkModeChange="false"`) and
  the layout watches through the provider itself, so a device flipping to dark at dusk
  cannot overwrite a member's explicit "light". Public pages bind straight to the device.
- **The accent lifts two steps after dark** (`#C0705E`) and takes the dark page as its own
  text; the design's terracotta was 2.7:1 on `#151719`, short of the 3:1 a non-text mark
  needs. `ThemeTests` computes WCAG contrast for the seven pairs a page is made of, in both
  palettes.
- **One flash on a dark device.** The interactive render is where storage and
  `prefers-color-scheme` can first be read, so a dark reader sees paper for the prerender
  before ink arrives. Painting the dark ground earlier would need the palette outside
  MudBlazor's provider; left as is.
- **The derived tokens** (`--nornis-onside-*`, the card shadow, the wash) get dark values
  from a `nornis-dark` class on the layout root. The journey map's hand-set colours were not
  revisited.

- [x] D1. `PaletteDark` in `NornisTheme.cs`; contrast assertions for every text/background pair.
- [x] D2. ~~`User.ThemePreference` (nullable; additive migration); `PUT /api/users/me/theme`.~~
      Replaced: the preference lives in the browser. See the notes.
- [x] D3. Your settings: light / dark / system; `MudThemeProvider.IsDarkMode` bound; public pages
      on system preference.
- [x] D4. Change log entry.

## Verification

- [x] V1. Per phase: build, `dotnet test --solution Nornis.sln`, `dotnet format --verify-no-changes`.
- [x] V2. After A: the reachability table re-run and pasted here, every route with at least one
      door.
- [ ] V3. After C: the live walk (C12) as GM and, through the two-identity recipe, as a Player.

## Deferred (explicitly not this feature)

- A mobile-first layout beyond the rail collapse.
- Keyboard navigation of the codex tree and graph.
- Pinned or favourite pages in the sidebar.
- A command palette that runs actions (the switcher opens pages only).
