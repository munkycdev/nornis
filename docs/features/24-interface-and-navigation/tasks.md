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

- [ ] C1. `NornisTheme.cs`: the 2.0 palette and typography; `app.css` tokens mirrored; theme
      tests pinning the table in `design.md`.
- [ ] C2. `App.razor`: Newsreader + IBM Plex Sans; Cormorant and Inter no longer loaded; a test
      that asserts exactly the two families.
- [ ] C3. The sidebar as a tint with a hairline; active item = accent icon + paper background;
      badges in ink.
- [ ] C4. The accent rule: primary action and active icon only. Links become ink with hover
      underline; eyebrows and badges lose gold. Sweep `app.css` for `--mud-palette-secondary`
      uses and reassign each.
- [ ] C5. `nornis-reading` applied to the record's own text: summaries, facts, sheets, digest,
      answers. Measure 680px.
- [ ] C6. Cards stop nesting: the template's sections are typographic; `nornis-card` reserved for
      proposals, snapshots, chips and the Home cards. The count of `nornis-*` overrides in
      `app.css` recorded before and after, and smaller after.
- [ ] C7. Public pages (Welcome, Features, About, public world) on the same type and palette; the
      topographic wash stays there and comes off member pages.
- [ ] C8. `ci/pages/` dashboard colours updated by hand, per its note.
- [ ] C9. `Directory.Build.props`: `MajorVersion` 2, `MinorVersion` 0. The footer reads 2.0.x.
- [ ] C10. `ui-design-system.md`: dated amendment over "Product Vibe" and "Visual Tokens" with the
      2.0 table; `product-vision.md` needs nothing — the vibe words ("solid, calm, durable") still
      hold, which is the test.
- [ ] C11. The 2.0 change-log entry, and a line on Welcome if the hero copy needs one.
- [ ] C12. Live check: every page walked once in the new surface, on a phone and a laptop.

## Phase D — Dark

- [ ] D1. `PaletteDark` in `NornisTheme.cs`; contrast assertions for every text/background pair.
- [ ] D2. `User.ThemePreference` (nullable; additive migration); `PUT /api/users/me/theme`.
- [ ] D3. Your settings: light / dark / system; `MudThemeProvider.IsDarkMode` bound; public pages
      on system preference.
- [ ] D4. Change log entry.

## Verification

- [ ] V1. Per phase: build, `dotnet test --solution Nornis.sln`, `dotnet format --verify-no-changes`.
- [ ] V2. After A: the reachability table re-run and pasted here, every route with at least one
      door.
- [ ] V3. After C: the live walk (C12) as GM and, through the two-identity recipe, as a Player.

## Deferred (explicitly not this feature)

- A mobile-first layout beyond the rail collapse.
- Keyboard navigation of the codex tree and graph.
- Pinned or favourite pages in the sidebar.
- A command palette that runs actions (the switcher opens pages only).
