# Tasks

Ships as Nornis 3.12. One migration (`AddDraftEditOutcomes`), which Dave applies before the merge.

**Specified 2026-11-06 22:20Z. Dave asked for Design-mode answers to change the draft, not the
chat; for a reply to scroll from his message; and for the cut-off to be caught, with room for
longer answers. He approved spec, build and ship in one go.**

## Phase A — Spec

- [x] A1. `requirements.md`, `design.md` and this file; row 41 in the features README; amendment
      to feature 37's design.

## Phase B — Build

- [ ] B1. Cut-off: the finish reason through `AzureOpenAiChatClient`; the executor's token check;
      the open-fence check. Tests, each shown failing first.
- [ ] B2. Ceilings: `AiPromptRequest.OutputCeilingTokens`, the Design bounds, the gate's lease.
- [ ] B3. `DraftEditOutcome`, the column and migration, `DraftEdits.ApplyAll`, whole-draft
      passages, `ApplyAnswerEditsAsync`, outcomes on hand Apply. Tests.
- [ ] B4. The conversation service: apply before save, `DraftEditStream`, the `writing` event;
      the prompt; `HistoryText`. Tests.
- [ ] B5. Web: outcomes on the DTO, the panel's summary and Show changes, writing status, the
      page's save/read-only/reload, scroll to the GM's message. bUnit tests.
- [ ] B6. Ship: `MinorVersion` 12; change log, Features page, README; validate (clean build,
      full test run, `dotnet format`).

## Phase C — Live

- [ ] C1. Dave applies `AddDraftEditOutcomes` to prod; merge; confirm the new revision serves.
- [ ] C2. Live walk (Dave): in Design mode, ask for a whole adventure on an empty draft, then a
      change to one scene; check the draft changes, Undo steps back, and the chat stays short.
