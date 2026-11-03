# Design Document

## Overview

Nornis keeps its own map from Auth0 subject to user: a `UserSignIns` table, with one row per
subject. `Users.Auth0SubjectId` is dropped, so the map has one source of truth. Linking is
decided by the API alone. The Web carries a one-time link code through an Auth0 round trip
that forces the login screen. The code is issued to the session's user, and is redeemed with
the new sign-in's own access token.

```text
Profile ── GET /account/link-sign-in ──► Web ── POST /api/me/sign-ins/link (session token) ──► API
                                          │                                  ◄── { code } ──┘
                                          └─ challenge, prompt=login, code in protected state ─► Auth0
Auth0 ── /signin-oidc ──► Web OnTokenValidated
                           ├─ same session sub as at the start? else stop
                           └─ POST /api/me/sign-ins/claim (NEW token, { code }) ──► API adds the subject
                           └─ HandleResponse: the session cookie is not replaced ─► /profile?signin=…
```

## Decisions

1. **Nornis owns the map, not Auth0.** Auth0 account linking (the Management API or its
   extension) changes the primary's `sub` in every application on the shared tenant at once.
   That's the 2026-10-30 incident. Bibliotaph keeps its own `logins` table for the same reason.
2. **No linking by email.** Not every connection verifies an email address, so matching on it
   would let anyone with a matching address take over an account.
3. **A subject owned by another user is refused, not merged.** Deciding that a user is
   "empty" means reading every table that points at a user. A hand-run script did the one
   merge that was needed (2026-11-02).
4. **The code, not two tokens.** The alternative is for the Web to send both the old and new
   tokens in one call. Then the API would validate a second token from a request body, and
   an expired session token would break the link. With the code, each call carries one
   bearer, validated the usual way.
5. **The code lives in a table, hashed.** The API has no persisted Data Protection ring of its
   own (the Bibliotaph ring is for that integration only), so a protected payload wouldn't
   survive a restart or reach another replica. `SignInLinkCodes` is keyed by `UserId`, so
   it's bounded by the number of users: issuing a code replaces the user's previous one, and
   redeeming deletes it. Only the SHA-256 of the code is stored.
6. **The session isn't replaced.** The link's callback calls `HandleResponse()` in
   `OnTokenValidated`, so the cookie, its refresh token and its `sub` stay as they were. The
   link round trip asks Auth0 for `openid profile email` only, with no `offline_access`, so
   no refresh token is issued that nobody keeps.
7. **The claim endpoint skips provisioning.** `UserProvisioningMiddleware` would otherwise
   create a user for the new subject before the claim could attach it.
   `[SkipUserProvisioning]` on the action is endpoint metadata the middleware reads. The
   claim reads the subject from the validated token itself.
8. **An explicit bearer wins in the Web's HTTP client.** `BearerTokenHandler` sets the
   session's token only when the request has no `Authorization` header, so the claim can
   carry the new token from inside the callback request, which still has the old cookie.

## Data

```text
UserSignIn
- Id: Guid
- UserId: Guid → Users (cascade)
- Subject: string(200), unique
- AddedAt: DateTimeOffset

SignInLinkCode
- UserId: Guid, primary key → Users (cascade)
- CodeHash: string(64), unique (hex SHA-256)
- ExpiresAt: DateTimeOffset
```

`User.SignIns` is the navigation. Migration `AddUserSignIns` creates both tables, copies
`Users.Auth0SubjectId` into `UserSignIns` with `AddedAt = Users.CreatedAt`, and drops the
unique index and the column.

**Downtime.** The API that is running reads `Auth0SubjectId`, so applying the migration
breaks it until the new revision serves. Dave is the only user (stated 2026-11-02), so the
window is acceptable. Apply the migration, merge, and the deploy completes once the new
revision is ready.

## Where each rule lives

| Rule | The one place |
| --- | --- |
| Subject → user, and creating a user with its first sign-in | `IUserRepository.GetBySubjectAsync` / `CreateAsync(user, subject)` |
| Link, claim, list, remove, and every refusal | `SignInService` (Application) |
| A provider's display name from a subject | `SignInProviders.Name(subject)` (Application) |
| The subject cache's key and its eviction | `UserProvisioningMiddleware.CacheKey(subject)` |
| Bibliotaph Connect's subject check | `BibliotaphConnectService.CompleteAsync`, through `IUserSignInRepository` |

`SignInProviders.Name` maps `windowslive` to Microsoft, `google-oauth2` to Google,
`oauth2|discord` and `discord` to Discord, `apple` to Apple, `github` to GitHub and `auth0`
to Email. Any other prefix is shown as it is.

## API

All are under `api/me/sign-ins`, authenticated, and refuse Bibliotaph tokens (the default
policy).

| Call | Answer |
| --- | --- |
| `GET` | `[{ id, provider, addedAt, current }]`, oldest first. `current` is the token's subject. |
| `POST link` | `{ code }`, 32 random bytes, base64url. Replaces the user's previous code. |
| `POST claim` `{ code }` | `[SkipUserProvisioning]`. `{ outcome: "linked" \| "already-linked" }`, 400 `link_expired` for an unknown, used or expired code, and 409 `sign_in_taken` for a subject another user owns. A known code is burned whatever the outcome. |
| `DELETE {id}` | 204. 404 when it isn't the caller's, 409 `sign_in_in_use` for the session's own subject, and 409 `last_sign_in` for the only one. Evicts the subject from the cache. |

## Web

- `GET /account/link-sign-in` (authenticated): begins with the API, then challenges with
  `Prompt = "login"`, `Items["nornis.link-code"]` and `Items["nornis.link-from"]` (the
  session's subject), `RedirectUri = "/profile"`. A failure to begin redirects to
  `/profile?signin=failed`.
- `OnRedirectToIdentityProvider`: a link asks for `openid profile email`.
- `OnTokenValidated`: for a link, the session's subject must equal `link-from`, otherwise
  `failed`. Otherwise it claims with the new access token, then redirects to
  `/profile?signin=linked|already-linked|taken|expired|failed` and calls `HandleResponse()`.
- `OnRemoteFailure`: a link cancelled or refused at Auth0 goes to `/profile?signin=failed`
  rather than the error page.
- Profile gets a **Sign-ins** card (`SignInsCard.razor`) above the Bibliotaph card. It shows
  the list, a Remove button on every row except this session's (hidden when there is one),
  **Link another sign-in**, and the outcome from `?signin=`.

## Tests

- Migration: the copy and the drop, against SQL Server's model (model validation).
- Provisioning: an unknown subject creates a user with one sign-in; a second linked subject
  resolves to the same user; a Bibliotaph token with a linked second subject resolves and
  never creates.
- `SignInService`: link replaces the previous code; claim adds, reports already-linked,
  refuses taken, refuses expired and used; remove refuses current, last and others', and
  evicts.
- The claim endpoint creates no user for its subject.
- Bibliotaph Connect accepts a second linked subject and refuses another user's.
- Web: `SignInProviders`, the callback's outcome mapping, and an explicit bearer surviving
  `BearerTokenHandler`.
