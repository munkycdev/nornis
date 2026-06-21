# Security and Permissions

## Authentication

Use Auth0 for authentication.

Initial social identity provider:

```text
Discord
```

Auth0 authenticates users. Nornis authorizes users.

Do not encode campaign authorization decisions in Auth0 roles for MVP. Campaign roles live in the Nornis database.

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
3. Campaign membership for campaign-scoped resources.
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
API checks CampaignMember
    ↓
API checks role and visibility
    ↓
API performs operation
```

## Campaign Roles

```text
GM
Player
Observer
```

### GM

Can:

- Manage campaign settings.
- Invite/manage members.
- Create, edit, and review GM-visible knowledge.
- See GMOnly content.
- Accept/reject proposals that affect shared or GM campaign knowledge.
- Review all proposals in the campaign regardless of source author.

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

- View PartyVisible content according to campaign membership.

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

- `Private`: only creating user unless explicitly shared in future.
- `GMOnly`: campaign GMs only.
- `PartyVisible`: all campaign members.

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
- Validate campaign membership in application services, not only in UI.
- Do not trust client-provided user IDs.
- Derive user identity from validated JWT claims.
- Store secrets in Azure Key Vault.
- Do not log secrets, access tokens, refresh tokens, raw Authorization headers, or full AI prompts containing private campaign secrets unless explicitly configured for safe redaction.

## Audit Trail

For MVP, record enough information to know who made important changes.

Track:

- Source creator
- Proposal reviewer
- Accepted/rejected timestamp
- Artifact/fact/relationship created/updated timestamps
- User responsible for accepted change

## Public Sharing

No public anonymous campaign sharing in MVP.

Do not build public campaign browsing unless explicitly requested later.

## Campaign Invitation

Campaign invitation flow is deferred for MVP. Members will be added through direct GM action only. No invite links, email invitations, or Discord integration for member onboarding in MVP.
