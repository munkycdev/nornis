# Tasks

Ordered so the marker's contract and the no-leak property are provable before any pixels.
Phase 1 ships independently of Phases 2 and 3.

**Status: phases A–F built 2026-08-06.** Only D5 (live verification behind Auth0) is
outstanding.

What the build changed from the spec:

- **The GM's note needed somewhere structural to live.** It existed only inside the composed
  source body, and recovering it by splitting that string would have made the composition
  format and a parse format two copies of one rule. `Source.RevealNote` now holds it;
  `RevealService` fills it; historical reveals are null, which is honest.
- **Party-visible-but-`Hidden` facts are not "learned".** A reveal can raise visibility while
  leaving the truth state Hidden, and the party then sees the shape of a claim without its
  truth. Counting that as learned would be the view's first lie.
- **Mark-seen uses the newest rendered entry's date, not `now`.** Marking to `now` would also
  close anything revealed between the page loading and the button being pressed.
- **Navigate-away marking (D2's second half) was deliberately not built.** A fire-and-forget
  call from `Dispose` can outlive the scope it needs, and the explicit button satisfies what
  D2 is actually for — not losing the list to an interruption. Recorded rather than done
  quietly.

Phases E and F, and what they changed:

- **The badge rides the existing activity poll rather than adding a second timer.**
  `SourceActivity`'s own comment calls it the most frequently requested thing in the system
  and forbids loading a row it does not count, so the count is a real aggregate
  (`CountRevealsSinceAsync`) reusing `SourceVisibilityRule` — not the read model. The
  controller composes the two services, so `SourceService` never learns about a member's
  marker.
- **Accepted consequence:** a reveal whose every element has since been archived is counted
  by the badge and dropped from the page, so the badge can overcount in a case that requires
  a whole disclosure to be retired. Recorded in the repository method's own comment.
- **The badge counts disclosures only, not the wider delta.** "Your GM told you something"
  is worth pulling someone back to the app for; "a session note finished processing" is not.
- **Phase F reused the resolver rather than joining through SourceReference.** An extraction
  batch's accepted proposals already name what it put into the record, exactly as a reveal's
  do — the only difference is the batch Kind, which is null for extraction. One code path
  now serves both, and the entry carries a Kind so the page can say which.
- **Property 3 needed strengthening before it covered F.** As first written its hidden noise
  was GM-only *artifacts*, which the element filter drops regardless — so removing the
  source-level visibility gate left it green. It now includes a GM-only session over
  party-visible material, and goes red on exactly that removal.
## Phase A — The marker (Req 3)

- [x] A1. `WorldMember.LearnedSeenAt` (nullable `DateTimeOffset`) + additive migration.
  **Apply to prod before the deploy that carries it.**
- [x] A2. Repository support for reading and advancing one member's marker.
- [x] A3. Advance-only semantics: `Math.Max` against the current value, future timestamps
  clamped to now.
- [x] A4. Tests for Properties 4 and 6 — the marker only moves forward, and one member's
  marking never touches another's.

## Phase B — The read model (Req 1, 2)

- [x] B1. Models: `LearnedDigest`, `LearnedEntry`, and the resolved element shapes.
- [x] B2. `ILearnedDigestService.GetAsync` — party-visible `SourceType.Reveal` sources newer
  than the marker, read at `VisibilityFilter.ForRole`, newest first, capped.
- [x] B3. Resolve each reveal's accepted proposals back to artifacts, facts, and relationships;
  drop what is no longer visible or has been archived.
- [x] B4. Drop an entry whose elements all resolved away (Req 2.3), and render the GM's note
  rather than the composed source body (Req 2.4).
- [x] B5. Null marker takes the bounded first view; `HasMore` reports truncation.
- [x] B6. **Property 3 first**: two worlds identical in party-visible material but differing
  arbitrarily in hidden material produce byte-identical views. This is the one that catches a
  well-meant "and 3 more" years from now.
- [x] B7. Property tests for Properties 1, 2, and 5.
- [x] B8. Service tests seeding through `RevealService` where practical, so the read model is
  tested against what the writer actually produces.

## Phase C — API (Req 1, 3)

- [x] C1. `LearnedResponse` and nested DTOs.
- [x] C2. `GET /api/worlds/{worldId:guid}/learned` and
  `POST /api/worlds/{worldId:guid}/learned/seen`, both behind `WorldMemberActionFilter`.
- [x] C3. Authorization tests — non-member 403 regardless of world existence, Player and
  Observer both allowed, one member's mark-seen invisible to another. Tagged
  `TestCategory=Authorization`.

## Phase D — The page (Req 1, 2)

- [x] D1. A page listing entries newest first: date, the GM's note set apart, promoted
  elements linking into the codex.
- [x] D2. Mark seen on dismiss, not on load. Navigate-away marking not built — see above.
- [x] D3. Nav entry, visible to every member.
- [x] D4. Empty state that reads as "nothing new", never as "nothing left".
- [ ] D5. Live-app verification behind Auth0 — same rider as features 16, 17, and 21. **Not run.**

## Phase E — The unseen count (Req 4)

- [x] E1. `CountUnseenAsync` + `GET /api/worlds/{worldId:guid}/learned/count`.
- [x] E2. Nav badge, capped for display the way the review badge caps its own, absent at zero.
- [x] E3. Tests: the count obeys the party floor, and zero renders nothing.

## Phase F — The wider delta (Req 5)

- [x] F1. Party-visible knowledge arriving through ordinary extraction since the marker.
- [x] F2. Kept distinguishable from deliberate reveals in the read model and on the page.
- [x] F3. Property 3 re-asserted over the combined view.

## Out of scope

- [ ] Notifications leaving the app.
- [ ] Per-character views.
- [ ] A GM-facing "what have I told them" audit.
- [ ] Per-row read receipts.
