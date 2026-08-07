# Tasks

Ordered so the marker's contract and the no-leak property are provable before any pixels.
Phase 1 ships independently of Phases 2 and 3.

**Status:** Not started. Phase 1 = requirements 1–3; Phase 2 = requirement 4; Phase 3 =
requirement 5.

## Phase A — The marker (Req 3)

- [ ] A1. `WorldMember.LearnedSeenAt` (nullable `DateTimeOffset`) + additive migration.
  **Apply to prod before the deploy that carries it.**
- [ ] A2. Repository support for reading and advancing one member's marker.
- [ ] A3. Advance-only semantics: `Math.Max` against the current value, future timestamps
  clamped to now.
- [ ] A4. Tests for Properties 4 and 6 — the marker only moves forward, and one member's
  marking never touches another's.

## Phase B — The read model (Req 1, 2)

- [ ] B1. Models: `LearnedDigest`, `LearnedEntry`, and the resolved element shapes.
- [ ] B2. `ILearnedDigestService.GetAsync` — party-visible `SourceType.Reveal` sources newer
  than the marker, read at `VisibilityFilter.ForRole`, newest first, capped.
- [ ] B3. Resolve each reveal's accepted proposals back to artifacts, facts, and relationships;
  drop what is no longer visible or has been archived.
- [ ] B4. Drop an entry whose elements all resolved away (Req 2.3), and render the GM's note
  rather than the composed source body (Req 2.4).
- [ ] B5. Null marker takes the bounded first view; `HasMore` reports truncation.
- [ ] B6. **Property 3 first**: two worlds identical in party-visible material but differing
  arbitrarily in hidden material produce byte-identical views. This is the one that catches a
  well-meant "and 3 more" years from now.
- [ ] B7. Property tests for Properties 1, 2, and 5.
- [ ] B8. Service tests seeding through `RevealService` where practical, so the read model is
  tested against what the writer actually produces.

## Phase C — API (Req 1, 3)

- [ ] C1. `LearnedResponse` and nested DTOs.
- [ ] C2. `GET /api/worlds/{worldId:guid}/learned` and
  `POST /api/worlds/{worldId:guid}/learned/seen`, both behind `WorldMemberActionFilter`.
- [ ] C3. Authorization tests — non-member 403 regardless of world existence, Player and
  Observer both allowed, one member's mark-seen invisible to another. Tagged
  `TestCategory=Authorization`.

## Phase D — The page (Req 1, 2)

- [ ] D1. A page listing entries newest first: date, the GM's note set apart, promoted
  elements linking into the codex.
- [ ] D2. Mark seen on dismiss or navigate-away, not on load.
- [ ] D3. Nav entry, visible to every member.
- [ ] D4. Empty state that reads as "nothing new", never as "nothing left".
- [ ] D5. Live-app verification behind Auth0 — same rider as features 16, 17, and 21.

## Phase E — The unseen count (Req 4)

- [ ] E1. `CountUnseenAsync` + `GET /api/worlds/{worldId:guid}/learned/count`.
- [ ] E2. Nav badge, capped for display the way the review badge caps its own, absent at zero.
- [ ] E3. Tests: the count obeys the party floor, and zero renders nothing.

## Phase F — The wider delta (Req 5)

- [ ] F1. Party-visible knowledge arriving through ordinary extraction since the marker.
- [ ] F2. Kept distinguishable from deliberate reveals in the read model and on the page.
- [ ] F3. Property 3 re-asserted over the combined view.

## Out of scope

- [ ] Notifications leaving the app.
- [ ] Per-character views.
- [ ] A GM-facing "what have I told them" audit.
- [ ] Per-row read receipts.
