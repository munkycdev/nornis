# Design Document

## Overview

One new entity, one additive migration, one application service that composes the Library
service it already has, two controllers (one anonymous, one GM), one anonymous page, and a
block on the Party page. Plus a one-line deletion in `PublicController`.

```text
Player (unlinked) 1 ── 0..1 standing ShelfLink   (RevokedAt IS NULL)
                            │
        GET /api/shelf/{code} ──▶ ShelfLinkService.OpenAsync
                                     │ resolves the link, stamps LastUsedAt
                                     ▼
                              LibraryService.ListAsync(worldId, WorldRole.Player)
                              LibraryService.GetDownloadAsync(docId, worldId, WorldRole.Player)
```

## Where each rule lives (pre-implementation check 1)

| Rule | The one place |
| --- | --- |
| Which documents a link shows | `LibraryService.GetAllowedScopes(WorldRole.Player)`, reached through `LibraryService.ListAsync` / `GetDownloadAsync`. `ShelfLinkService` acts as a Player and never names a scope. `ListAsync` already drops `PendingUpload` rows. |
| Whether a link opens | `ShelfLink.IsActive` (`RevokedAt is null`). `ShelfLinkService.ResolveAsync(code)` is the one path both anonymous reads take, and it is where `LastUsedAt` is stamped. |
| Who may mint, list and revoke | `ShelfLinkService.RequireGm`, the same shape as `PlayerService` and `WorldInviteService`. |
| One standing link per player | Filtered unique index `(PlayerId) WHERE RevokedAt IS NULL`, and `CreateAsync` revokes the standing one before inserting. The index is the guard; the service is the intent. |
| A link is for a player without an account | `CreateAsync` refuses `WorldMemberId is not null` with 409 `player_linked`. Feature 25's merge deletes the unlinked row on claim or link, so the FK cascade retires the link without a second rule. |
| Code shape | `IInviteCodeGenerator.Generate()` — 16 random bytes as Base64Url. It is a capability-code generator that happens to be named for its first caller; reused rather than duplicated. |
| What null means | `RevokedAt` null = standing. `LastUsedAt` null = never opened. Nothing else is nullable. |
| Not cached | No `[OutputCache]` on `ShelfController`; the response varies by a secret and a revocation must land now. The `EvictPublicCacheOnWriteFilter` is unaffected either way. |

## Domain

```csharp
ShelfLink
- Id: Guid
- PlayerId: Guid              // cascade from Player
- Code: string                // ≤ 32, unique; a capability secret, never logged
- CreatedByUserId: Guid       // Restrict, as WorldInvite.CreatedByUserId
- CreatedAt: DateTimeOffset
- RevokedAt: DateTimeOffset?  // null = standing
- LastUsedAt: DateTimeOffset? // null = never opened
- Player, CreatedByUser       // navigations
+ bool IsActive => RevokedAt is null;
```

No `WorldId`: the world is `Player.WorldId`, and a denormalised copy would be a second
cascade path from Worlds (Worlds→Players→ShelfLinks beside Worlds→ShelfLinks), which SQL Server
refuses. The repository joins through the player when it lists by world. No `ExpiresAt`: see
design decision 3.

`ShelfLinkConfiguration`: table `ShelfLinks`; `Code` required, max 32, unique index; index on
`PlayerId`; filtered unique index on `PlayerId` where `RevokedAt IS NULL`; Player FK Cascade;
CreatedByUser FK Restrict. The only cascading path into `ShelfLinks` is Worlds→Players→ShelfLinks
(WorldMembers→Players is `ClientSetNull`, so it adds none).

Migration `AddShelfLinks`: additive — one table, three indexes, two FKs. Applied to production
before the deploy, per `nornis-migrations-ops`.

`IShelfLinkRepository` (Domain):

```csharp
Task<ShelfLink> CreateAsync(ShelfLink link, ct)
Task<ShelfLink?> GetByCodeAsync(string code, ct)       // tracked; includes Player and Player.World
Task<ShelfLink?> GetByIdAsync(Guid id, ct)             // includes Player (for the world check)
Task<ShelfLink?> GetActiveByPlayerAsync(Guid playerId, ct)
Task<IReadOnlyList<ShelfLink>> ListActiveByWorldAsync(Guid worldId, ct)  // via Player.WorldId
Task<ShelfLink> UpdateAsync(ShelfLink link, ct)
```

## Application

```csharp
IShelfLinkService
  Task<AppResult<ShelfLink>> CreateAsync(Guid worldId, Guid playerId, Guid actingUserId, WorldRole actingRole, ct)
  Task<AppResult<IReadOnlyList<ShelfLink>>> ListActiveAsync(Guid worldId, WorldRole actingRole, ct)
  Task<AppResult> RevokeAsync(Guid worldId, Guid linkId, WorldRole actingRole, ct)
  Task<AppResult<Shelf>> OpenAsync(string code, ct)
  Task<AppResult<LibraryDownload>> DownloadAsync(string code, Guid documentId, ct)

record Shelf(Guid WorldId, string WorldName, string PlayerName, IReadOnlyList<LibraryDocument> Documents)
```

- `CreateAsync`: GM; load the player, 404 if missing or another world's; 409 if linked; revoke
  the standing link if any; insert with a fresh code.
- `RevokeAsync`: GM; load by id, 404 if missing or another world's (through the player);
  stamp `RevokedAt` if unset. Idempotent.
- `ResolveAsync(code)` (private): `GetByCodeAsync`; null or `!IsActive` → 404 `not_found`
  "This link is not valid." — one error for every failure; stamp `LastUsedAt` and save.
- `OpenAsync`: resolve, then `_library.ListAsync(world.Id, WorldRole.Player)`. Player name
  through `PlayerDisplayName.For(player, member: null)` — links exist only for unlinked
  players, but the name has one home and this is not a second one.
- `DownloadAsync`: resolve, then `_library.GetDownloadAsync(documentId, world.Id, WorldRole.Player)`.
  A GM-shelf document, or a document of another world, answers the library's own 404.

Depends on `ILibraryService` rather than the document repository, so the shelf cannot drift
from the Library page: same list, same filter on pending uploads, same download.

## API

```text
Anonymous, [EnableRateLimiting("public")], no output cache:
GET  api/shelf/{code}                              → ShelfResponse
GET  api/shelf/{code}/documents/{documentId}/download → LibraryDownloadResponse

GM, world-scoped (WorldMemberActionFilter):
GET    api/worlds/{worldId}/shelf-links              → [ShelfLinkResponse]   standing links only
POST   api/worlds/{worldId}/shelf-links  {playerId}  → ShelfLinkResponse     201
DELETE api/worlds/{worldId}/shelf-links/{linkId}     → 204

ShelfResponse(string WorldName, string PlayerName, IReadOnlyList<ShelfDocumentResponse> Documents)
ShelfDocumentResponse(Guid Id, string Title, string Kind, string FileName, string ContentType, long SizeBytes, int? PageCount)
ShelfLinkResponse(Guid Id, Guid PlayerId, string Code, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt)
```

`ShelfController` is the anonymous half, `ShelfLinksController` the GM half — the same split
as `InvitesController` / `WorldInvitesController`, for the same reason: the anonymous caller
has no membership to filter on. The `/api/shelf/**` family joins the anonymous-endpoint list
in `security-and-permissions.md`.

`PublicController.ListSources` loses its `Where(s => s.Type is SessionNote or ImportedNote)`.

## Web

- **`/shelf/{Code}`** in `Pages/Public` (so it inherits `AllowAnonymous`), `PublicLayout`,
  `<HeadContent>` with `noindex, nofollow`. Three states: loading; invalid ("This link isn't
  valid" — the API answers every failure the same way, so the page has one message too); the
  shelf: world name as the eyebrow, "Shared with {player}", then one row per document with
  the kind's icon, title, size and page count, and a Download button that fetches the read
  URL and opens it through `nornisUpload.open`, as the Library page does.
- **Party page**, GM only, on each unlinked player's row, under the link-to-member field:
  a "Shelf link" block. No standing link: *Give {name} a link to the shelf*. Standing link:
  the URL in a read-only field, Copy, Show QR (inline SVG from QRCoder, toggled), "Opened
  {relative}" or "Never opened", Revoke (confirm), and New link (confirm, because the old one
  stops working). The page loads `GET shelf-links` beside players when the reader is a GM.
- `NornisApiClient`: `GetShelfLinksAsync`, `CreateShelfLinkAsync`, `RevokeShelfLinkAsync`,
  `GetShelfAsync(code)`, `GetShelfDownloadAsync(code, documentId)`. The URL is
  `{Nav.BaseUri}shelf/{code}`, composed where the invite URL is.
- `ReachabilityTests`: `/shelf/{Code}` joins the exempt list — it is an entry point, not a
  destination.
- QRCoder (MIT) joins `Nornis.Web.csproj` and the Licenses page.

## Testing strategy

- `ShelfLinkTests` (Domain): `IsActive` before and after revocation.
- `ShelfLinkServiceTests` (Application, in-memory fakes): Player and Observer minting → 403;
  linked player → 409 `player_linked`; unknown player and another world's player → 404;
  minting twice leaves one standing link and the first code no longer opens; revoke is
  idempotent and the code stops opening; opening an unknown code and a revoked code fail with
  the same error code; opening lists a stored party-visible document and neither a GM-shelf one
  nor a pending upload; opening stamps `LastUsedAt`; download of a party document returns the
  URL, of a GM-shelf document 404. **Sabotage:** pass `WorldRole.GM` in `OpenAsync`; the
  GM-shelf test must go red.
- `ShelfEndpointTests` (Api, `NornisWebApplicationFactory`): GM creates a player, uploads a
  party document and a GM document through the handshake, mints a link; the anonymous client
  reads the shelf and sees one document; downloads it; a Player-role client minting → 403;
  the anonymous client on `GET shelf-links` → 401; after revoke the shelf answers 404; a
  made-up code answers 404 with the same body.
- `PublicControllerTests`: the type-filter test inverts — a journal entry is now in the list.
- `ReachabilityTests`: passes with the exemption.
- Live: on a sandbox world as GM, mint a link for an unlinked player, open it in a private
  window, download a document, revoke, reload — invalid.

## Design decisions

1. **Anchored to a Player, not a World.** A per-world secret is a password the table shares,
   and children share passwords. Per player, revocation is per person, "last opened" means
   something, and the row already exists: feature 25 put Henry on the Party page precisely so
   things like this have somewhere to hang.
2. **The shelf only.** The codex and Ask are the public surface's job, and Ask spends money.
   Letting the same link open the whole party-visible read surface is the obvious extension and
   is not this feature.
3. **No expiry.** A campaign link that quietly stops working mid-session is worse than one the
   GM revokes on purpose. Revoke is the control; `LastUsedAt` is the visibility.
4. **One standing link per player, minting rotates.** Two links for Henry means the GM cannot
   answer "which one did I send him". The filtered index makes the invariant structural.
5. **Acts as a Player, not an Observer.** The public surface runs as Observer because a
   stranger is one. Henry is a player at the table. The library's scopes do not distinguish
   the two today, and if they ever do, the link should follow the player.
6. **Only for players without an account.** A member has the Library page. A link for a
   member would be a second, weaker door to the same room, and one that survives their
   membership being removed.
7. **Not exported.** A code is a secret, and an export loaded into another world would carry
   codes that open a shelf they were never minted for.
8. **The public sessions list stops filtering by type** because the detail endpoint never did,
   and a list that hides what the page beside it serves is not a rule, it is a surprise. The
   visibility rule is untouched; a GM who does not want an excerpt on the public world puts
   the document on the GM shelf, which is where the excerpt's visibility comes from.
