# Tasks

Ordered so the scoring function is provable before anything queries it, and the read model is
provable before any pixels. Phase 1 ships independently of Phase 2.

**Status: phases A and B built 2026-08-06** — the score and the read model, with the service registered in DI ahead of its endpoint. Phases C–E remain.

Two things the build changed from the plan, recorded here rather than silently:

- **A Private fact marked `TruthState.Hidden` was reaching the candidate set.** Writing the truth-state arm as an alternative to the visibility check let it past Requirement 1.3. The example tests covered Private and Hidden singly and never together; Correctness Property 2 found the pairing on its seventh generated world.
- **The fact query is bounded by `MaxFactsPerArtifact`, and its truncation runs against this feature.** The repository returns the *newest* facts per artifact while dormancy ranks the *oldest*, so on an artifact holding more facts than the cap the gauge drops exactly the candidates it most wants. Set to 200, which no real artifact should reach. The fix, if one ever does, is a hidden-facts-by-world query rather than a larger number.
- **`ConvergenceScore` reads no clock.** Days-hidden is passed in, matching the repo's prevailing `DateTimeOffset.UtcNow` idiom rather than introducing a second time abstraction for one service.

## Phase A — The score (pure, no I/O)

- [x] A1. `ConvergenceWeights` — the five weights and the familiarity floor as constants in one
  place, per Req 2.3.
- [x] A2. `ConvergenceScore` — a pure static taking the five component values and returning the
  total, applying familiarity as a multiplier over the weighted sum.
- [x] A3. Unit tests over A2: each component at 0 and 1, saturation boundaries, the familiarity
  floor, and the ordering tiebreak (score → `CreatedAt` → `Id`).
- [x] A4. Property test for Correctness Property 5 — familiarity gates rather than adds.

## Phase B — Candidate discovery and the read model (Req 1–5)

- [x] B1. Models in `Nornis.Application`: `ConvergenceGauge`, `ConvergenceCandidate`,
  `ConvergenceComponents`, `ConvergenceCandidateKind`.
- [x] B2. `IConvergenceGaugeService` / `ConvergenceGaugeService.GetGaugeAsync` — GM gate,
  candidate query at `VisibilityFilter.All`, excluding `Private` and archived.
- [x] B3. Dormancy, anchor familiarity, and storyline-state components from the loaded entities.
- [x] B4. Self-containment via `RevealClosure.MissingArtifactDependencies`, called per candidate
  — reused, not reimplemented (Req 3.3).
- [x] B5. Contradiction component from the latest `HealthAssessment`'s Open `Contradiction`
  findings, matched on `ArtifactId` and resolved `EvidenceJson` ids; `null` when no assessment
  exists (Req 4.3).
- [x] B6. `ConvergenceGaugeServiceTests`: candidate selection (Private excluded, archived
  excluded, already-visible excluded), each component's contribution, and the no-assessment
  path.
- [x] B7. Property tests for Correctness Properties 2 and 3.
- [x] B8. A test asserting Property 4 — the gauge's reported closure equals `RevealClosure`'s
  for the same candidate.

## Phase C — API (Req 1, 6)

- [ ] C1. `ConvergenceResponse` and its nested DTOs in `Nornis.Api/Contracts/Responses`.
- [ ] C2. `GET /api/worlds/{worldId:guid}/convergence`.
- [ ] C3. Controller/authorization tests — non-GM 403, non-member 404, empty gauge 200. Tagged
  `TestCategory=Authorization`.

## Phase D — The page (Req 2, 6)

- [ ] D1. A GM-only page listing candidates highest first, each row rendering the component
  phrases and the self-contained marker from the read model without recomputation.
- [ ] D2. Row selection opens the existing reveal dialog with the candidate and its closure
  pre-selected, submitting through `IRevealService.RevealAsync`.
- [ ] D3. Nav entry, GM-only, alongside the other GM surfaces.
- [ ] D4. bUnit tests for the row rendering and the pre-filled hand-off.
- [ ] D5. Live-app verification behind Auth0 — carries the same rider as features 16 and 17;
  not runnable until the sign-in question is settled.

## Phase E — Narrated ordering (Req 7)

- [ ] E1. `IConvergenceNarrationClient` at the prompt seam: Application owns the prompt text,
  the adapter owns transport, timeout, and parse.
- [ ] E2. Budget gate and usage recording, matching the other AI paths.
- [ ] E3. Annotation applied over the top N without reordering; failure and budget exhaustion
  fall back to the unannotated ranking (Req 7.4).
- [ ] E4. Tests: the captured prompt carries only GM-scoped material (leak-surface pattern), the
  ranking is unchanged by annotation, and an exhausted budget still returns candidates.

## Out of scope

- [ ] Automatic reveal on a threshold.
- [ ] Per-player or per-character readiness.
- [ ] Learning from accepted or ignored suggestions.
- [ ] `LibraryDocument` readiness.
