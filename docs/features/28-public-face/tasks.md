# Tasks

Ordered so the rule and the endpoints are proven before any page reads them. No migration.

**Status: built and shipped 2026-09-10** in one branch (`feature-28-public-face`) from a
worktree, merged as `c63410f`. What the build changed from the spec, and what it found:

- **No `SourceType` on the reference view after all.** `SourceReference.Source` is already
  loaded by the repository's `Include`, so the public projection reads the type from the
  navigation and fails closed when it is missing — and the reveal test asserts an ordinary
  citation survives, so a dropped `Include` would show up there rather than as a silent loss.
- **`CampaignDisplay`** took the member page's `PlayedSpan`, `StatusColor` and grouping so the
  two campaign pages cannot drift; `CampaignsPanel`'s own short date helper was left as it is.
- **`PublicAskLink`** is a component rather than markup repeated in three rails.
- **Sabotage fired**: `ShowsSource => true` turned exactly the three reveal assertions red
  (list-and-404, citations, campaign sessions) and nothing else.
- **Live walk, local API and Web against production, Ruins of Symbaroum at
  `/w/toasted-marshmallows`**: six tabs; the Campaigns list showed the three campaigns; The
  Bleeding Heart opened on the template with the rule, a rail of Cast 6 / Entries 152 /
  Sessions 25 and "Ask this world", a 660-word party recap, six cast chips titled "Played by …",
  34 entry links and 25 session links, and no GM control on the page. Fera's entry read
  "Character · Active · Played by munkyc" with Sources 35 / Facts 29 / Relationships 5 in the
  rail, five related entries, and no Loremaster panel. The party-shelf excerpt showed "Library
  · Ruins of Symbaroum Players Guide v1.0.2 · p. 29" with no library link and a rail of what
  it contributed; the GM-shelf excerpt answered "doesn't exist or isn't public". No reveal
  records exist in that world, so the exclusion is proven by the tests alone.
- **V2 after the deploy**: `GET /api/public/worlds/toasted-marshmallows/campaigns` on
  production returned the three campaigns, `nornis.app/w/toasted-marshmallows/campaigns`
  returned 200, and `/health` reported healthy.

## Phase A — The surface rule and the campaign endpoints

- [x] A1. `PublicSurface.ShowsSource`; applied to the public list, detail, knowledge and
      locations reads; `SourceType` on `ArtifactDetail` source references and the public entry
      projection drops what fails the rule. Tests, including the sabotage.
- [x] A2. `GET campaigns` and `GET campaigns/{id}/detail` on `PublicController`, cached;
      `PublicCampaignDetailResponse` and its parts. `PublicCampaignEndpointTests`.

## Phase B — The pages

- [x] B1. Web contracts and client methods.
- [x] B2. `PublicWorldFrame`: Campaigns tab; Sessions → Sources; overview tile and empty copy.
- [x] B3. `PublicWorldArtifactDetail` on `EntityPage` with "Played by" and the rail.
- [x] B4. `PublicWorldSourceDetail` on `EntityPage` with the Library row, the campaign link and
      the rail.
- [x] B5. `PublicWorldCampaigns` and `PublicWorldCampaignDetail`.
- [x] B6. `PublicWorldPagesTests`; `ReachabilityTests` green.
- [x] B7. Change log entry; `docs/features/README.md` row; `security-and-permissions.md`
      anonymous list note (two more GETs under the same family, same rules).

## Verification

- [x] V1. Build, `dotnet test --solution Nornis.sln`, `dotnet format --verify-no-changes`.
- [x] V2. Live on a public world after the deploy.

## Deferred (explicitly not this feature)

- A per-world switch to withhold player names from the public site.
- A public character page.
- Public campaign filtering on the Sources tab.
