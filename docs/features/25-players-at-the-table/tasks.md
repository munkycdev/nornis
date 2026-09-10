# Tasks

Ordered so the steward rule and the one-player-per-member invariant are proven before any
page reads them, and so each phase ships alone. Phase A carries the one migration, applied by
David before its deploy.

## Phase A — The player

- [ ] A1. `Player` entity, `PlayerConfiguration` (filtered unique index, `SetNull` from member,
      cascade to characters), `WorldMember.Player`, `Character.PlayerId` replacing
      `WorldMemberId`; `domain-model.md` amendment written now, dated.
- [ ] A2. Migration `AddPlayers` with the SQL backfill; the two post-apply zero-count queries
      recorded in the migration's comment and here.
- [ ] A3. `IPlayerRepository` (+ EF and in-memory fakes) with `MoveCharactersAsync(from, to)` as
      one save.
- [ ] A4. `WorldMemberFactory` and the three creation sites moved onto it; `MemberPlayerScanTests`,
      sabotaged in `WorldInviteService` and watched failing.
- [ ] A5. `PlayerDisplayName` and its tests; `MemberDisplayName` unchanged.
- [ ] A6. `CharacterService`: `IsSteward` replaces `IsOwner`; `ForPlayerId`; `PlayerId` in
      `CharacterView`; dossier `PlayerName`. Steward matrix tests; **the widening sabotage, two
      tests red**.
- [ ] A7. `IPlayerService` + `PlayerService`: list, create, rename, delete (409 with characters).
      Tests.
- [ ] A8. `PlayersController` (list, create, rename, delete) and the character request/response
      renames; whole-response tests updated.
- [ ] A9. Party page: players with characters, "Not on Nornis" chip, GM add / rename / remove
      inline. Your settings and the world settings panel send `PlayerId`.
- [ ] A10. Verification bar; migration applied by David; post-apply counts both zero; deploy
      watched; Party checked live as GM.

## Phase B — Claiming

- [ ] B1. `PlayerService.MergeIntoMemberAsync`; `ClaimAsync` and `LinkAsync` as its two callers.
      Conservation test over the world's character set. Claim of a linked player → 409.
- [ ] B2. `POST players/{id}/claim`, `PUT players/{id}/member`; controller tests.
- [ ] B3. Your settings: "Claim a player" beside "Claim an existing character". Party: GM "Link
      to member" on unlinked rows.
- [ ] B4. Member removal no longer deletes characters: `SetNull` verified by a test that removes
      a member and finds their player unlinked with every character still present.
- [ ] B5. Live: the two-identity recipe with an unlinked Henry, claimed by the second identity.
- [ ] B6. Change log entry.

## Phase C — The ripple

- [ ] C1. `ArtifactService.ResolvePlayedByAsync` through players; `CampaignsController` cast
      carries `PlayerName`; character page and rail read it.
- [ ] C2. Export writes `players.json`; `characters.json` carries `PlayerId`. Demo package:
      every packaged character to the demo member's player; an old package still loads (test
      with the checked-in package).
- [ ] C3. `docs/features/README.md` row; `future-features.md` if anything is left open.
- [ ] C4. Change log entry, if B's needs a second line.

## Verification

- [ ] V1. Per phase: build, `dotnet test --solution Nornis.sln`, `dotnet format
      --verify-no-changes`.
- [ ] V2. After A: the two backfill counts, pasted here with the date.
- [ ] V3. After B: the live claim walk (B5) as GM and as the second identity.

## Deferred (explicitly not this feature)

- A player page of their own.
- Contact details, notes or availability on a player.
- Merging two linked players, or moving a character between two linked players by anyone but
  a steward or the GM (the existing character claim covers the common case).
