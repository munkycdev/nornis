# Requirements Document

## Introduction

Half of one table is under twelve. They play every week, they have characters on the Party
page (feature 25 put them there by name), and they will never sign up for Nornis: some of
them would rather not, and none of them can agree to the terms of service. The GM has the
Player's Guide and a stack of handouts on the world's party shelf, and no way to hand them
over except a group chat full of attachments.

The public world is the wrong door. `/w/{slug}` is a publication — anonymous, cached,
crawlable — and a copyrighted sourcebook there is exactly the thing the GM does not want to
do. The party shelf, on the other hand, is a decision the GM has already made: *the party may
read this*. What is missing is a way for a person at the table who is not on Nornis to open
that shelf, as themselves, and nothing else.

This feature gives each such player a **shelf link**: a capability URL, minted by the GM on
the Party page for a player who has no account, that opens the party shelf with no login and
no terms to accept. It is handed to one named person, it can be taken back from that person,
and it opens nothing the party could not already read.

The second, smaller change in this feature is to the public world: its sessions list stops
filtering by source type. The detail endpoint already served every party-visible source by
id, and the list was pretending otherwise. Now they agree.

The governing constraints:

- **A shelf link is for a person, not a world.** It belongs to one player, it is revoked per
  player, and the GM can always answer "who has a link".
- **It opens the party shelf and nothing else.** No codex, no Ask, no mutation. What it
  shows is what a member with the Player role would see on the Library page.
- **It is closed distribution, not publication.** Unguessable, unlisted, uncached, not
  indexed, never linked from the public world.
- **It stands in for an account; it is not one.** Only a player without a membership can
  have a link, and when that player gets an account the link goes with the old row.

## Requirements

### Requirement 1 — A player without an account can have a shelf link

**User Story:** As a GM, I want to give Henry — who plays every week and is not on Nornis —
a link that opens the party shelf, so that he has the Player's Guide without signing up.

#### Acceptance Criteria

1. WHEN a GM mints a shelf link for a player THEN a `ShelfLink` SHALL be created for that
   player carrying an unguessable code, the minting user, and the time.
2. Only a GM SHALL mint, list or revoke shelf links. A Player or Observer SHALL be refused
   with 403.
3. A shelf link SHALL be minted only for a player with no membership. Minting for a linked
   player SHALL be refused with 409 `player_linked`, because that person has a better door.
4. A player SHALL have at most one standing link. Minting again SHALL revoke the standing one
   and issue a new code, so the GM can always say which link Henry has.
5. A GM SHALL be able to revoke a link. A revoked link SHALL never open again. Revoking an
   already-revoked link SHALL succeed and change nothing.
6. WHEN a player is deleted, or claimed or linked into a member (feature 25's merge, which
   deletes the unlinked row) THEN their shelf links SHALL go with them.
7. Shelf links SHALL NOT be exported with a world.

### Requirement 2 — The link opens the party shelf

**User Story:** As Henry, I want to open the link on my phone and download the Player's
Guide, without an account.

#### Acceptance Criteria

1. WHEN a standing link is opened THEN the response SHALL name the world and the player, and
   list the world's party-visible library documents that have finished uploading.
2. Documents on the GM shelf SHALL NOT appear and SHALL NOT be downloadable through a link,
   whatever id is asked for.
3. WHEN a listed document is requested for download THEN the response SHALL carry the same
   short-lived read URL a member's download does.
4. An unknown, revoked or malformed code SHALL answer 404 with one error, whichever it is —
   the surface is not an oracle for which codes exist.
5. Opening a link SHALL record when it was last used, so the GM can see whether Henry ever
   opened it.
6. The shelf SHALL offer nothing else: no artifacts, no sessions, no Ask, no writes.

### Requirement 3 — It is not a publication

**User Story:** As a GM, I want to be sure that handing Henry a link is like handing him the
book, not like posting the book.

#### Acceptance Criteria

1. The shelf endpoints SHALL be anonymous and rate-limited with the same policy as the public
   world surface.
2. Shelf responses SHALL NOT be output-cached: a revocation lands on the next request.
3. The shelf page SHALL carry `noindex, nofollow`, and nothing on the public world or any
   anonymous page SHALL link to a shelf.
4. A link's code SHALL be treated as a secret: never logged, never in a list any member other
   than a GM can read.

### Requirement 4 — The GM manages links on the Party page

**User Story:** As a GM at the table, I want to give Henry his link from the row that already
says he is not on Nornis, and take it back from the same place.

#### Acceptance Criteria

1. On the Party page, each player without a membership SHALL show, to a GM only, either a
   button to mint a link or the standing link with copy, a QR code the GM can hold up at the
   table, when it was last opened, and revoke.
2. Minting again from a row with a standing link SHALL ask first, because the old link stops
   working.
3. Revoking SHALL ask first.
4. Linked players SHALL show none of this.

### Requirement 5 — The public sessions list flows every party-visible source

**User Story:** As a visitor to a public world, I want the sessions list to show what the
detail pages already serve.

#### Acceptance Criteria

1. `GET /api/public/worlds/{slug}/sources` SHALL list every party-visible, non-draft source of
   the world regardless of type. The visibility rule is unchanged; only the type filter goes.
