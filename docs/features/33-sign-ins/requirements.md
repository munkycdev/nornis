# Requirements Document

## Introduction

A Nornis user is one Auth0 `sub`. Each sign-in provider has its own `sub` (`oauth2|discord|…`,
`windowslive|…`, `google-oauth2|…`), and an unknown `sub` quietly creates a new, empty user.
So a person who signs in with a second provider finds no worlds. On the shared MunkyC
tenant, linking or unlinking accounts in Auth0 also changes the `sub` that every app sees.
That happened to Dave on 2026-10-30 and again on 2026-11-02. It split his record across
three users, which had to be merged by hand.

Nornis 3.3 lets one account hold several sign-ins, as Bibliotaph's `logins` table already
does. Nornis owns the mapping from `sub` to user. Auth0 still only authenticates.

## Glossary

- **Sign-in**: one Auth0 subject (`sub`) that belongs to a Nornis user. A user has one or
  more. The provider (Discord, Google, Microsoft…) is read from the subject's prefix.
- **Link**: adding a sign-in to the account that is signed in, by signing in with it once.
- **Link code**: the one-time, short-lived code that ties a link's Auth0 round trip to the
  account that started it.

## Requirements

### Requirement 1 — An account holds several sign-ins

**User Story:** As a GM who signs in with Discord at home and Google at work, I want both to
open the same worlds.

#### Acceptance Criteria

1. A user SHALL own one or more sign-ins, and a subject SHALL belong to at most one user.
2. Every authenticated API request SHALL resolve its user through the token's subject. A
   subject no user owns SHALL create a new user with that one sign-in, as an unknown subject
   does today.
3. A token Bibliotaph holds SHALL resolve through any of the user's sign-ins, and SHALL never
   create a user.
4. Connecting Bibliotaph SHALL accept a grant whose subject is any sign-in of the user who
   began, and SHALL refuse every other subject.
5. Existing users SHALL keep their current subject as their one sign-in after the migration.

### Requirement 2 — Link another sign-in

**User Story:** As a signed-in member, I want to add another way to sign in from my Profile.

#### Acceptance Criteria

1. Profile SHALL list the account's sign-ins, each with its provider and the date it was
   added, and SHALL mark the one this session uses.
2. **Link another sign-in** SHALL send the browser to Auth0 with the login screen forced
   (`prompt=login`), so a provider other than the current one can be chosen.
3. When the browser comes back, the new subject SHALL be added to the account that started,
   and the member SHALL stay signed in with the session they started from.
4. A subject that already belongs to this account SHALL report "already linked" and change
   nothing.
5. A subject that belongs to another user SHALL be refused. Nothing SHALL be merged or moved,
   and Profile SHALL say that sign-in already has its own Nornis account.
6. A link code SHALL be used at most once, SHALL expire 10 minutes after it was issued, and
   SHALL only add a sign-in to the user it was issued to. Each user SHALL have at most one
   outstanding code, and a new one replaces the old.
7. A link that comes back to a browser now signed in as someone else, or with no session,
   SHALL change nothing.
8. Nornis SHALL NOT link accounts by email address, and SHALL NOT link accounts in Auth0.

### Requirement 3 — Remove a sign-in

**User Story:** As a member, I want to remove a sign-in I no longer use.

#### Acceptance Criteria

1. A member SHALL be able to remove any of their sign-ins except the one this session uses.
2. The last sign-in SHALL NOT be removable.
3. Removing SHALL ask first, and SHALL say that signing in with that provider afterwards
   starts a new, empty account.
4. Removing a sign-in that belongs to another user, or that doesn't exist, SHALL answer 404.
5. A removed subject SHALL stop resolving to the account on the replica that removed it
   at once, and on any other replica within the 10-minute subject cache.

## Staging

- **Merging two existing users** stays a hand-run script. Linking refuses a subject that has
  its own user rather than deciding what "empty" means across the 20-odd tables that point at
  a user.
- **A sign-in's last use** is not tracked. It would mean a write every time the subject cache
  is refilled.
