# Tasks

Ordered so the mechanism lands and is provable before any UI, and Phase 1 (canon reveal)
ships independently of Phase 2 (source reveal). Tests accompany each service/endpoint per
repo rules; authorization/visibility and proposal application are the priority coverage.

**Status:** Phase 1 (A–D) built; full suite green (Application 1069, Api 329, Domain 563,
Web 33, Infrastructure 160, Worker 27, Shared 1). Reveal-specific: 23 Application (8 closure +
15 service) + 5 API. Phase 2 (E) not started. Phase 1 = requirements 1–5, 7; Phase 2 = req 6.

## Phase A — Reveal source type (Phase 1 foundation)

- [x] A1. Added `SourceType.Reveal` (additive; enums are stored as strings).
- [x] A2. Mapped it in `SourceTypeDisplay` ("Reveal") — a system-generated label, not a capture option.
- [x] A3. `EnumDefinitionTests` updated (expected value + count 12→13).

## Phase B — RevealService (Requirements 1–5, 7)

- [x] B1. Models: `RevealCommand`, `FactCorrection`, `RevealResult` (with `IsClosed` / `TotalRevealed`).
- [x] B2. `RevealClosure.MissingArtifactDependencies` — pure function; single pass (only artifacts
  can be missing deps). Isolated + unit-tested (`RevealClosureTests`, 8).
- [x] B3. `RevealService.RevealAsync`: GM gate; load + validate (world scope, Private→400, drop
  already-PartyVisible no-ops); closure check; mint reveal `Source` (`SourceType.Reveal`,
  PartyVisible) + `ReviewBatch { Kind = "Reveal" }`; per element an `Update*` proposal with
  `{ "visibility": "PartyVisible" }` (corrections → `UpdateFact` truthstate); apply via the real
  `IProposalApplicator`; accept-stamp with acting GM. One `IUnitOfWork` transaction. DI registered.
- [x] B4. `RevealServiceTests` (15): gate, per-kind promotion + provenance, no-op, not-closed
  rejects whole + returns missing set, dependency-in-same-set closes, selective (artifact reveal
  keeps its GM-only fact hidden), correction re-truth-states in same batch, Private 400, cross-world
  404, unknown 404, rollback-on-apply-failure.
- [x] B5. Correctness Properties 1–6 covered by the scenario + closure tests above (matching the
  house convention — services are tested with concrete scenarios, not FsCheck). FsCheck variants
  remain an optional follow-up.

## Phase C — API (Requirements 1–5, 7)

- [x] C1. `RevealRequest` / `RevealResponse` / `RevealNotClosedResponse` contracts. Missing deps
  are artifacts only, so the 422 body carries `missingArtifactIds` (no `missingFactIds` — no such case).
- [x] C2. `POST /api/worlds/{worldId}/reveal`; `WorldMemberActionFilter` + GM gate; maps result,
  422 for not-closed, `invalid_truth_state` 400.
- [x] C3. `RevealEndpointTests` (5): GM reveals a GM-only artifact → player then sees it (round-trip);
  GM reveals a GM-only fact → player sees its value on Voss; player POST → 403; not-closed → 422 with
  the missing parent artifact id.

## Phase D — UI (Requirements 1, 2, 4)

- [x] D1. `NornisApiClient.RevealAsync` (surfaces 422 as a `RevealOutcome` with missing deps, not an
  error); GM-only "Reveal to the party" card on `ArtifactDetail`, shown when the artifact or any of
  its facts/relationships is GM-only.
- [x] D2. `RevealDialog` — per-element checkboxes (the selective choice, Req 4), a note, and on a
  not-closed result an "Include and reveal" affordance that adds the missing artifacts and retries;
  success surfaces the reveal counts. Web builds clean.
- [ ] D3. Live-app verification behind Auth0 — not run (the reveal UI needs Discord login + a seeded
  GM-only world the preview can't reach, exactly as the wrap-up feature's E4). Backend behavior is
  proven end-to-end by the C3 round-trip tests over real controller/auth/EF wiring.

## Phase E — Source reveal (Requirement 6, Phase 2)

- [ ] E1. `RaiseVisibilityAsync` on the source repository/service — the sole sanctioned path
  that lifts a processed `GMOnly` source to `PartyVisible`; the `SourceService` 409 stays for
  ordinary edits.
- [ ] E2. `RevealService.RevealSourceAsync` (GM gate + provenance/audit note); confirm map
  attachment gating against `MapViewService` so the image surfaces to players once the source
  is revealed.
- [ ] E3. `POST /api/worlds/{worldId}/sources/{sourceId}/reveal`; GM-gated.
- [ ] E4. Tests: 409 still blocks ordinary edits; reveal path raises a processed GMOnly source;
  player can load the previously GM-only map source; derived GMOnly locations stay hidden until
  separately revealed (composition with Phase 1).
- [ ] E5. UI: "Reveal to party" on a GM-only source / map view; composes with the Phase 1
  location reveal for the full map worked example.

## Deferred (explicitly not this feature)

- [ ] Automatic cross-visibility duplicate detection / reconciliation (manual `ArtifactMergeService`
  is the current fix).
- [ ] Convergence gauge — ripeness scoring / "what to reveal now" suggestions that call this primitive.
- [ ] GM-only structural extraction pass (richer GM-truth authoring).
- [ ] Per-player / per-character reveal.
- [ ] Un-reveal / visibility lowering.
- [ ] `LibraryDocument` reveal.
- [ ] Player-facing "what you learned" digest / notifications.

## Decisions (resolved in design)

1. Non-closed sets — **reject with the missing dependency set**, no silent expansion.
2. Reveal provenance — **new `SourceType.Reveal`**, `PartyVisible` synthetic source, `Kind = "Reveal"` batch.
3. Source reveal — **direct gated `RaiseVisibilityAsync`**, not a source-visibility proposal.
4. Corrections — **in Phase 1** via `UpdateFact` truthstate; contradiction *detection* stays out of scope.
5. Phasing — **canon reveal (Phase 1) ships before source reveal (Phase 2)**; same doc.
