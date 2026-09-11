# Tasks

Ordered so the rule and the endpoints are proven before any page reads them. No migration.

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
- [ ] V2. Live on a public world after the deploy.

## Deferred (explicitly not this feature)

- A per-world switch to withhold player names from the public site.
- A public character page.
- Public campaign filtering on the Sources tab.
