# Design Document

## Overview

Two public endpoints, one public-surface rule, two pages rebuilt on the template, two pages
added, and copy. No domain change, no migration, no new permission: every read already exists
in `ArtifactService`, `SourceService` and `CampaignService` and already accepts the Observer
role with the anonymous identity.

## Where each rule lives (pre-implementation check 1)

| Rule | The one place |
| --- | --- |
| Which source types the public site shows | `PublicSurface.ShowsSource(SourceType)` (Api). Every public read of a source — list, detail, knowledge, locations, campaign sessions, an entry's source references — asks it. Today it excludes `Reveal`. |
| What the public may see of anything | Unchanged: the services, given `WorldRole.Observer` and `Guid.Empty`, as every existing public read does. `PublicSurface` narrows by type after visibility has been applied; it never widens. |
| The page shape | `EntityPage` + `ContextRail`, `ShowLoremaster="false"`. Public pages fill the three slots like member pages and never rebuild the frame. |
| A player's public name | Already `PlayerDisplayName` through the API's `PlayedBy` and `CharacterView.PlayerName`; the pages render what they are sent. |
| The party rendering of a recap | `CampaignRecapView.From(recap, WorldRole.Observer)` — `Content` is the party text, `PartyPreview` null. The public response carries `Content` only. |

## API

```text
GET api/public/worlds/{slug}/campaigns                      → [CampaignResponse]          cached
GET api/public/worlds/{slug}/campaigns/{campaignId}/detail  → PublicCampaignDetailResponse cached

PublicCampaignDetailResponse(
    CampaignResponse Campaign,
    IReadOnlyList<PublicCampaignCastResponse> Cast,      // (Guid Id, string Name, string PlayerName)
    IReadOnlyList<CampaignArtifactResponse> Artifacts,   // party-visible rollup, as the member page's
    int ArtifactTotalCount,
    IReadOnlyList<SourceListItemResponse> RecentSessions, // reveals removed
    int SessionCount,
    DateTimeOffset? FirstSessionAt, DateTimeOffset? LastSessionAt,
    PublicCampaignRecapResponse Recap)                   // (bool HasData, DateTimeOffset? GeneratedAt, string? Content)
```

Both resolve the world through the existing `ResolveAsync(slug)` and answer `PublicNotFound()`
otherwise. `GetDetail` calls `CampaignService.GetDetailAsync(id, world.Id, Guid.Empty,
Observer)` and `CharacterService.ProjectForReaderAsync` for the cast, then projects. The cast
response is deliberately not `CharacterResponse`: a public page needs a name and who plays it,
and nothing about sheets or links.

`PublicSurface` (Api, static):

```csharp
public static bool ShowsSource(SourceType type) => type != SourceType.Reveal;
```

Applied in `PublicController`: `ListSources` filters; `GetSource`, `GetSourceKnowledge`,
`GetSourceLocations` load the source and answer `PublicNotFound()` when it fails the rule
(the knowledge and locations reads take a source id; they check through
`SourceService.GetByIdAsync` first, which is one extra read on a cached surface); `GetDetail`
(artifact) drops source references whose type fails it — which needs the type on the
reference view: `ArtifactDetail`'s `SourceReferences` gain `SourceType`, sourced from the
join the service already makes for the title. The campaign detail filters `RecentSessions`.

`SessionCount` on the campaign is left as the service computes it; a reveal filed under a
campaign would count without being listed. Reveals are filed under no campaign, so in
practice the two agree.

## Web

- **`PublicWorldFrame`** gains a Campaigns tab between Timeline and Sources; Sessions becomes
  Sources. The overview tile likewise.
- **`PublicWorldArtifactDetail`** rebuilt on `EntityPage`: header = the member header without
  the GM controls (icon, name, type chip, status chip, "Played by" chip, confidence); body =
  summary, open questions, what's known, connections, source references, as today; rail =
  `ContextRail` with the member page's rows and related links pointed at `/w/{slug}/…`, no
  Loremaster, and a child "Ask this world" link to the overview when `world.AskEnabled`.
- **`PublicWorldSourceDetail`** likewise: header = icon, title, type, date, campaign chip
  (linking to `/w/{slug}/campaigns/{id}`); body = the Library row for an excerpt, then the
  body, link, locations and the knowledge panel as today; rail = campaign row, counts of what
  it contributed (from the knowledge panel's data), related entries.
- **`PublicWorldCampaigns`** (`/w/{slug}/campaigns`): one row per campaign, name linking to
  its page, dates, status chip. Empty state: "No campaigns yet."
- **`PublicWorldCampaignDetail`** (`/w/{slug}/campaigns/{CampaignId:guid}`): header = icon,
  name, played span, status chip, introduction; body = the story so far (party recap), cast
  chips titled "Played by …" (no link — there is no public character page), what this campaign
  touched (grouped tabs as the member page, linking to public entries), sessions (linking to
  public sources); rail = Cast / Entries / Sessions counts and the cast as related links
  without hrefs — `ContextRail.RailLink` requires one, so the cast goes in the rows instead.
- `NornisApiClient`: `GetPublicCampaignsAsync(slug)`, `GetPublicCampaignDetailAsync(slug, id)`;
  contracts `PublicCampaignDetailDto`, `PublicCampaignCastDto`, `PublicCampaignRecapDto`.
- `SourceDetailDto` already carries the library fields.

## Testing strategy

- `PublicCampaignEndpointTests` (Api): list and detail answer for a public world and 404 for a
  disabled one; the detail carries the party recap and never the GM rendering; a GM-only
  entry cited by a campaign source is absent from `Artifacts`; a reveal is absent from
  `RecentSessions`; the cast names players by display name.
- `PublicControllerTests`: a `Reveal` source seeded directly (the way `MarkSubmittedAsync`
  reaches the context) is absent from the list and 404 by id, and absent from an entry's
  source references. **Sabotage:** make `ShowsSource` return true; every one of those goes red.
- `PublicOutputCacheTests.EveryPublicGetIsCached` covers the two new GETs by construction.
- `PublicWorldPagesTests` (Web, bUnit): the public entry page renders "Played by" and a rail
  with counts; the public source page renders the Library row for an excerpt; the campaign
  page renders the recap and cast. Stubbed API, as `LearnedPageTests` does it.
- `ReachabilityTests`: the new routes have doors (the tab, the list rows).
- Live: a public world on nornis.app — entry with a played-by, an excerpt, the campaigns tab,
  a campaign page; a reveal absent from Sources.

## Design decisions

1. **Names are shown.** `PlayedBy` and `PlayerName` are display names with the public-safe
   fallback and never usernames, and a GM who publishes a world has published its party-visible
   record, which cites those characters already. If a table wants its players unnamed on the
   public site, that is a per-world switch to add, not a reason to hide the field everywhere.
2. **Reveals are excluded by type, not by visibility.** Their visibility is right — the party
   may read them. What is wrong is the audience: a reveal is addressed to the table. The rule is
   about the surface, so it lives on the surface, in the API's public controller family, and
   not in `SourceVisibilityRule`.
3. **The rail has no Loremaster.** The panel is a member conversation; public Ask is single-shot
   and capped, and it already has a home on the overview. A link there is the honest version.
4. **The cast is names only.** No public character page exists and none is added: a character
   dossier includes a sheet and snapshots, which are the player's, not the world's.
5. **Public campaign endpoints project their own response** rather than reuse
   `CampaignDetailResponse`, because that shape carries `UnfiledInSpan` and full
   `CharacterResponse`s — fields the public page must not have even when empty. What a
   response cannot carry cannot leak.
