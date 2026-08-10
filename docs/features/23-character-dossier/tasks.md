# Tasks

Ordered so the visibility properties are provable before any pixels, and so each phase ships
alone. Phase A needs no migration; B and C add one each; D is optional and may never be built.

**Status: Phase A built 2026-08-09.** A1–A8 done; A9 half-done and half-declined (below).

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

- [x] V1. Build, `dotnet format --verify-no-changes`, and `./scripts/coverage-gate.ps1` green
      for Phase A (2026-08-09). Application 1847, Api 516, Web 185 tests pass; all four
      coverage floors met. Re-run per phase.
- [ ] V2. Live-app check behind Auth0: dossier as GM and as a second Player in the same world,
      confirming the sheet gate and the indistinguishability property with real data.
