# Security and Permissions

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

Allowed anonymous endpoints for MVP:

```text
GET /health
GET /status optional
```

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

No public anonymous world sharing in MVP.

Do not build public world browsing unless explicitly requested later.

## World Invitation

World invitation flow is deferred for MVP. Members will be added through direct GM action only. No invite links, email invitations, or Discord integration for member onboarding in MVP.
