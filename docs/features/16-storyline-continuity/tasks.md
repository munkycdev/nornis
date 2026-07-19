# Tasks

Ordered so each task is independently reviewable and the signal (Req 1) lands before the
surface (Req 2) that consumes it. Tests accompany each service/endpoint per repo rules.

**Status:** Phases A–E built; full suite green (1046 Application + 325 API tests). E2 and E4
carried to follow-ups — see notes.

## Phase A — Shared derivation refactor (behavior-preserving)

- [x] A1. Extracted the derivation into `StorylineDevelopmentReader` (`StorylineDevelopmentData`:
  storylines, sources, facts, relationships, parent-by-child, developments-by-(storyline,session)).
- [x] A2. `GetStorylineTimelineAsync` now projects the reader (constructs it inline from its
  own repos, so `ArtifactService`'s constructor — and its 4 test call sites — are unchanged).
- [x] A3. `ArtifactServiceTimelineTests` pass unchanged.

## Phase B — Staleness signal (Requirement 1)

- [x] B1. `ContinuityOptions { StaleThresholdSessions = 3, RecentSessionWindow = 2 }`, bound
  from the `Continuity` section.
- [x] B2. Models: `StorylineContinuityReport`, `QuietStoryline`, `UnanchoredStoryline`, `ContinuitySessionRef`.
- [x] B3. `StorylineContinuityService` (math isolated in internal static `BuildReport`). DI registered.
- [x] B4. `StorylineContinuityServiceTests` — boundaries, unanchored, open-question count,
  ordering, GM-only visibility parity, empty world.
- [x] B5. `GET .../storylines/continuity` + contract; GM-gated in controller; controller tests.

## Phase C — Wrap-up read (Requirement 2 read side)

- [x] C1. `WrapUpView` + response contract.
- [x] C2. `StorylineWrapUpService.GetWrapUpAsync` assembles Advanced / GoneQuiet / CouldNest /
  UnparentedArcs from the shared reader + continuity + pending PartOf proposals.
- [x] C3. `GET .../storylines/wrap-up`; GM-gate; empty-state + assembly tests.

## Phase D — Wrap-up write (Requirement 2 write side, Requirement 3)

- [x] D1. `WrapUpDecisionsCommand` + `WrapUpDecisionsRequest`.
- [x] D2. `ApplyAsync`: synthetic `Source` + `ReviewBatch` (`Kind = "SessionWrapUp"`); closures
  applied via `IProposalApplicator` (confirm-and-apply); accept/reject via `IReviewService`;
  parents via `SetStorylineParentAsync`. Closures transactional; delegated ops run outside to
  avoid nested transactions.
- [x] D3. `POST .../storylines/wrap-up`; GM-gate; returns counts + batch id.
- [x] D4. Tests: role gate, invalid-status 400, closure batch tagged + accepted with provenance,
  unknown-storyline 404, applicator-failure propagation, parent reuse, accept/reject delegation,
  review-service error propagation. Plus a controller round-trip proving the storyline goes Resolved.

## Phase E — UI (Requirement 2)

- [x] E1. `SessionWrapUpCard` on the Ask landing (`/`), GM-only, renders only when `HasWork`.
- [ ] E2. GM-only nav badge — deferred (the landing card is the primary surface; no dedicated
  on-demand entry yet).
- [x] E3. Client-persisted snooze (localStorage keyed by world + storyline + latest-session id).
- [ ] E4. Live-app verification — not run: the card is behind Auth0 Discord login and needs a
  seeded world with quiet storylines, which the preview can't reach. Backend behavior is proven
  by the controller round-trip test instead.

## Deferred (explicitly not this feature)

- [ ] Auto-enqueue retrospective/backfill on staleness threshold (design §5).
- [ ] Per-world staleness threshold / recent-window overrides.
- [ ] `WorldMember.LastWrapUpAt` durable cross-device "seen" state (design §4).
- [ ] Non-storyline artifact dormancy on parent closure.

## Decisions (resolved)

1. Closures — **apply-on-confirm** via `IProposalApplicator`.
2. "New since last wrap-up" — **stateless**; dismiss is a client-persisted snooze.
3. Wrap-up home — **card on the Ask landing** (`/`), not a dedicated page.
4. Recent window — **last *k* sessions** (`RecentSessionWindow`, default 2).
