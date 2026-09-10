# Design Document

## Overview

One new entity, one migration, one moved foreign key, one predicate that replaces one field
comparison, and a Party page that reads a different list. The rest is following the key.

The shape:

```
WorldMember 1 ── 0..1 Player 1 ── * Character
                  (WorldMemberId?)   (PlayerId)
```

## Where each rule lives (pre-implementation check 1)

| Rule | The one place |
| --- | --- |
| A player's display name | `PlayerDisplayName.For(player, member)` in Application: linked → `MemberDisplayName.For(member)`, unlinked → `player.Name`. `MemberDisplayName` keeps its public-safe fallback. |
| Who may act on a character | `CharacterService.IsSteward(character, player, actingMember, role)`: the linked member, or any GM when unlinked. Replaces `IsOwner` and the `character.WorldMemberId == actingMember.Id` comparisons in `SetSheetSharingAsync` and `CheckOwnershipAsync`. |
| A member always has a player | `WorldMemberFactory.Create(...)` in Application returns the member and its player together; every membership creation (`WorldService.CreateAsync` for the owner, `WorldMemberService.AddMemberAsync`, `WorldInviteService.RedeemAsync`) calls it. `MemberPlayerScanTests` asserts no other `new WorldMember` exists in `src/`. |
| Claiming = linking | `PlayerService.MergeIntoMemberAsync(playerId, memberId, worldId)`; `ClaimAsync` (self) and `LinkAsync` (GM, any member) are the two callers. |
| What null means | `Player.WorldMemberId` null = not on Nornis. Nothing else. There is no "pending" or "invited" state; an invite that is redeemed creates a member and its player, and the GM links the old one. |

## Domain

```csharp
Player
- Id: Guid
- WorldId: Guid
- WorldMemberId: Guid?      // SET NULL when the member is removed
- Name: string              // ≤ 200; the GM's name for an unlinked player; kept but unread once linked
- CreatedAt, UpdatedAt

Character
- PlayerId: Guid            // replaces WorldMemberId; cascade from Player
```

`WorldMember.Characters` goes; `WorldMember.Player` (nullable navigation) arrives.
`Player.Characters` is the collection.

Constraints in `PlayerConfiguration`: `(WorldId, WorldMemberId)` unique filtered on
`WorldMemberId IS NOT NULL`; `Name` max 200; delete behaviour Player→Characters cascade
(deleting a player is refused by the service while characters exist, so the cascade only ever
runs for world deletion), WorldMember→Player `SetNull`.

The cycle note in `CharacterConfiguration` (Worlds→Artifacts→Characters vs
Worlds→Members→Characters) moves to Players: Worlds→Players→Characters. SQL Server still needs
one of the paths `NoAction`; keep the existing choice and re-derive the comment.

## Migration: `AddPlayers`

One migration, three steps in order, all in `Up`:

1. Create `Players`; add `Characters.PlayerId` nullable with no FK yet.
2. SQL backfill:
   ```sql
   INSERT INTO Players (Id, WorldId, WorldMemberId, Name, CreatedAt, UpdatedAt)
   SELECT NEWID(), m.WorldId, m.Id, COALESCE(NULLIF(m.DisplayName, ''), CONCAT('User ', LEFT(CONVERT(varchar(36), m.UserId), 8))), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
   FROM WorldMembers m;
   UPDATE c SET PlayerId = p.Id FROM Characters c JOIN Players p ON p.WorldMemberId = c.WorldMemberId;
   ```
   The name expression mirrors `MemberDisplayName.For` — legitimately, because no compiler
   spans a migration and a service; the comment says so.
3. Make `PlayerId` non-null, add the FK and index, drop `Characters.WorldMemberId` and its FK.

The migration is **not additive**: a running old revision reads `Characters.WorldMemberId` and
errors between the schema change and the rollout. The same window `RenameCampaignToWorld`
accepted, for the same single-user reason; recorded in tasks.md. Applied by David before the
deploy, as usual (see `nornis-migrations-ops`). The backfill SQL is not covered by CI (EF
InMemory skips migrations): post-apply, `SELECT COUNT(*) FROM Characters WHERE PlayerId IS
NULL` and `... FROM WorldMembers m LEFT JOIN Players p ON p.WorldMemberId = m.Id WHERE p.Id IS
NULL` must both be zero, checked before the deploy is watched.

## Application

`IPlayerService`:

```csharp
Task<AppResult<IReadOnlyList<PlayerView>>> ListByWorldAsync(worldId, ct)
Task<AppResult<Player>> CreateAsync(worldId, name, actingUserId, role, ct)          // GM
Task<AppResult<Player>> RenameAsync(playerId, worldId, name, actingUserId, role, ct) // GM, unlinked only
Task<AppResult> DeleteAsync(playerId, worldId, actingUserId, role, ct)              // GM, unlinked, no characters → else 409 player_has_characters
Task<AppResult<Player>> ClaimAsync(playerId, worldId, actingUserId, role, ct)       // not Observer; self
Task<AppResult<Player>> LinkAsync(playerId, worldId, memberId, actingUserId, role, ct) // GM
```

`PlayerView(Guid Id, string Name, Guid? WorldMemberId, WorldRole? Role)` — `Name` already
resolved through `PlayerDisplayName`. Players carry no visibility; every member sees every
player, as every member sees every member today.

`MergeIntoMemberAsync` (private, the one operation): load the unlinked player, refuse if linked
(`409 player_linked`), load the member's own player, re-point every character, delete the
unlinked player, in one `SaveChanges` through a repository method `MoveCharactersAsync(from, to)`
so conservation is a transaction and not a loop.

`CharacterService` changes:

- `CreateCharacterCommand.ForWorldMemberId` → `ForPlayerId`. Default: the acting member's own
  player. A GM may name any player of the world.
- `ToView`: `PlayerId` in, `WorldMemberId` out.
- `GetDossierAsync`: `OwnerDisplayName` → `PlayerName` via `PlayerDisplayName`; `CanEditSheet` =
  steward or GM; `CanShareSheet` = steward.
- `ClaimAsync` (character) re-points `PlayerId` to the acting member's player.
- `IsSteward` is the only predicate. `IsOwner` is deleted, not kept as an alias.

`ArtifactService.ResolvePlayedByAsync` joins characters → players → members and formats through
`PlayerDisplayName`. `CampaignDetail.Characters` stays a `Character` list; the controller's
`ProjectForReaderAsync` already produces `CharacterView`s, and the response gains `PlayerName`
by a players lookup in the same controller call — one query per page, not one per character.

`JumpService` is unchanged: character results were already `CharacterView`s by name.

## API

```
GET    api/worlds/{worldId}/players                    every member
POST   api/worlds/{worldId}/players        {name}      GM
PUT    api/worlds/{worldId}/players/{id}   {name}      GM, unlinked
DELETE api/worlds/{worldId}/players/{id}               GM, unlinked, empty
POST   api/worlds/{worldId}/players/{id}/claim         member (not Observer)
PUT    api/worlds/{worldId}/players/{id}/member {worldMemberId}   GM
```

`CreateCharacterRequest.WorldMemberId` → `PlayerId`. `CharacterResponse.WorldMemberId` →
`PlayerId`; `CharacterDossierResponse.OwnerDisplayName` → `PlayerName`. `PublicController`
serves none of this.

## Web

- **Party** reads `GET players` and `GET characters` and groups by `PlayerId`. Each row: avatar
  initial, name, then either the role label or a quiet "Not on Nornis" chip. GM rows get an
  inline rename (unlinked) and a remove (unlinked, empty). An "Add player" field sits at the
  foot, GM only. No new page and no new route; `ReachabilityTests` is unaffected.
- **Your settings** — "Claim an existing character" stays; a new "Claim a player" picker beside
  it lists unlinked players with their character counts. `CreateCharacterRequest` sends
  `PlayerId`; the GM's "for another member" picker on the world settings panel becomes a
  players picker.
- **Character page** — "Played by {PlayerName}"; the rail row likewise.
- **Campaign page cast** — chips unchanged, with the player name in the tooltip where the member
  name was.
- `MemberDisplay` (Web) gains `Name(PlayerDto)` reading the resolved name — it does not resolve
  anything itself; resolution is the Application's.

## Testing strategy

- `CharacterServiceTests`: steward matrix — linked member, other member, GM, unlinked player ×
  edit / delete / sheet edit / sheet share / snapshot attach. **Sabotage:** widen `IsSteward` to
  any member; the sheet-share test and the delete test must both go red.
- `PlayerServiceTests`: create/rename/delete rules; delete with characters → 409; claim moves
  every character and deletes the player (conservation asserted on the world's character set,
  not on a count); claim of a linked player → 409; GM link ≡ claim (same repository calls).
- `PlayerDisplayNameTests`: linked with display name, linked without (fallback is `User xxxxxxxx`,
  never the username), unlinked.
- `MemberPlayerScanTests`: every `new WorldMember` in `src/` is inside `WorldMemberFactory`.
  **Sabotage:** add one in `WorldInviteService`; the test names the file.
- `CharacterViewIndistinguishabilityTests`: unchanged in intent, field renamed.
- `CampaignsControllerTests` / `CharactersControllerTests`: response shapes carry `PlayerId` /
  `PlayerName`; the whole-response tests compare the new field.
- Live: the two-identity recipe (`nornis-live-two-user-check`) with an unlinked "Henry" —
  create as GM, create Malliano for him, sign in as the second identity, claim, confirm Malliano
  is now editable by the claimant and gone from the GM's steward list.

## Design decisions

1. **Every member gets a player automatically.** The alternative — a player only for members
   with characters, and Party showing "members ∪ players" — puts the join in the page and
   reintroduces the two-source list this feature exists to remove. A GM with no character
   appears on Party as today; that is a person at the table.
2. **Claim is merge, not re-link.** Because 1 means the claimant already has a player, linking
   the old one would give them two. Moving characters and deleting the empty row keeps the
   invariant without a special case.
3. **The GM stands in for an absent player.** Unlinked characters' sheets are GM-editable and
   GM-shareable; nothing else makes sense for Malliano, and it is exactly the right the GM
   already has for characters created "for another member" who never logs in.
4. **Leaving keeps the player.** The membership cascade deleting characters was a data-loss
   rule that only ever looked reasonable because a character could not exist without a member.
   Now it can, and should.
5. **No adoption of the unlinked name on claim.** Tempting, and it would be the claim changing
   the member's display name across the world without saying so.
