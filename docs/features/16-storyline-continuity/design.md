# Design Document

Two pieces: a deterministic **staleness signal** (Requirement 1) and a GM **session
wrap-up** surface (Requirement 2) that consumes it. The signal is the shared foundation;
the wrap-up is mostly wiring the signal plus already-existing proposals to one screen and
one confirm action.

## 1. Shared timeline derivation (refactor first)

`ArtifactService.GetStorylineTimelineAsync` already computes the exact thing staleness
needs: per (storyline, dated session) developments, under the caller's visibility filter.
Rather than duplicate the reference-attribution logic, extract the core into a reusable
producer:

- New internal method / helper, e.g. `StorylineDevelopmentReader.ReadAsync(worldId, filter,
  role, ct)` (or a private method on `ArtifactService` exposed through the service
  interface), returning the intermediate model both callers share:
  - `Sessions`: visible dated sources ordered by `OccurredAt`.
  - `DevelopmentsByStoryline`: `storylineId → list of (SourceId, OccurredAt, developments)`.
  - `Storylines`, `ParentByChild`, open-question facts.
- `GetStorylineTimelineAsync` becomes a projection of this into `StorylineTimeline`.
- The new continuity service becomes a second projection into the staleness model.

Keep this refactor behavior-preserving; the existing timeline tests
(`ArtifactServiceTimelineTests`) are the regression net.

## 2. Staleness signal

New Application service `StorylineContinuityService : IStorylineContinuityService`.

```csharp
Task<AppResult<StorylineContinuityReport>> GetContinuityReportAsync(
    Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct);
```

Model (`Nornis.Application.Models`):

```csharp
public record StorylineContinuityReport(
    int ActiveCount,
    int StaleThresholdSessions,
    TimelineSessionRef? LatestSession,       // most recent visible dated session
    IReadOnlyList<QuietStoryline> Quiet,     // stale, ordered most-quiet first
    IReadOnlyList<UnanchoredStoryline> Unanchored);

public record QuietStoryline(
    Guid StorylineId, string Name, string Status,
    DateTimeOffset? LastDevelopmentAt,
    int SessionsSinceLastDevelopment,
    int OpenQuestionCount,
    Guid? ParentStorylineId);

public record UnanchoredStoryline(
    Guid StorylineId, string Name, DateTimeOffset CreatedAt, int OpenQuestionCount);
```

Computation (no AI):

1. Read the shared derivation (section 1) for `worldId` under the caller's filter.
2. `orderedSessions = Sessions.OrderByDescending(OccurredAt)`; index each session by its
   position so "sessions since" is a count of distinct sessions, not days.
3. For each **Active** storyline:
   - `lastDev = max OccurredAt` across its developments, or none.
   - `sessionsSince = number of sessions whose OccurredAt > lastDev` (0 if it was touched
     in the latest session). No dated development → **unanchored**, not quiet.
   - `openQuestionCount` = facts with predicate `"open question"` and `TruthState != False`.
   - Flag **quiet** when `sessionsSince >= StaleThresholdSessions`.
4. Order `Quiet` by `sessionsSince` desc, then `openQuestionCount` desc (a dangling loose
   end outranks a clean lull).

Threshold: `ContinuityOptions.StaleThresholdSessions` (default **3**), a new options class
bound from configuration, mirroring `ExtractionOptions`/`LoremasterOptions`. The same
options class carries `RecentSessionWindow` (default **2**) used by the wrap-up sections.
Per-world overrides are explicitly future work.

Visibility: reuse `VisibilityFilter.ForRole` + `CanSeeSource` (already applied inside the
shared derivation), so no new visibility surface is introduced.

### API

`GET /api/worlds/{worldId}/storylines/continuity` → `StorylineContinuityReport`
(GM-gated at the surface per Requirement 2, though the read itself is harmless; gate in
the controller to match the wrap-up being GM-only). Add a response contract mirroring the
model, consistent with `StorylineTimelineResponse`.

## 3. Session wrap-up

The wrap-up is an aggregate read + one write endpoint.

### Read: `GET /api/worlds/{worldId}/storylines/wrap-up`

Returns a `WrapUpView` assembled from data that already exists:

- **Advanced**: from the shared derivation — storylines with a development in the last *k*
  dated sessions (`ContinuityOptions.RecentSessionWindow`, default **2**). Read-only.
- **GoneQuiet**: `StorylineContinuityReport.Quiet`.
- **CouldNest**: pending `PartOf` `AddRelationship` proposals (status `Pending`) whose
  child storyline was touched within the last *k* sessions — surfaced from the existing
  review-proposal store, not recomputed. Each carries proposalId, child/parent names,
  rationale, confidence.
- **UnparentedArcs**: storylines whose first dated development falls within the last *k*
  sessions, that have no `PartOf` parent and are not Archived.

If all four are empty → `HasWork = false` (Requirement 2.5).

GM-gated in the controller (`member.Role == GM`, 403 otherwise).

### Write: `POST /api/worlds/{worldId}/storylines/wrap-up`

Body = the GM's decisions collected client-side, e.g.:

```jsonc
{
  "closures":   [ { "storylineId": "…", "status": "Dormant" }, … ],
  "acceptProposalIds": [ "…", … ],   // CouldNest suggestions accepted
  "rejectProposalIds": [ "…", … ],   // CouldNest suggestions rejected
  "parents":    [ { "childId": "…", "parentId": "…" }, … ],  // UnparentedArcs
  "dismissStorylineIds": [ "…", … ]  // "still active" — snoozed, see below
}
```

New `StorylineWrapUpService` applies them in one transaction, reusing existing machinery:

1. **Closures (apply-on-confirm)** — mirror the retrospective's provenance pattern: create
   one synthetic `Source` (`GMNote`, `GMOnly`, `Processed`, title `Session wrap-up —
   {date}`) and one `ReviewBatch` with `Kind = "SessionWrapUp"` (Requirement 3.4). For each
   closure add an `UpdateArtifact { status }` `ReviewProposal` + `SourceReference`. Because
   these are the GM's explicit confirmations, apply them immediately via the existing
   `IProposalApplicator` and mark each proposal `Accepted` by the acting user — the same
   confirm-and-apply pattern `ArtifactMergeService` uses. The click *is* the confirmation;
   the record still carries full provenance and history.
2. **acceptProposalIds / rejectProposalIds** — route through the existing proposal
   accept/reject flow; no new lineage model (Requirement 3.2).
3. **parents** — call the existing `SetStorylineParent` command per pair.
4. **dismiss** — "still active" acknowledgement; see snooze decision below.

Return a summary (`{ closed, nested, parented, dismissed }`) and the wrap-up batch id so
the UI can deep-link to the review queue if closures were left Pending.

### UI — card on the Ask landing

A `SessionWrapUpCard` component on the Ask/Home landing (`/`), GM-only, so wrap-up lands at
the natural cadence: the GM opens the app after a session and the card is right there. Not
a separate destination page.

- The card renders only when `HasWork == true`; otherwise it is absent (no empty scaffold
  on the landing — the "nothing to wrap up" state of Requirement 2.5 is simply the card not
  showing). A GM can still reach a full wrap-up on demand via a nav affordance.
- Four sections mirroring the read model; toggles/buttons collect decisions client-side; a
  single **Apply** submits the write. Untouched → no-op (Requirement 2.4).
- GM-only nav entry with a badge (count = quiet + pending-nest) per Requirement 2.6, which
  also serves as the on-demand entry point when the landing card has been dismissed.
- The existing "Run retrospective" button on [Timeline.razor](../../../src/Nornis.Web/Components/Pages/Timeline.razor)
  stays; the wrap-up card is the ambient path, the button the manual one.

## 4. Surfacing "new since last wrap-up" (Requirement 2.6) — stateless

No per-user review state and no migration. The landing card shows whenever `HasWork ==
true` (quiet storylines, pending nest suggestions, or unparented recent arcs exist). Acting
on items removes them from the next read, so the card naturally empties as the GM works
through it.

A quiet storyline the GM deliberately leaves open would otherwise keep reappearing; the
**dismiss** ("still active") action handles that as a **client-persisted snooze** (e.g.
`localStorage` keyed by world + storyline + latest-session id), so it re-surfaces only when
a *newer* session touches nothing on it. If GMs later want durable, cross-device "I've seen
this" state, graduate to a `WorldMember.LastWrapUpAt` column — explicitly deferred.

## 5. Optional follow-on: auto-pre-populate proposals

Independent of the above, the "Could nest" and "Gone quiet" sections are richer when the
existing sweeps have already run. A follow-on can, after a source finishes extraction,
enqueue the retrospective and/or relationship backfill when staleness crosses the
threshold — so proposals are waiting when the GM opens the wrap-up. This reuses the
existing worker/queue and both existing services unchanged. Kept **out of the first cut**
so the signal + surface land independently; called out here as the natural next lever.

## Resolved decisions

1. **Closures — apply-on-confirm.** The GM's click is the confirmation; proposals are
   applied immediately via `IProposalApplicator` and recorded `Accepted`, with full
   provenance (design §3, write step 1).
2. **"New since last wrap-up" — stateless.** Card shows whenever `HasWork == true`; the
   dismiss action is a client-persisted snooze. No migration (design §4).
3. **Wrap-up home — a card on the Ask landing**, not a dedicated page (design §3 UI).
4. **Recent window — last *k* sessions**, `ContinuityOptions.RecentSessionWindow` (default
   2), used by "Advanced", "CouldNest", and "Unparented arcs".

## Testing

- `StorylineContinuityService`: staleness math (0/threshold-1/threshold/beyond),
  unanchored exclusion, open-question counting, visibility parity with the timeline
  (a player must not see a GM-only quiet storyline), empty world.
- Shared-derivation refactor: `ArtifactServiceTimelineTests` must stay green unchanged.
- `StorylineWrapUpService`: role gate; closures produce `SessionWrapUp`-kind batch +
  applied/accepted proposals with provenance; accept/reject routes to the proposal flow;
  parent assignment reuses `SetStorylineParent`; empty decision set is a no-op.
- Controller: GM-only 403s for the wrap-up read/write; continuity read shape.
