# Tasks

Three PRs. The spec, then the side conversation (the only migration, additive), then Threads
to use and the version bump. Nothing in Phase B depends on Phase C, so C can ship without B if
B stalls.

**Status: specified 2026-11-02.** Ships as Nornis v3.1. One-click PDF is deferred (see
requirements.md, Staging), pending Dave's call on the design doc.

## Phase A — Spec

- [x] A1. `requirements.md`, `design.md` and this file.
- [x] A2. `domain-model.md` amendment: `Conversation` is built, with `AdventureId`, GM-only by
      current role, never extracted (Requirement 8).
- [x] A3. Row 30 in `docs/features/README.md`.

## Phase B — The side conversation

- [ ] B1. Domain: `Conversation` (+ `AdventureId?`), `ConversationMessage`, `ConversationRole`,
      `AiOperationType.AdventureConversation`. EF configurations: World FK cascade, Adventure
      FK restrict with an explicit delete in `AdventureService.DeleteAsync`, the unique
      filtered index on `AdventureId`, and the `(ConversationId, CreatedAt)` index. Update the
      enum-definition tests.
- [ ] B2. Migration `AddPrepConversations`. **Applied to prod by Dave before the PR merges**
      (docs/runbooks/migration-missed.md).
- [ ] B3. `PrepOptions` gains the four conversation bounds; zero refused; a bound test pinning
      `MaxConversationMessageChars <= ConversationHistoryChars`.
- [ ] B4. `AiSpendPools.PoolOf`: `AdventureConversation` → Prep. Sabotage: the daily pool.
- [ ] B5. `AdventureConversationPrompt`, cache-ordered, with its tests.
- [ ] B6. `AdventureConversationService` (`GetAsync`, `SendAsync` streaming, `ClearAsync`),
      the per-Adventure gate, usage recording. Tests per design.md, including Hidden at GM
      through the real `KeywordKnowledgeRetriever`, and Ask's retriever never reading a
      conversation.
- [ ] B7. `AdventureService.PutInSectionAsync` through `TryAcceptDraftAsync`. `PutInSectionTests`.
- [ ] B8. `PrepController`: the four conversation routes, in the 403 sweep. Endpoint tests,
      including a streamed turn and Put in section then Undo.
- [ ] B9. World lifecycle: `WorldDeletionService` wipes both tables; `prep.json` carries each
      Adventure's conversation; the demo clone skips it.
- [ ] B10. Web: the **Talk it through** panel (stream, stop, saved-or-not line, Put in section
      dialog, Clear), the SSE event-name mirror test, bUnit tests.
- [ ] B11. `LibraryPrintPathTests` (Requirement 7).

## Phase C — Threads to use, and ship 3.1

- [ ] C1. `GET prep/{id}/threads` over `IConvergenceGaugeService`, top five, in the 403 sweep.
- [ ] C2. The Overview's draft prompt gains "Threads ready to pay off", bounded. Prompt test.
- [ ] C3. Web: the Threads card on the Overview, **Use in this adventure** appending to the
      instruction box, the "nothing hidden yet" line. bUnit tests.
- [ ] C4. Ship: `MinorVersion` 1, changelog entry, Features page, README, steering re-check
      notes, feature README row updated.
- [ ] C5. Live walk in Gribbits: a conversation grounded in Hidden truth, an answer put in a
      section and undone, a thread used in an Overview draft, the Prep pool on Costs showing
      both operations.

## Deferred

- One-click PDF, as a separate `ca-nornis-print` container if it is ever built.
