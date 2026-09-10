# Requirements Document

## Introduction

A `Character` belongs to a `WorldMember`, and a `WorldMember` is a login. That was the right
shape when every player was expected to sign in, and it has a hole the moment one does not.
Henry plays every week and has chosen not to use Nornis. Malliano, whom he plays, has nowhere
to exist: there is no row he can belong to, so the party's record of its own cast has a person
missing, and the campaign page's cast, the Party page, the artifact's "played by" line and the
quick switcher all inherit the gap.

This feature separates **who is at the table** from **who can log in**.

A **Player** is a person who plays in a world. A player may be linked to a membership, in
which case they are the same person as far as Nornis is concerned, or not, in which case they
exist only as a name the table knows. Characters belong to players. The Party page lists
players; the Members page lists accounts. When Henry one day signs up, his player is claimed
into his membership and Malliano comes with it, sheet and snapshots intact.

The governing constraint is that **nothing about authorization moves**. Membership still
governs every read and every write; a player who is not a member holds no rights, and the
rights that used to belong to a character's owning member now belong to the member the
character's player is linked to — or, while there is none, to the GM, who stands in for a
player who is not here.

Delivered in phases:

- **Phase A — The player.** The entity, the migration and backfill, the service and API, the
  Party page listing players with the GM's affordances to add, rename and remove them, and
  characters created for a player. One migration.
- **Phase B — Claiming.** A member claims an unlinked player as themselves, or the GM links one
  to a member; either way the player's characters move and the unlinked player is gone. Leaving
  the world leaves the player at the table.
- **Phase C — The ripple.** Export and demo packages, the campaign cast, the artifact's
  "played by" line, the domain-model amendment and the change log.

## Requirements

### Requirement 1: A Player Is A Person At The Table

**User Story:** As a GM, I want to record that Henry plays Malliano even though Henry has no
account, so the world's record of its own cast is complete.

#### Acceptance Criteria

1. THE system SHALL provide a `Player` entity, world-scoped, with a name and an optional link to
   exactly one `WorldMember` of the same world.
2. A `WorldMember` SHALL be linked to at most one `Player`, and every `WorldMember` SHALL have a
   linked `Player` from the moment the membership exists. Joining a world by any path — invite,
   GM addition, world creation — SHALL create the player in the one place memberships are
   created.
3. A `Character` SHALL belong to a `Player`, never directly to a `WorldMember`. The
   `Character.WorldMemberId` column SHALL be removed once every character has a player.
4. A GM SHALL be able to add an unlinked player by name, rename any unlinked player, and remove
   an unlinked player that has no characters. Removing a player that still has characters SHALL
   be refused with a conflict error that names what stands in the way; the characters are moved
   or deleted first, deliberately.
5. A linked player's display name SHALL be the member's display name, resolved by the one rule
   that already resolves member names for public-safe display. An unlinked player's display
   name SHALL be the name the GM gave it. Neither SHALL ever be a username or an email.
6. Migration SHALL create one linked player per existing member, named from the membership, and
   attach every existing character to its owner's player. No character SHALL be left without a
   player and no member without one.

### Requirement 2: Party Lists Players, Members Lists Accounts

**User Story:** As a member, I want the Party page to show everyone who plays, including the
people who are not on Nornis, and the Members page to show who can log in.

#### Acceptance Criteria

1. THE Party page SHALL list every player in the world, each with the characters they play, in
   one list with no distinction in placement between linked and unlinked players.
2. An unlinked player SHALL carry a quiet mark that they are not on Nornis; a linked player
   SHALL carry the role label the Party page shows today.
3. THE Party page SHALL give a GM the affordances of Requirement 1.4 inline; players and
   observers SHALL see none of them.
4. THE Members page SHALL be unchanged in meaning: accounts and roles. It SHALL NOT list
   characters.
5. Creating a character SHALL take a player, not a member. A member SHALL be able to create a
   character for their own player; a GM SHALL be able to create one for any player. The
   existing "for another member" path SHALL become "for another player" with the same
   GM-only rule.

### Requirement 3: Rights Follow The Link

**User Story:** As a player, I want the same rights over my characters I have today; as a GM,
I want to keep Malliano's sheet on Henry's behalf until Henry takes it over.

#### Acceptance Criteria

1. THE steward of a character SHALL be the member its player is linked to. WHERE the player is
   unlinked, every GM SHALL be a steward. This rule SHALL live in exactly one predicate and every
   ownership decision — edit, delete, sheet edit, sheet sharing, snapshot attach and detach —
   SHALL call it.
2. Sheet reading SHALL be unchanged: GM, steward, or anyone once the sheet is shared with the
   party.
3. Sheet sharing SHALL be the steward's decision. For an unlinked player's character that means
   the GM may share it; for a linked player's character it means the member alone, as today.
4. An unlinked player SHALL confer no read or write right on anyone. Only membership does.
5. `CharacterView` SHALL keep its reader-scoped shape and its two silences (`ArtifactId`,
   `SheetUpdatedAt`), carrying `PlayerId` in place of `WorldMemberId`. The whole-response
   indistinguishability tests SHALL continue to pass unchanged in intent.

### Requirement 4: Claiming And Linking

**User Story:** As Henry, having finally signed up, I want Malliano to become mine without
anyone retyping anything; as the GM, I want to do it for him if he does not think to.

#### Acceptance Criteria

1. A member who is not an Observer SHALL be able to claim an unlinked player. Claiming SHALL
   move every character of the unlinked player to the member's own player and delete the
   unlinked player. Nothing about the characters — sheets, sharing, snapshots, artifact links,
   campaign assignments — SHALL change.
2. A GM SHALL be able to link an unlinked player to any member of the world, with the same
   effect as that member claiming it.
3. Claiming and linking SHALL be one operation in the service with two callers, so the two can
   never drift.
4. WHERE the member has no display name, claiming SHALL offer nothing automatically: the
   unlinked player's name is dropped, not adopted. (Adopting it silently would make the claim
   change the member's name across the world without asking.)
5. The existing "claim an existing character" on Your settings SHALL remain, re-pointing one
   character's player rather than its member. Claiming a player SHALL sit beside it.
6. Removing a member from a world SHALL NOT delete their player or its characters. The player
   SHALL become unlinked and stay at the table. (Today the membership cascade deletes the
   characters; that stops.)

### Requirement 5: Nothing Else Changes Meaning

#### Acceptance Criteria

1. The campaign page's cast, the artifact's "played by" line, the quick switcher's character
   results and the character page's "played by" SHALL read the player's display name.
2. World export SHALL write `players.json` and `characters.json` SHALL carry `PlayerId`. The demo
   world package SHALL attach every packaged character to the demo member's player, so a
   package written before this feature still loads.
3. The public world page SHALL be unaffected: it serves no players and no characters, and the
   artifact's "played by" line it already serves names players by the public-safe rule.
4. `domain-model.md` SHALL gain a dated amendment over its `Character` and `WorldMember`
   sections; the sections themselves stay.

## Non-goals

- Players are not a permission concept. No right is ever granted to or through an unlinked
  player.
- No player profile page. A player is a row on Party; their characters are the places to go.
- No per-player visibility, notes, or contact details. A name is the whole record.
- No merging of two linked players. A member has one player; that is an invariant, not a case.

## Correctness properties

1. **Conservation.** Claiming, linking and deleting a player never changes the set of
   characters in a world, only which player each belongs to.
2. **One steward rule.** Every ownership decision in `CharacterService` goes through the single
   predicate; sabotaging it (widening to "any member") fails the sheet-rights tests, not just
   one of them.
3. **One player per member.** A unique index on `(WorldId, WorldMemberId)` where not null holds
   it in the database; a source scan holds it in the code by asserting every `new WorldMember`
   outside the one factory does not exist.
4. **Names are never usernames.** The player display rule is one function and its public-safe
   test covers both branches.
5. **Indistinguishable absence** for `CharacterView` is unchanged.
