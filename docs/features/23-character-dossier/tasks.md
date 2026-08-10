# Tasks

Ordered so the visibility properties are provable before any pixels, and so each phase ships
alone. Phase A needs no migration; B and C add one each; D is optional and may never be built.

## Phase A — The record

- [ ] A1. `CharacterRecordProjector`: group an `ArtifactDetail`'s connected artifacts by
      `ArtifactType`, cap each group at `MaxPerGroup = 24`, report `TotalCount` above the cap.
      Pure function over an already-filtered detail — no repository access.
- [ ] A2. `CharacterDossier`, `CharacterRecord`, `CharacterRecordGroup` read models.
- [ ] A3. `CharacterService.GetDossierAsync` composing `ArtifactService.GetDetailAsync`.
      Swallow its 404/403 into `Record: null`.
- [ ] A4. **Prove Property 3 before A5.** Test: dossier for a character linked to a `GMOnly`
      artifact, read as Player, equals the dossier for an unlinked character. Break it first by
      returning a distinguishable empty record, watch it fail, then fix.
- [ ] A5. **Prove Property 2.** Test: a `GMOnly` fact on the linked artifact is absent for the
      owning Player. Invert the assertion, confirm it fails naming the fact, restore.
- [ ] A6. `GET /worlds/{worldId}/characters/{characterId}/dossier` on the existing controller;
      `CharacterDossierResponse` contract; `NornisApiClient` method + DTO.
- [ ] A7. Role matrix tests: Owner, GM, other Player, Observer all read; none mutate.
- [ ] A8. `CharacterDetail.razor` at `/characters/{characterId:guid}` — header, campaigns,
      grouped record with per-group "showing N of M" where capped.
- [ ] A9. Link in from `Profile.razor` character rows and `ArtifactDetail`'s `PlayedBy` names.

## Phase B — The written sheet

- [ ] B1. Migration: `Character.Sheet`, `SheetSharedWithParty` (default false), `SheetUpdatedAt`.
- [ ] B2. XML comment on `Sheet` stating it is uninterpreted and must never reach an AI path,
      so a future author has to delete a sentence to break Property 4.
- [ ] B3. `MaxSheetChars = 20_000` on `CharacterService`; refuse over-length, do not truncate;
      normalize empty-after-trim to null.
- [ ] B4. `UpdateSheetAsync` and `SetSheetSharingAsync`, both through `CheckOwnershipAsync`;
      sharing is owner-only, reading is owner + GM + (party when shared).
- [ ] B5. **Prove the read gate.** Test: another Player reading an unshared sheet gets null, not
      text. Write the permissive version first and watch it fail.
- [ ] B6. Sheet endpoints + contracts + client methods.
- [ ] B7. Sheet panel in `CharacterDetail.razor` using `NotesEditor` and `MarkdownRenderer`;
      "start a sheet" empty state; share toggle visible only to the owner.
- [ ] B8. Property 4 guard: a test asserting no AI-path type references `Character.Sheet`.
      Mechanical, and the only kind of guard that survives a refactor by someone who has not
      read this document.

## Phase C — Sheet snapshots

- [ ] C1. `CharacterSheetSnapshot` entity + configuration: unique `(CharacterId, SourceId)`,
      `Snapshot → Source` Cascade, `Snapshot → Character` NoAction.
- [ ] C2. `CharacterRepository.DeleteAsync` removes snapshot rows explicitly, following the
      `CampaignRepository.DeleteAsync` precedent. Test it, since no cascade will.
- [ ] C3. Migration.
- [ ] C4. `AttachSnapshotAsync` / `DetachSnapshotAsync`: same-world check, actor-readable source
      check (same 400 for both failures), `MaxSnapshots = 50` refused not truncated.
- [ ] C5. Snapshot listing filtered by the *source's* visibility for the reader — Property 5.
      Test that a `Private` source attached by another member is invisible.
- [ ] C6. Endpoints, contracts, client.
- [ ] C7. Snapshots panel: newest-first by `AsOf`, source link, attach picker over existing
      world sources, detach with confirmation that says the source is kept.
- [ ] C8. Verify Requirement 5 holds by inspection: no extraction path was added.

## Phase D — Unreconciled items (optional)

- [ ] D1. Plain case-insensitive name-presence test of record artifacts against the sheet text,
      over what the reader may see.
- [ ] D2. Render as an observation with no action attached; never modifies the sheet.
- [ ] D3. If it proves noisy, delete it rather than tune it. Tuning is how a text search becomes
      a parser, and a parser is the rules engine this feature exists to avoid.

## Verification

- [ ] V1. Build, `dotnet format --verify-no-changes`, and `./scripts/coverage-gate.ps1` green.
- [ ] V2. Live-app check behind Auth0: dossier as GM and as a second Player in the same world,
      confirming the sheet gate and the indistinguishability property with real data.
