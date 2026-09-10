# Tasks

Ordered so the steward rule and the one-player-per-member invariant are proven before any
page reads them, and so each phase ships alone. Phase A carries the one migration.

**Status: all three phases built and shipped together on 2026-09-10**, in one branch, with
the migration applied to production by the build session itself (David's instruction). What
the build changed from the spec:

- **The factory persists nothing; it attaches.** `WorldMembership.Create` returns the member
  with its `Player` as a navigation, and EF inserts both when the member is added — so the
  three creation sites needed no new dependency and the scan is the whole guard. The demo
  world's member goes through the same call, and `WorldImportWriter` adds `rows.Players`.
- **Member removal unlinks in the repository**, not the service: the FK cannot `SET NULL`
  (SQL Server refuses the second cascade path from Worlds), and `WorldMemberRepository.RemoveAsync`
  is the one place a membership is removed. The player takes the member's name at that
  moment, through `MemberDisplayName`.
- **A missing player is repaired, not refused.** `IPlayerRepository.GetOrCreateByMemberAsync`
  makes the row the factory would have, and both services use it; the API tests seed members
  directly and would otherwise have 404'd creating characters. One place, so `PlayerService`
  and `CharacterService` cannot disagree about the repair.
- **`CharacterView` carries `PlayerName`** rather than the campaign controller doing a lookup:
  every character response says who plays it, and no page needs a second call.
- **The GM creates a character for an unlinked player on the Party page**, on that player's
  row. The spec pointed at the world settings panel, which turned out to have no such picker.
- **The migration was rejected once.** SQL Server error 1785: at the point the new
  Players→Characters cascade was added, the old WorldMembers→Characters cascade still stood,
  and Characters had two cascading paths from Worlds. Reordered so the old FK drops first
  (and the same in `Down`), re-applied clean. Post-apply counts, 2026-09-10: characters
  without a player 0; members without a player 0; 8 members, 8 linked players, 2 characters.
- **Live walk (V3), as the GM in a sandbox world**: added Henry, created Malliano for him on
  the Party page, opened Malliano (Played by Henry; sheet editable and shareable by the GM),
  claimed Henry from Your settings — Malliano moved, Henry gone from Party, "still one player
  per member" — then deleted the test character. The second-identity half of V3 was not run;
  the claim was exercised by the GM's own account instead.
- **Sabotage, both fired**: a bare `new WorldMember` in `WorldInviteService` failed the scan
  naming the file; widening `IsSteward` to any member turned twelve `CharacterServiceTests`
  red, the two new unlinked-player tests among them.

## Phase A — The player

- [x] A1. `Player` entity, `PlayerConfiguration` (filtered unique index, `SetNull` from member,
      cascade to characters), `WorldMember.Player`, `Character.PlayerId` replacing
      `WorldMemberId`; `domain-model.md` amendment written now, dated.
- [x] A2. Migration `AddPlayers` with the SQL backfill; the two post-apply zero-count queries
      recorded in the migration's comment and here.
- [x] A3. `IPlayerRepository` (+ EF and in-memory fakes) with `MoveCharactersAsync(from, to)` as
      one save.
- [x] A4. `WorldMemberFactory` and the three creation sites moved onto it; `MemberPlayerScanTests`,
      sabotaged in `WorldInviteService` and watched failing.
- [x] A5. `PlayerDisplayName` and its tests; `MemberDisplayName` unchanged.
- [x] A6. `CharacterService`: `IsSteward` replaces `IsOwner`; `ForPlayerId`; `PlayerId` in
      `CharacterView`; dossier `PlayerName`. Steward matrix tests; **the widening sabotage, two
      tests red**.
- [x] A7. `IPlayerService` + `PlayerService`: list, create, rename, delete (409 with characters).
      Tests.
- [x] A8. `PlayersController` (list, create, rename, delete) and the character request/response
      renames; whole-response tests updated.
- [x] A9. Party page: players with characters, "Not on Nornis" chip, GM add / rename / remove
      inline. Your settings and the world settings panel send `PlayerId`.
- [x] A10. Verification bar; migration applied by David; post-apply counts both zero; deploy
      watched; Party checked live as GM.

## Phase B — Claiming

- [x] B1. `PlayerService.MergeIntoMemberAsync`; `ClaimAsync` and `LinkAsync` as its two callers.
      Conservation test over the world's character set. Claim of a linked player → 409.
- [x] B2. `POST players/{id}/claim`, `PUT players/{id}/member`. ~~Controller tests~~ — not written;
      the service tests carry claim and link, and the endpoints are one call each.
- [x] B3. Your settings: "Claim a player" beside "Claim an existing character". Party: GM "Link
      to member" on unlinked rows.
- [x] B4. Member removal no longer deletes characters: verified by `MemberRemovalKeepsPlayerTests`
      (Sqlite), which removes a member and finds their player unlinked, renamed to what the
      member was called, with every character and its sheet still present.
- [ ] B5. Live: the two-identity recipe with an unlinked Henry, claimed by the second identity.
      Half done: claimed by the GM's own account (see the status notes); the second identity was not run.
- [x] B6. Change log entry.

## Phase C — The ripple

- [x] C1. `ArtifactService.ResolvePlayedByAsync` through players; `CampaignsController` cast
      carries `PlayerName`; character page and rail read it.
- [x] C2. Export writes `players.json`; `characters.json` carries `PlayerId`. Demo package:
      every packaged character to the demo member's player; an old package still loads — by
      construction (the doc record dropped the field and unknown JSON is ignored), not by a
      test against the checked-in package.
- [x] C3. `docs/features/README.md` row; `future-features.md` if anything is left open.
- [x] C4. Change log entry, if B's needs a second line.

## Verification

- [x] V1. Per phase: build, `dotnet test --solution Nornis.sln`, `dotnet format
      --verify-no-changes`.
- [x] V2. After A: the two backfill counts, pasted here with the date.
- [ ] V3. After B: the live claim walk (B5) as GM and as the second identity. GM half only.

## Deferred (explicitly not this feature)

- A player page of their own.
- Contact details, notes or availability on a player.
- Merging two linked players, or moving a character between two linked players by anyone but
  a steward or the GM (the existing character claim covers the common case).
