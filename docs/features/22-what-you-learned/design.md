# Design Document

## Overview

A read model over reveal sources the system already writes, plus one nullable column carrying
each member's place in them. No new domain entity, no new write path, and — deliberately — no
AI.

The whole of Phase 1 is: find the party-visible `SourceType.Reveal` sources newer than the
member's marker, resolve each one's accepted proposals back to the artifacts, facts, and
relationships they promoted, drop anything the reader cannot currently see, and render.

## Why there is nothing to record

[`RevealService`](../../../src/Nornis.Application/Services/RevealService.cs) already writes,
per reveal, a `PartyVisible` source of `SourceType.Reveal` whose batch is
`ReviewBatchKinds.Reveal` and whose accepted proposals name every promoted element by target
type and id — with the GM's optional note in the body. That is a complete, dated, party-visible
account of the disclosure, sitting in the players' own record since feature 17 shipped.

Building a second record of "what was revealed" would be a second definition of the same fact,
which is the first of the pre-implementation checks. The read model resolves the existing one.

## The reader's marker

`WorldMember.LearnedSeenAt` — nullable `DateTimeOffset`, additive migration.

**Server-side, against the membership, not in the browser.** Browser storage fails the actual
use — a player reads on a phone at the table and a laptop at home — and this codebase has
already been bitten once by putting reader state in `localStorage`: Ask history had no cap,
swallowed its quota exception, and silently stopped recording for heavy users.

**Null means never looked**, which is not the same as *seen nothing*: a member who joined a
world with two years of reveals behind it must not be handed all of them. Null therefore takes
the bounded first view (Requirement 3.5) — the most recent `FirstViewLimit` reveals — while a
member whose marker is set gets everything after it, also capped. This is the "what does null
mean" question the checks require answering out loud, and the answer is *not* "the beginning of
time".

Moving the marker is `Math.Max` against its current value (Requirement 3.4): two tabs, or a
stale client posting an older timestamp, must not reopen what was closed.

## Scoping

The gauge reads at `VisibilityFilter.All` because its subject is what sits below the party
floor. This is its opposite and reads at the caller's own filter — `VisibilityFilter.ForRole`
— so the party floor is enforced by the same mechanism as everywhere else rather than by this
feature remembering to.

That is also what makes Requirement 2 mostly structural: an element that is no longer
`PartyVisible` simply does not come back from the repository, so it cannot be rendered. The
part that is *not* structural, and needs care, is the reveal's own body — `BuildBody` composes
a line naming how many facts and relationships were promoted, and a revealed element that has
since been archived would leave that count disagreeing with the list. The view renders the GM's
note and the resolved elements, never the composed body (Requirement 2.4).

## Read model

```text
LearnedDigest
  WorldId, GeneratedAt, SeenThrough (the marker as it was read), HasMore
  Entries[]:
    SourceId, OccurredAt, GmNote
    Artifacts[] / Facts[] / Relationships[]  — resolved, party-visible, non-archived
```

`HasMore` says the cap truncated the list, not that anything is hidden — it is a paging fact
about disclosures the reader may see, and Requirement 2.2 is untouched by it.

An entry whose elements all resolve away is dropped rather than rendered empty (Requirement
2.3): "the GM revealed something on the 4th, and it is gone" is exactly the gap that invites
the question this feature must not provoke.

## Service and API

`ILearnedDigestService`:

- `GetAsync(worldId, actingUserId, role, ct)` → the unseen entries. Read-only; reading is not
  seeing (Requirement 1.4).
- `MarkSeenAsync(worldId, actingUserId, seenThrough, ct)` → advances the marker, never
  backwards.
- `CountUnseenAsync(worldId, actingUserId, ct)` → Phase 2's badge.

`GET /api/worlds/{worldId:guid}/learned`, `POST /api/worlds/{worldId:guid}/learned/seen`,
`GET /api/worlds/{worldId:guid}/learned/count`. All carry `WorldMemberActionFilter`, which
answers a non-member `403` regardless of whether the world exists — the correction feature 21's
design doc needed, applied here from the start.

Marking seen is an explicit `POST` rather than a side effect of the `GET`, because a reader who
opens the page, is interrupted, and comes back should not have lost the list. The page posts
when the reader dismisses or navigates away.

## UI

A page listing entries newest first: the date, the GM's note set apart as their words, and the
promoted elements as links into the codex. Phase 2 adds a nav count beside it, capped for
display the way the review badge already caps its own.

Nothing on this page is GM-gated — it is the one surface in the app whose whole audience is the
people without privileges.

## Correctness Properties

*A property is a characteristic that should hold across all valid executions — the bridge
between the spec and machine-verifiable tests.*

### Property 1: The view is read-only

*For any* read, no visibility, truth state, or marker changes. **Validates: Req 1.4.**

### Property 2: Only party-visible material appears

*For any* member and any world state, every element rendered is `PartyVisible` and not
archived. **Validates: Req 2.1, 2.3.**

### Property 3: Nothing hidden is countable

*For any* two worlds identical in their party-visible material but differing arbitrarily in
their `GMOnly`, `Private`, and `Hidden` material, the view is byte-identical.
**Validates: Req 2.2.** *This is the property worth writing first; it is the one that would
catch a well-meant "and 3 more" line years from now.*

### Property 4: The marker only moves forward

*For any* sequence of mark-seen calls in any order, the marker equals the latest timestamp
among them. **Validates: Req 3.4.**

### Property 5: A first view is bounded

*For any* world with any number of reveals, a member whose marker is null receives at most
`FirstViewLimit` entries. **Validates: Req 3.5.**

### Property 6: Markers are private to their member

*For any* member marking seen, no other member's marker changes. **Validates: Req 3.6.**

## Error Handling

- Non-member → `403`, via the filter, regardless of world existence.
- Nothing unseen → `200` with an empty list. Not an error; it is the ordinary state.
- `MarkSeen` with a timestamp older than the marker → `200`, no change. Idempotent, not a
  conflict.
- `MarkSeen` with a future timestamp → clamped to now, so a skewed client cannot mark unseen
  what has not happened.

## Testing

Property 3 first, and as a property rather than an example: it is quantified over what the
world contains, which is precisely what an example test cannot cover. Properties 4 and 6 are
cheap and pin the marker's whole contract. Authorization tests carry
`TestCategory=Authorization`.

The service tests seed reveals through `RevealService` rather than by hand where practical, so
the read model is tested against the shape the writer actually produces rather than against a
fixture's idea of it.

## Design decisions to confirm before build

1. **A marker on `WorldMember` (chosen) vs a separate read-state table.** A column is additive
   and this is one timestamp per membership. A table would earn itself only if per-row read
   state arrives, which the requirements rule out. Leaning column.
2. **Explicit mark-seen (chosen) vs marking on read.** Marking on read loses the list to an
   interruption. Leaning explicit, posted on dismiss or navigate-away.
3. **Reveals only in Phase 1 (chosen).** The wider delta answers a different question and can
   be added over a proven Phase 1. Leaning phased.
4. **`FirstViewLimit` and the page cap.** Numbers not yet chosen; they want a real world's
   reveal history to look at, the same way feature 21's weights do.
5. **Whether GMs see this page at all.** They can already read the reveal sources in the
   ledger, and a GM's own marker is meaningless to them. Leaning: visible to everyone, because
   hiding it from GMs makes it untestable by the only person who can test it.
