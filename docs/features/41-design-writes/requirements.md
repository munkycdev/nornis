# Requirements Document

## Introduction

On 2026-11-06 Dave planned a one-shot ("The Great Toilet Heist", world Gribbits Detective Agency)
in Prep's side conversation, in Design mode. When he asked the Loremaster to build the adventure
out, its answer (8,176 characters, one ```` ```edit ```` fence) stopped mid stat block. Four turns
later the Loremaster said it had never seen the adventure and asked him to paste it back. Reading
the stored conversation showed two real defects and one design gap:

1. **The cut-off was missed.** Every OpenAI stream (Nornis's Azure key and an OpenAI user key) is
   read by `AzureOpenAiChatClient`'s own streaming override, which never copies the finish reason,
   so `ChatFinishReason.Length` never reaches the executor and a cut-off answer is saved as if it
   ended. Its edit fence never closed, so there was nothing to Apply.
2. **The answer fell out of the history window.** A turn carries the newest whole messages up to
   12,000 characters and stops at the first that doesn't fit, so one 8,176-character answer
   dropped out after four short turns, and everything older with it.
3. **The adventure text lived in the chat.** Design mode shows edits as cards to apply one by one,
   with Put in draft as the other route. Until the GM acts, the text exists only in the
   conversation, which is exactly the part of the prompt that is windowed. The output ceiling
   (2,500 tokens, drafting's) is also too small to write a whole adventure.

Dave asked (22:05Z) for the Loremaster to work on the Design text box itself: when he asks it to
generate the adventure, bring something new in or replace it, the draft changes, not the chat.
He also asked that a reply scroll so his own message is at the top, with the answer growing
below it, and that the cut-off be caught properly with room for longer answers. He approved
spec, build and ship together (22:09Z).

Ships as Nornis 3.12. One migration (`AddDraftEditOutcomes`), which Dave applies before the merge.

## Glossary

- **Design turn**: a side-conversation turn sent from the Design tab (scope Draft, feature 37).
- **Edit**: one ```` ```edit ```` fence in an answer (`DraftEdit`): a passage and its replacement.
  An empty passage means the whole draft.
- **Outcome**: what happened to one edit of a Design turn's answer: Applied, Did not fit (its
  passage isn't in the draft exactly once), or Not applied (the draft changed while the answer was
  written, or the result would be too long).

## Requirements

### Requirement 1 — The Loremaster writes into the draft

**User Story:** As a GM in Design mode, I want the Loremaster's changes to land in the draft, so
the adventure text never lives only in the chat.

#### Acceptance Criteria

1. WHEN a Design turn's answer ends cleanly THEN THE system SHALL apply its edits to the draft, in
   order, before the answer is saved, as one draft change with one revision (one Undo step).
2. An edit whose passage is not in the draft exactly once SHALL be skipped, and the others still
   applied. Its outcome is Did not fit.
3. WHEN the draft changed between the turn and the write, or the result would exceed
   `Adventure.MaxDraftChars`, THEN no edit SHALL be applied and every outcome is Not applied.
4. The answer SHALL be saved with its outcomes. An edit that didn't land keeps its Apply button;
   applying it by hand updates its outcome.
5. An empty passage SHALL replace the whole draft. The prompt reserves it for an empty draft or a
   GM who asks to write or rewrite the whole adventure.
6. Applied text SHALL count as the Loremaster writing (feature 38's AI use), and a research turn's
   as researched on the web (feature 40), exactly as a hand-applied edit does.
7. The chat SHALL show the answer's prose and a line saying what changed in the draft; the edits'
   text SHALL be behind "Show changes". While the answer streams, edit text SHALL NOT be shown;
   the panel says it is writing into the draft.
8. The Design editor SHALL be read-only while a Design turn runs, what the GM typed SHALL be saved
   before the turn is sent, and the editor SHALL show the new draft when the turn ends.
9. Section mode SHALL be unchanged: answers go into a section only through Put in section.

### Requirement 2 — The conversation keeps its thread

1. In the history a turn carries, an answer with edits SHALL be shown as its prose plus a one-line
   note per edit (what it changed, how long, and its outcome), not the edit text. In Design mode
   the whole draft already rides with every turn.
2. The history window SHALL measure the messages as they are shown to the model.

### Requirement 3 — Cut-off answers are caught

1. A stream SHALL count as cut off when the provider says it stopped for length, on every provider
   path, including OpenAI's streaming override.
2. A stream SHALL also count as cut off when the provider reports output tokens at or above the
   call's ceiling.
3. A Design turn's answer SHALL also count as cut off when an edit fence is opened and never closed.
4. A cut-off answer SHALL NOT be saved, and none of its edits applied, and the GM SHALL be told so.

### Requirement 4 — Longer Design answers

1. A Design turn SHALL have its own output ceiling (`PrepOptions.DesignOutputCeilingTokens`,
   16,000, enough for the 60,000-character draft) and timeout (`DesignTimeoutSeconds`, 220, under
   Azure Container Apps' 240-second ingress limit). A research Design turn takes the larger of each.
2. Every other Prep call SHALL keep its ceiling and timeout.
3. The one-turn-at-a-time lease SHALL outlive the longest Prep call.

### Requirement 5 — The reply scrolls from the question

1. WHEN the GM sends a message THEN THE panel SHALL scroll its message list so that message is at
   the top, and SHALL NOT follow the answer as it grows.
2. WHEN the turn ends and the list reloads THEN the same message SHALL stay at the top.
