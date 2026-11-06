# Design Document

## Overview

```
Design tab ── save typing ──► send (scope Draft) ──► AdventureConversationService
                                                     │  ceiling 16,000 / 220 s (Design)
                                                     │  stream ─► DraftEditStream ─► prose deltas + "writing"
                                                     │  cut off? provider Length | tokens ≥ ceiling | fence left open
                                                     ▼
                                       AdventureDesignService.ApplyAnswerEditsAsync
                                         DraftEdits.ApplyAll(draft, edits) → draft + outcomes
                                         one write, one revision (Edit), on the draft's stamp
                                                     ▼
                                       answer saved with DraftEditOutcomes ──► panel: prose + "Changed the draft"
                                                                              page: reloads the draft into the editor
```

## Data model (migration `AddDraftEditOutcomes`)

- `DraftEditOutcome` (Domain enum): `Applied`, `DidNotFit`, `NotApplied`.
- `ConversationMessage.DraftEditOutcomes` (`IReadOnlyList<DraftEditOutcome>?`), stored as
  `DraftEditOutcomes nvarchar(max) null`, the names comma-separated. One per edit, in the
  answer's order.
  - **Null**: the answer isn't a Design turn's with edits: a GM message, a Section turn, an answer
    with no edits, or anything saved before 3.12. Those answers behave exactly as before (cards
    with Apply).
  - **Empty** never happens: a Design answer with no edits stores null.
  - Bounded by the answer, which is bounded by the output ceiling.

## The rules, each in one place

| Rule | Lives in |
| ---- | -------- |
| What an edit is, how it applies, an empty passage = the whole draft | `DraftEdits.Parse` / `Apply` / `ApplyAll` |
| A fence left open | `DraftEdits.HasUnclosedFence` |
| What streams while an answer is written | `DraftEditStream` (feeds on chunks, uses `DraftEdits`' markers) |
| Writing the draft with a revision, AI use and web research | `AdventureDesignService.WriteWithRevisionAsync` (now also behind `ApplyAnswerEditsAsync`) |
| A stream that stopped for length | `AiCallExecutor` (finish reason, or output tokens ≥ the call's ceiling) |
| An answer cut off | `AdventureConversationService` (the executor's flag, or an open fence on a Design turn) |
| How an answer reads in the history | `AdventureConversationPrompt.HistoryText` (used by both `HistoryWindow` and the transcript) |
| A Design turn's ceiling and timeout | `PrepOptions.DesignOutputCeilingTokens` / `DesignTimeoutSeconds` |
| The gate's lease | `PrepOptions.LongestCallSeconds` |

## Applying a Design answer

`AdventureDesignService.ApplyAnswerEditsAsync(ApplyAnswerEditsCommand)` takes the world, the
Adventure, the GM's role, the parsed edits and whether the turn searched the web:

1. Reload the Adventure; refuse a non-GM as every other draft write does.
2. `DraftEdits.ApplyAll(draft, edits)` applies each edit to the result of the one before;
   an edit that doesn't fit is `DidNotFit` and leaves the text alone.
3. No edit applied: return the outcomes, write nothing.
4. Over `Adventure.MaxDraftChars`: every outcome `NotApplied`, write nothing.
5. Otherwise one `WriteWithRevisionAsync(..., AdventureDraftChange.Edit, basedOn: the stamp just
   read)`. On a lost race (the GM saved in between) reload and try once more; a second loss makes
   every outcome `NotApplied`.

The conversation service calls it after the stream ends and before the answer is saved, then saves
the answer with the outcomes. The page reloads the Adventure when the done event's answer has any
`Applied`, so the editor, the Undo count and the AI-use box are the server's.

Hand Apply (`POST draft/edits`) works as before, and when the message has outcomes it sets that
edit's to `Applied` (`IConversationRepository.SetDraftEditOutcomesAsync`), so a reload doesn't
offer Apply on text that already landed.

**Why apply on the server, at the end.** The answer is the only copy until it's applied; applying
in the same request means a GM who leaves mid-answer still finds the draft changed, and the client
never sends text to apply (feature 37's rule). Streaming text into the editor live was weighed and
left out: the editor would have to merge with the GM's caret, and the draft must change in one
undoable step anyway.

## Whole-draft writes

`DraftEdits.Apply` with an empty passage now replaces the whole draft, empty or not (feature 37
allowed it only on an empty draft). The prompt says to use it only when the draft is empty or the
GM asks to write or rewrite the whole adventure, and to keep a whole adventure under about 9,000
words so it fits one answer. Undo takes it back.

## Streaming

`DraftEditStream.Push(text)` returns the prose to show and whether an edit fence opened. It works
on whole lines (a partial line waits for its newline, flushed at the end), hides everything from a
```` ```edit ```` line to the ```` ``` ```` that closes it, and raises one "writing" per fence. It is
display only: the saved answer is still parsed by `DraftEdits` afterwards, so a malformed fence
that the stream hid shows up as prose once the list reloads.

`ConversationEvent.Writing` goes out as the SSE event `writing` (`{}`). An older Web ignores it.

## Cut-off

- `AzureOpenAiChatClient` copies `FinishReason` (`length` → `ChatFinishReason.Length`, and the
  others) onto the update, and yields an update that carries only a finish reason. This was the
  defect: every OpenAI stream lost it.
- `AiCallExecutor` also sets `CutOff` when the reported output tokens reach the call's
  `MaxOutputTokens`, for providers that stop without saying why.
- A Design turn whose text has an open fence is cut off.
- A cut-off answer is refused as before (`answer_cut_off`): nothing saved, nothing applied.

## Ceilings and timeouts

- `AiPromptRequest.OutputCeilingTokens` (`int?`): null means the client's own ceiling
  (`AdventureDraftingAiClient.OutputCeilingTokens`, 2,500, or a research turn's). Set only by a
  Design turn.
- `PrepOptions.DesignOutputCeilingTokens` 16,000 and `DesignTimeoutSeconds` 220. A Design research
  turn takes the larger ceiling and timeout. Both join `FirstNonPositiveBound`.
- `SectionDraftGate`'s lease is `LongestCallSeconds` (the largest Prep timeout) + 60. It was
  `DraftTimeoutSeconds` + 60 = 150 s, shorter than a 180 s research turn already.
- 220 s stays under Azure Container Apps' 240 s request limit on the Web → API hop.

## History

`HistoryText(message)`: for an answer with edits, its prose and then per edit one line,
`[Edit to the draft: replaced "first words…" with 1,234 characters — applied]`, or
`[Edit to the draft: wrote the whole draft, 8,176 characters — applied]`. The outcome is left out
when the message has none. `HistoryWindow` sums these lengths, so a large write costs a few
dozen characters of window.

## Web

- `ConversationMessageDto.DraftEditOutcomes` (names). For an answer with outcomes the panel shows
  the prose, "Changed the draft: N edits. Undo on the draft takes it back." (or what didn't land),
  the cards of edits that didn't land with Apply, and "Show changes" for the rest.
- While a Design turn streams: prose as it comes, and "Writing into the draft…" once an edit opens.
- The page passes `BeforeSend` (save the draft if dirty; a failed save stops the send),
  `OnTurnRunning` (the design editor is read-only meanwhile) and `OnDraftEdited` (reload the
  Adventure and remount the editor).
- Scroll: each message has `data-talk-index`; after the GM's message renders, and again after the
  reload, `nornisCompose.scrollToMessage(list, index)` puts it at the top of the list. Nothing
  scrolls while the answer streams.

## Pre-implementation checks

1. **Where each rule lives**: the table above.
2. **Second caller.** The executor's token check covers drafting, planning and covers too. Hand
   Apply and auto-apply share `WriteWithRevisionAsync`, so both set AI use and web research.
3. **Null, empty, zero.** Outcomes null = "behave as before"; never empty. `OutputCeilingTokens`
   null = "the client's ceiling", never zero (the bounds refuse zero).
4. **Unbounded.** Outcomes are bounded by the answer; the answer by the ceiling; the draft by
   `MaxDraftChars`; the history by `ConversationHistoryChars`.
5. **Show each guard failing.** The finish-reason copy (remove it: the stream test fails); the
   token check; the open-fence check; the history note (a long edit must no longer push older
   messages out).

## Out of scope

- Streaming the text live into the editor.
- Section mode writing into sections on its own.
- A "suggest only" switch to keep the old Apply-each-card behaviour (hand Apply stays for edits
  that didn't land and for answers from before 3.12).
- Deleting `AzureOpenAiChatClient`'s override in favour of the stock adapter (its own change, as
  that class says).
