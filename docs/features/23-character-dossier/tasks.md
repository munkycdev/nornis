# Tasks

Ordered so the visibility properties are provable before any pixels, and so each phase ships
alone. Phase A needs no migration; B and C add one each; D is optional and may never be built.

**Status: Phases A, B and C built 2026-08-09.** A1–A8, B1–B8 and C1–C8 done; A9 half-done
and half-declined (below). Phase D remains unbuilt and optional.

What the build changed from the spec:

- **`GetDetailAsync` does not bound anything.** The design's "what is unbounded" table claimed
  facts and relationships were "bounded by that service's own limits". They are not — it
  returns every visible row. The projector now caps facts at `MaxFacts = 60` and reports
  `TotalFactCount` alongside, the same shape the per-group cap already used. Recorded rather
  than quietly fixed, because the table asserted something untrue about another component.
- **`ArtifactDetail.PlayedBy` was not widened to carry character ids**, so A9's second half —
  linking from an artifact back to the characters playing it — is not built. `PublicController`
  serves the same `GetDetailAsync` to anonymous readers, and putting character ids on that path
  to save a convenience click is a bad trade. If the link is wanted, it needs a separate
  authenticated resolution, not a wider shared model.
- **`MemberDisplayName` extracted.** The display-name rule was inline in `ArtifactService` and
  differently inline in `CostService`; the dossier needed a third. The public-safe form (id
  fallback, never the username) is now one place. `CostService` deliberately keeps its own —
  it is GM-only and never public — and the helper says so, so the next reader does not
  "finish the job" by unifying them and leaking usernames onto the public world page.
- **`ArtifactsController.ToFactResponse` and `ToConnectedResponse` widened to `internal`**
  rather than growing a second copy of the same mapping in `CharactersController`.
- **The sabotage run happened and caught more than it was aimed at.** Widening the reader's
  role and returning a distinguishable empty record failed three tests, not two:
  `GroupsOnlyVisibleConnections` also went red, naming the GM-only Cursed Dagger that had
  leaked into a player's item list.
- **One test was wrong before the code was.** `GetDossierAsync_NamesOwnerAndCampaigns` seeded
  `CampaignCharacters` by mutating the entity; the in-memory repository rebuilds that
  collection from its own assignments, exactly as EF's `Include` does. Fixed by going through
  `ReplaceCampaignAssignmentsAsync` — the API the production code actually uses.

Phase B, and what it changed:

- **`MaxSheetChars` lives on the `Character` entity, not on `CharacterService`.** The design
  put it on the service, but Infrastructure needs it too for the column's `HasMaxLength` and
  cannot reference Application. On the entity, in Domain, both sides read the same constant and
  the compiler enforces the sameness — so the "mirrors X" comment the EF configuration would
  otherwise have needed does not exist.
- **`CanShareSheet` added to the read model.** The design listed only `CanEditSheet`, which
  cannot express the rule it also states: a GM may edit any character's sheet but does not
  decide who else reads it. Two capabilities, two flags.
- **`SheetSharedWithParty` is reported only to readers who may edit.** Otherwise a member who
  cannot read the sheet still learns whether one exists to be shared. Same instinct as Phase A's
  indistinguishability property, applied to a smaller thing.
- **`SheetUpdatedAt` surfaced rather than merely stored.** "Is this sheet still current?" is
  the question a paper table actually has, and a column written but never read is one nobody
  will trust later.
- **Both guards were seen failing, and the second attempt mattered.** The read-gate sabotage
  worked first time. The isolation guard's first sabotage silently did not apply — the string
  being replaced did not exist — so the run that "passed" proved nothing. Re-planted properly,
  it failed and named `LoremasterService.cs`. A guard whose sabotage does not land is
  indistinguishable from a guard that works.
- **`CharacterSheetIsolationTests` carries its own self-check.** It asserts every directory and
  file it claims to scan actually resolves, because a renamed folder would otherwise turn it
  green forever — which is precisely the "guard that could not fire" defect.

Phase C, and what it changed:

- **Snapshot visibility reuses `ISourceRepository.ListAttributionByIdsAsync`.** It is the same
  projected, SQL-applied `SourceVisibilityRule` that decides which provenance rows an artifact
  page may show — so a snapshot is readable exactly when its source is, without this feature
  writing a rule at all. Ids that no longer resolve are absent, which fails closed.
- **`MaxSnapshots` lives on the entity**, for the same reason `MaxSheetChars` does.
- **Attach returns 204, not the created snapshot.** A snapshot only means anything with its
  source's title beside it, and that title is resolved through the *reader's* visibility on the
  dossier. Returning a titleless half of one would have been a second, worse shape of the same
  thing — so the caller reloads.
- **Detach is idempotent and quiet about other characters' rows.** A snapshot id belonging to
  another character is "not there" for this caller; both cases return success and neither
  deletes anything, matching the repository contract's rule that deleting what is absent does
  nothing.
- **Both Phase C guards were watched failing.** Removing the explicit snapshot cleanup made
  `CharacterDeleteSnapshotCleanupTests` fail with `SQLite Error 19: FOREIGN KEY constraint
  failed` — exactly the symptom its own comment predicts, and the reason the test is at the
  database level rather than the service level. Removing the visibility filter failed
  `GetDossierAsync_SnapshotsFollowTheirSourcesVisibility`.
- **Requirement 5 holds by construction:** no extraction path, prompt, or schema was added.
  A snapshot is an ordinary source and reaches extraction, if at all, the way every source
  does.

## Phase A — The record

- [x] A1. `CharacterRecordProjector`: group an `ArtifactDetail`'s connected artifacts by
      `ArtifactType`, cap each group at `MaxPerGroup = 24`, report `TotalCount` above the cap.
      Pure function over an already-filtered detail — no repository access.
- [x] A2. `CharacterDossier`, `CharacterRecord`, `CharacterRecordGroup` read models.
- [x] A3. `CharacterService.GetDossierAsync` composing `ArtifactService.GetDetailAsync`.
      Swallow its 404/403 into `Record: null`.
- [x] A4. **Prove Property 3 before A5.** Test: dossier for a character linked to a `GMOnly`
      artifact, read as Player, equals the dossier for an unlinked character. Break it first by
      returning a distinguishable empty record, watch it fail, then fix.
- [x] A5. **Prove Property 2.** Test: a `GMOnly` fact on the linked artifact is absent for the
      owning Player. Invert the assertion, confirm it fails naming the fact, restore.
- [x] A6. `GET /worlds/{worldId}/characters/{characterId}/dossier` on the existing controller;
      `CharacterDossierResponse` contract; `NornisApiClient` method + DTO.
- [x] A7. Role matrix tests: Owner, GM, other Player, Observer all read; none mutate.
- [x] A8. `CharacterDetail.razor` at `/characters/{characterId:guid}` — header, campaigns,
      grouped record with per-group "showing N of M" where capped.
- [x] A9a. Link in from `Profile.razor` character rows.
- [ ] A9b. Link back from `ArtifactDetail`'s `PlayedBy` names — declined, see above.

## Phase B — The written sheet

- [x] B1. Migration: `Character.Sheet`, `SheetSharedWithParty` (default false), `SheetUpdatedAt`.
- [x] B2. XML comment on `Sheet` stating it is uninterpreted and must never reach an AI path,
      so a future author has to delete a sentence to break Property 4.
- [x] B3. `MaxSheetChars = 20_000` on `CharacterService`; refuse over-length, do not truncate;
      normalize empty-after-trim to null.
- [x] B4. `UpdateSheetAsync` and `SetSheetSharingAsync`, both through `CheckOwnershipAsync`;
      sharing is owner-only, reading is owner + GM + (party when shared).
- [x] B5. **Prove the read gate.** Test: another Player reading an unshared sheet gets null, not
      text. Write the permissive version first and watch it fail.
- [x] B6. Sheet endpoints + contracts + client methods.
- [x] B7. Sheet panel in `CharacterDetail.razor` using `NotesEditor` and `MarkdownRenderer`;
      "start a sheet" empty state; share toggle visible only to the owner.
- [x] B8. Property 4 guard: a test asserting no AI-path type references `Character.Sheet`.
      Mechanical, and the only kind of guard that survives a refactor by someone who has not
      read this document.

## Phase C — Sheet snapshots

- [x] C1. `CharacterSheetSnapshot` entity + configuration: unique `(CharacterId, SourceId)`,
      `Snapshot → Source` Cascade, `Snapshot → Character` NoAction.
- [x] C2. `CharacterRepository.DeleteAsync` removes snapshot rows explicitly, following the
      `CampaignRepository.DeleteAsync` precedent. Test it, since no cascade will.
- [x] C3. Migration.
- [x] C4. `AttachSnapshotAsync` / `DetachSnapshotAsync`: same-world check, actor-readable source
      check (same 400 for both failures), `MaxSnapshots = 50` refused not truncated.
- [x] C5. Snapshot listing filtered by the *source's* visibility for the reader — Property 5.
      Test that a `Private` source attached by another member is invisible.
- [x] C6. Endpoints, contracts, client.
- [x] C7. Snapshots panel: newest-first by `AsOf`, source link, attach picker over existing
      world sources, detach with confirmation that says the source is kept.
- [x] C8. Verify Requirement 5 holds by inspection: no extraction path was added.

## Phase D — Unreconciled items (optional)

**Built 2026-09-08.** `Knowledge.UnreconciledItems.Find(record, sheetText)` is the whole rule:
one pure function fed the reader's already-filtered record and the sheet as the reader may
read it, so it has no way to see anything either gate withheld. Two decisions the spec left
open, made here rather than polled:

- **Items only.** Requirement 6.1 says "artifacts"; the user story says "the record says I
  picked something up". Nobody writes every location they have stood in on a sheet, so
  reconciling all types would have been the noise D3 says to delete. `ObservedTypes` is a
  named constant with a test pinning it, so widening it is a visible product decision.
- **No sheet means no observation.** "Everything is missing from a blank page" is true and
  useless, and a reader who may not open the sheet gets the same null — so the observation
  can never say anything about a sheet the reader cannot read. Empty in, empty out.

Bounded by the record's own per-group cap, since it reads only what the record chose to show.
The page renders it under the sheet as a sentence and a row of chips linking to the items;
nothing else is clickable and nothing writes.

- [x] D1. Plain case-insensitive name-presence test of record artifacts against the sheet text,
      over what the reader may see.
- [x] D2. Render as an observation with no action attached; never modifies the sheet.
- [ ] D3. If it proves noisy, delete it rather than tune it. Tuning is how a text search becomes
      a parser, and a parser is the rules engine this feature exists to avoid. **Open by
      nature** — this is the standing instruction for whoever next finds it noisy.

## Verification

- [x] V1. Build, `dotnet format --verify-no-changes`, and `./scripts/coverage-gate.ps1` green
      through Phase C (2026-08-09). Application 1874, Api 516, Web 185, Domain 626,
      Infrastructure 309 pass; all four coverage floors met.
- [x] V2. Live-app check: dossier as GM and as a second Player in the same world, confirming
      the sheet gate and the indistinguishability property with real data. **Run 2026-09-07**
      against the production database through the dev-auth bypass, with a synthetic Player
      (`dev|v2-player-check`) invited into Ruins of Symbaroum and Vespergale Reach and removed
      afterwards. Both migrations applied beforehand; being additive, they did not open the
      destructive-change health window and the deployed API stayed healthy.

      What held: Fera's unshared sheet rendered for the GM and was absent for the Player, with
      no sheet section at all and `sheetSharedWithParty` reported false. Ugma's shared sheet
      rendered for the Player with no share toggle. A snapshot of a party-visible source
      listed for both readers. Two GM-owned characters in Vespergale Reach, one linked to the
      GM-only Castellan Maren Voss and one unlinked, rendered identical pages to the Player.

      What the run found:

      - **`character.artifactId` was on the dossier for every reader — fixed 2026-09-08.** The
        two Vespergale dossiers differed in exactly one field: the linked one carried the
        GM-only artifact's id. The pages were indistinguishable; the JSON was not. The exposure
        predated this feature — `CharacterResponse` had always carried `ArtifactId`, and the
        character list served it to every member — so the dossier inherited it. The Property 3
        test compared `Record` and never the character envelope, which was the detection gap.

        The fix: characters now leave the Application layer as `CharacterView`, projected for
        the reader by `CharacterService.ProjectForReaderAsync`. `ArtifactId` is null unless the
        reader's own `VisibilityFilter` admits the artifact (a dangling id fails closed to
        unlinked, GM included); `SheetUpdatedAt` is null unless the reader passes the sheet's
        read gate, since a timestamp on a sheet you cannot open tells you one exists to share.
        Every character response — list, single read, dossier envelope, campaign detail and
        assignment, and every mutation's echo — goes through the one projection; the controller
        has no entity-to-response mapping left. The Property 3 test now serializes and compares
        the whole dossier with only identity fields normalized, so the next field added to it is
        covered without anyone remembering to add it. Sabotage (serve the raw id) failed four
        service tests and the new wire-level API test.
      - **C7's "detach with confirmation" was a toast, not a confirmation — fixed 2026-09-08.**
        Detach fired on the first click and the toast said the source is kept. It now asks
        first ("Detach" / "Keep"), stating the rule while the reader can still change their
        mind; the toast remains as the receipt.
      - **Copy drift — fixed 2026-09-08.** The share toggle read "Share with the world" where
        the design says party; it and its tooltip now say party. The unlinked-character
        message told a non-owner to "link one from your profile", which they cannot do; the
        instruction now appears only for the owner, and the sentence every other reader sees
        is the same for a hidden link as for no link.

      Left behind: the synthetic user row (`v2player`), since there is no user-delete path;
      and one consumed use on the Symbaroum Player invite link.
