# Security and Permissions

> **Amendment (2026-08-02):** two sections at the foot of this document — "Public Sharing"
> and "World Invitation" — describe MVP deferrals that were later built. Both are corrected
> in place below rather than deleted, because the *reasoning* they carry still governs how
> those features work. The anonymous-endpoint list has grown accordingly. Everything else
> here — the role model, the visibility model, the AI rules, the secure-development rules —
> stands as written and is the authority for `[auth]` work.

> **Amendment (2026-09-10):** a third anonymous family, `/api/shelf/{code}/**`, joins the list below,
> and a "Shelf Links" section at the foot says what it serves and why it is safe. Feature 27.

> **Amendment (2026-08-02): where GM gating lives.** Role checks are enforced in the
> **application services**, which receive the acting role as a parameter (`ActingUserRole` on
> a command, or an explicit argument). Controllers resolve membership through
> `WorldMemberActionFilter` and pass what it found; they no longer re-check the role
> themselves, and services no longer re-read the membership row the filter already resolved.
> One inline controller check survives on purpose —
> `WorldMembersController.ListAddable`, whose service reads across the user table rather
> than within a world — and it is commented as deliberate.
>
> This makes the filter load-bearing: it is opt-in per controller, so
> `WorldMemberFilterCoverageTests` asserts every `{worldId}`-routed controller carries it.
> A new world-scoped controller without that attribute is an authorization hole, not a
> performance detail.

> **Amendment (2026-09-09):** "Store secrets in Azure Key Vault" under Secure Development
> Rules names a service that does not exist in this system. The secrets that remain — two Azure
> OpenAI keys and the Auth0 client secret — are Container Apps secrets, set by
> `scripts/provision-azure.ps1` from the operator's .NET user-secrets stores and never
> committed; SQL, Blob Storage and Service Bus have needed no secret since 2026-09-08, when the
> apps began reaching them as managed identities (`azure-hosting.md`). The rule's intent —
> secrets live outside the repo and outside the image — is unchanged. The neighbouring rule,
> "explicit `AllowAnonymous` only for health/status", reads as "only for the endpoints in the
> anonymous list above", which the 2026-08-02 amendment already widened.

## Authentication

Use Auth0 for authentication.

Initial social identity provider:

```text
Discord
```

Auth0 authenticates users. Nornis authorizes users.

Do not encode world authorization decisions in Auth0 roles for MVP. World roles live in the Nornis database.

## API Authentication Posture

Default API posture:

```text
Authenticated by default.
```

Anonymous endpoints are forbidden unless explicitly approved.

Allowed anonymous endpoints (updated 2026-09-10):

```text
GET  /health                              liveness — is this deploy broken
GET  /status                              dependency probes for the ops page
     /api/public/worlds/{slug}/**         the public world surface (below)
POST /api/public/worlds/{slug}/ask        public Ask, capped per world
GET  /api/shelf/{code}/**                 the party shelf behind a shelf link (below)
```

The `/api/public/**` family is anonymous by design and rate-limited as a group. It serves
only what a GM has explicitly published: a world with `PublicAccessEnabled` and a slug, and
within it only PartyVisible content. Public Ask additionally requires a positive
per-world monthly budget — the cap is also the switch, so it is off until a GM sets one.

Everything else requires:

1. Valid Auth0 JWT.
2. Resolved Nornis user.
3. World membership for world-scoped resources.
4. Role check for GM-only operations.
5. Visibility check for source/artifact/fact/relationship access.

## Important Rule

The Blazor app is not trusted just because it is the Blazor app.

All authorization must be enforced server-side.

## Authorization Flow

```text
Request arrives
    ↓
API validates Auth0 JWT
    ↓
API resolves or provisions Nornis User
    ↓
API checks WorldMember
    ↓
API checks role and visibility
    ↓
API performs operation
```

## World Roles

```text
GM
Player
Observer
```

### GM

Can:

- Manage world settings.
- Invite/manage members.
- Create, edit, and review GM-visible knowledge.
- See GMOnly content.
- Accept/reject proposals that affect shared or GM world knowledge.
- Review all proposals in the world regardless of source author.

### Player

Can:

- Create sources.
- View PartyVisible content.
- Manage own Private content.
- Review proposals generated from their own sources (both public and private).

Cannot:

- See GMOnly content.
- Mutate GM-only canon.
- Manage members.
- Review proposals from other users' sources.

### Observer

Can:

- View PartyVisible content according to world membership.

Cannot:

- Create sources unless explicitly changed later.
- Review proposals.
- Mutate artifacts.

## Visibility

```text
Private
GMOnly
PartyVisible
```

Rules:

- `Private`: the creating user and world GMs (matching the Source model — GMs see all
  world content, and the review flow requires it). Knowledge entities (artifacts, facts,
  relationships) carry `CreatedByUserId` for this; a Private record with no recorded
  creator is GM-only (fail closed).
- `GMOnly`: world GMs only.
- `PartyVisible`: all world members.

All visibility decisions go through `VisibilityFilter` (Nornis.Domain.Models) — do not
hand-roll role→scope mappings in services or repositories.

## AI Visibility Rules

AI extraction must preserve visibility boundaries.

- Private source creates private proposals by default.
- GMOnly source creates GMOnly proposals by default.
- PartyVisible source creates PartyVisible proposals by default.

The Ask interface must never leak GMOnly or Private information to unauthorized users.

## Secure Development Rules

- No controller/action should be anonymous by accident.
- Use authenticated-by-default middleware or policies.
- Require explicit `AllowAnonymous` only for health/status.
- Validate world membership in application services, not only in UI.
- Do not trust client-provided user IDs.
- Derive user identity from validated JWT claims.
- Store secrets in Azure Key Vault.
- Do not log secrets, access tokens, refresh tokens, raw Authorization headers, or full AI prompts containing private world secrets unless explicitly configured for safe redaction.

## Audit Trail

For MVP, record enough information to know who made important changes.

Track:

- Source creator
- Proposal reviewer
- Accepted/rejected timestamp
- Artifact/fact/relationship created/updated timestamps
- User responsible for accepted change

## Public Sharing

> **Amended 2026-08-02.** Original text: *"No public anonymous world sharing in MVP. Do not
> build public world browsing unless explicitly requested later."* It was explicitly
> requested later, and built.

A GM may publish a world at `/w/{slug}` by setting a public slug and enabling public access.
The rules that make it safe:

- **PartyVisible only.** GMOnly and Private content never crosses the public boundary, and
  Draft sources are excluded — the same `SourceVisibilityRule` the authenticated paths use,
  with an anonymous identity of `Guid.Empty`.
- **Anonymous means anonymous.** There is no user, so there is no "own Private content"
  carve-out to fall through; the empty-identity guard exists precisely to stop one.
- **Reveal records are not public** (feature 28, 2026-09-10). They are party-visible by design —
  the GM's note to the table — and addressed to the players, not to whoever holds the link.
  `PublicSurface.ShowsSource` in the API is the one place that says so, and every public read
  that can reach a source goes through it; a reveal answers the same 404 as a source that does
  not exist. The public campaign pages (`GET campaigns`, `GET campaigns/{id}/detail`) run under
  the same Observer scoping and project their own response so the GM-only parts of the member
  response have no field to travel in.
- **Public Ask is capped in money, not requests.** A per-world monthly USD budget gates it,
  and a world with no positive budget has the feature off. That is the deliberate inverse of
  the world daily budget, where zero means "no cap".
- Publishing is a GM action and reversible: clearing the slug or disabling access removes it.

## World Invitation

> **Amended 2026-08-02.** Original text: *"World invitation flow is deferred for MVP. Members
> will be added through direct GM action only. No invite links, email invitations, or Discord
> integration for member onboarding in MVP."* Invite links were built; the rest was not.

GMs may create invite links. Direct GM addition still exists alongside them.

- **Creating, listing and revoking invites is GM-only**, enforced in `WorldInviteService`.
- **An invite carries the role it grants**, chosen by the GM at creation. It cannot grant
  more than the GM could grant directly.
- **Redemption is the one world-scoped path with no membership filter in front of it**, by
  necessity: the redeemer is not a member yet. It authorizes on the invite code itself, and
  is idempotent — redeeming twice lands an existing member back in the world without
  consuming a use.
- Invites can expire and can carry a maximum use count; revoked, expired and exhausted
  invites each answer with their own error rather than a generic refusal.
- Still not built, and still not wanted without a request: email invitations and Discord
  onboarding integration.

## Shelf Links

> **Added 2026-09-10 (feature 27).**

A GM may mint a shelf link for a player who is not on Nornis — a person at the table with
no account, and possibly no ability to agree to one. The link opens the world's party shelf,
the Library as a member with the Player role sees it, and nothing else.

- **The code is the credential.** `/api/shelf/{code}/**` is anonymous and rate-limited with
  the public family. It serves the shelf and a document's short-lived read URL, both through
  `LibraryService` acting as `WorldRole.Player`, so the scope rule has one home and a GM-shelf
  document is unreachable by any id. No Ask, no codex, no write.
- **Unknown and revoked codes answer one and the same 404.** No existence oracle.
- **Not output-cached.** A revocation lands on the next request, not at the end of a window.
- **Minting, listing and revoking are GM-only**, enforced in `ShelfLinkService`. Only a player
  without a membership can hold one (409 `player_linked` otherwise — a member has the Library
  page); one standing link per player, enforced by a filtered unique index; minting again
  rotates; there is no expiry, so revocation is the one control and `LastUsedAt` the visibility.
- **Closed distribution, not publication.** The page carries `noindex, nofollow`; nothing on the
  public world links to a shelf; codes are never logged and appear only in GM responses.
- **A link goes with its player.** The row cascades from `Player`, so the claim-or-link merge of
  feature 25 retires it with the unlinked row. Not exported with a world.
