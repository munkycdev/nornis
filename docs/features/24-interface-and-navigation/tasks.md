# Tasks

Nornis 2.0. Four phases, each shipping alone; A closes the wayfinding problem by itself, C is
where the version turns over. Guards are watched failing before they are trusted, as always.

## Phase A — Wayfinding

- [ ] A1. `NavMenu.razor`: groups as data (`NavGroup` records), rendered by one loop; the four
      groups in the required order; `RequiresGm` on the GM group; badges kept. Render tests per
      role.
- [ ] A2. `Campaigns.razor` at `/campaigns`: current campaign first; GM edit affordances reused
      from `WorldSettingsPanel`, not copied.
- [ ] A3. `Party.razor` at `/party`: every member's characters, grouped by member, linked. Reads
      the already-projected `CharacterView`s — no new visibility logic.
- [ ] A4. `Members.razor` at `/members`: lifted from `WorldSettingsPanel`; Admin loses the section.
- [ ] A5. Storylines, Map and Reveal entries pointing at the existing pages; `/storylines` alias
      kept.
- [ ] A6. `Breadcrumb.razor` + `BreadcrumbTrail`; every member page supplies its trail from data
      it already has. Where a page lacks a campaign name, the response gains it.
- [ ] A7. `JumpController` + `GET /api/worlds/{id}/jump?q=`; role-matrix tests; **the
      whole-response indistinguishability test, sabotaged and watched failing first.**
- [ ] A8. `QuickSwitcher.razor`: `Ctrl+K`, the sidebar search box, debounce, grouped results,
      recents in world-scoped `localStorage`, `Ctrl+Enter` reserved for the rail.
- [ ] A9. The every-name-a-link sweep, page by page; the reachability script re-run and its table
      recorded here.
- [ ] A10. **The reachability test**: walks every `@page` in `Nornis.Web`, asserts each member
      route is linked from the nav or another page. Break it by removing a link; watch it name the
      route.
- [ ] A11. Import gets its door from Capture.
- [ ] A12. `ui-design-system.md` Navigation section: dated amendment recording the four groups.
- [ ] A13. Change log entry, written for readers.

## Phase B — The page template

- [ ] B1. `EntityPage.razor` with `Trail`, `Title`, `Properties`, `Actions`, `Body`, `Rail`
      slots; rail collapses to a tab strip under `Breakpoint.Md`. bUnit render tests.
- [ ] B2. `ContextRail.razor` + `RailModel`; the Loremaster panel opens in the rail's column.
- [ ] B3. Migrate `ArtifactDetail` — the richest page, so the template is proven on the hardest
      case first. Visually checked live.
- [ ] B4. Migrate `SourceDetail`.
- [ ] B5. Migrate `CampaignDetail`.
- [ ] B6. Migrate `CharacterDetail`.
- [ ] B7. Migrate `LibraryDocumentDetail`.
- [ ] B8. **Property 1 guard**: for each migrated page, a test that the rail model's ids are a
      subset of the page detail's ids.
- [ ] B9. Change log entry.

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
