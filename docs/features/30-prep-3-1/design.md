# Design Document

> **Amendment (2026-11-02, Phase B):** what building the side conversation changed. The text
> below is left as written.
>
> - **Put in section lives in `AdventureConversationService`**, not `AdventureService`. It reads
>   a conversation message, which is that service's repository, and it writes through the same
>   `IAdventureRepository.TryAcceptDraftAsync` as planned, so the revision, the trim and the
>   stamp are the draft's code. `AdventureService` gains no dependency.
> - **The bound test pins a turn's own material, not the whole message.** The world context
>   alone may be `MaxContextTokens`, so "the whole user message within `MaxContextTokens`"
>   can't hold. `PrepOptionsTests` pins that the section, the history window and the message
>   together fit in `MaxContextTokens` × 4 characters, and that one message fits the window.
> - **Zero is refused at start-up.** `PrepOptions.FirstNonPositiveBound` names the first bound
>   at zero or below, for every Prep bound, not only the four new ones, and the API's options
>   registration validates on start.
> - **"Full" means no room for a turn.** A turn adds two messages, so a conversation is full
>   when its count plus two would pass `MaxConversationMessages`.
> - **Clear deletes the conversation row too**, and the next message starts a new one. Clear
>   is refused with 409 `answer_in_progress` while an answer is being written, through the
>   same gate a turn holds.
> - **A turn streams under the draft's event names.** `done` carries a
>   `ConversationMessageResponse` instead of a section, so there is no second set of names to
>   mirror. The panel reloads the conversation after every turn rather than patching it
>   locally, and "No answer was saved" is shown under any question that no answer follows,
>   so it survives a reload.
> - **Put in section is a small dialog**: the section (the one open first) and Append (the
>   default) or Replace. The page, which owns the editor, saves what is typed first and bases
>   the write on the stamp that save returned.
> - **The layout is three columns at 1,300px of page and up**, the conversation under the
>   editor from 900px, and everything stacked below that. There is no tab.
> - **"Never read by Ask" is pinned by a source scan**, `PrepBoundaryTests`, in place of a
>   `RetrievalScopeTests` case (no such fixture exists). Only an allow-list of files may name
>   the conversation's repository or tables; a retriever, extractor or public route that did
>   would fail it. The same fixture holds Requirement 7's scan: a `LibraryDocument` gets an
>   `AdventureId` only in `LibraryService.RequestUploadAsync`. `LibraryPrintTests` adds that a
>   confirmed print keeps its Adventure and stays GM-only.
> - **The Web's mirrors are pinned** by `ConversationMirrorTests`: the role names, the Put in
>   section modes, and the 4,000-character message box.
> - **The demo clone needed no change.** It copies the tables it names, and these aren't among
>   them.

## Overview

3.1 adds two tables, one AI operation, one prompt and five routes to Prep, and reuses the rest
of 3.0: the drafting client and its output ceiling, the Prep pool, the section gate, the
revision path behind Undo, and the convergence gauge. Nothing new reaches the worker.

```
 GM ── panel ── POST prep/{id}/conversation/messages ──▶ AdventureConversationService
                     (SSE: delta … done | error)          │  GM check, bounds, Prep pool, gate
                                                           │  save the GM's message
                                                           │  retrieve at GM role
                                                           │  AdventureConversationPrompt
                                                           ▼
                                           IAdventureDraftingAiClient (2,500-token ceiling)
                                                           │
                                       answer saved only on a clean end, usage recorded as
                                       AdventureConversation (Prep pool)

 GM ── Put in section ── POST prep/{id}/sections/{sid}/put ──▶ IAdventureRepository.TryAcceptDraftAsync
                                                                (revision kept → Undo; stamp checked)

 GM ── Overview ── GET prep/{id}/threads ──▶ IConvergenceGaugeService (no AI) → top 5
```

## Data

```csharp
Conversation : // Id, WorldId, UserId, AdventureId?, Title, CreatedAt, UpdatedAt
ConversationMessage : // Id, ConversationId, Role, Content, Model?, CreatedAt
ConversationRole : enum { User, Assistant }
```

- `Conversations`:
  - Unique filtered index on `AdventureId` (one per Adventure).
  - FK to `Worlds` cascade; FK to `Adventures` restrict, deleted explicitly in
    `AdventureRepository.DeleteAsync`, the way sections are.
  - `UserId` is the GM who started it.
  - `Title` is the Adventure's title at creation; nothing reads it in 3.1, but the column is
    the domain model's.
- `ConversationMessages`:
  - FK to `Conversations` cascade.
  - Index on `(ConversationId, CreatedAt)`.
  - `Content` is `nvarchar(max)`, bounded in the service: 4,000 for the GM's message; an
    answer is bounded by the output ceiling.
  - `Model` is set on an answer only.
- Migration `AddPrepConversations`, additive. Applied to production before the PR that brings
  it merges (runbook `migration-missed.md`).
- `AdventureId` is nullable because the domain model's `Conversation` is not Prep-only. 3.1
  only ever creates one with an `AdventureId`, and every 3.1 query filters on it.

## Bounds (`PrepOptions`, one place)

| Option | Default | Means |
| --- | --- | --- |
| `MaxConversationMessageChars` | 4,000 | A GM message over it is refused with 400 `prep_too_large`. |
| `MaxConversationMessages` | 400 | At the cap a turn is refused with 409 `conversation_full`; the panel offers Clear. |
| `ConversationHistoryChars` | 12,000 | The history sent with a turn: newest whole messages first, until the next would cross it. |
| `ConversationSectionChars` | 6,000 | The section on screen, cut to this, as a neighbour is in a draft. |

`PrepOptionsTests` pins that a turn's whole user message (world context + outline + section +
history + message) stays within `MaxContextTokens` at 4 characters a token, the way 3.0 pins the
export against `SourceService.MaxBodyChars`. Zero is not a valid value for any of the four; the
options validator refuses it, so none can mean "unbounded".

## A turn

`AdventureConversationService.SendAsync(SendConversationMessageCommand)` returns
`AppResult<IAsyncEnumerable<ConversationEvent>>`, as `DraftAsync` does. In order, everything
before the stream answers with its own status code:

1. GM by current role, else 403.
2. Message non-blank and within `MaxConversationMessageChars`, else 400.
3. Adventure loaded and in this world, else 404. A `sectionId`, if given, must be one of its
   sections, else 404.
4. Message count below `MaxConversationMessages`, else 409 `conversation_full`.
5. `AiBudgetGuard.CheckPrepAsync` (the Prep pool and the global pause).
6. `ISectionDraftGate.TryEnter(adventureId)`, else 409 `answer_in_progress`. Keyed on the
   Adventure, not a section, so a Draft and a turn can run side by side, but two turns can't.
7. Create the Conversation if this is the first message; save the GM's message.

Then the stream:

- Retrieval: `IKnowledgeRetriever` at the caller's role, on the message plus the Adventure's
  and the section's titles; formatted by `LoremasterService.FormatKnowledgeContext` within
  `MaxSessionChars` / `MaxContextTokens`, as Draft does.
- Prompt: `AdventureConversationPrompt` (below). Model `Prep:Deployment ?? Loremaster.AiModel`;
  timeout `DraftTimeoutSeconds`.
- `IAdventureDraftingAiClient.StreamAsync`, so a turn carries the same 2,500-token ceiling and
  `CutOff` signal as a draft. The client is not renamed; it is "the Prep client".
- On a clean end with text, the answer is saved and `done` carries it. A cut-off, empty,
  stopped or failed answer is not saved: `error` with `answer_cut_off`, `answer_empty`, the
  classified failure code, or nothing on a stop.
- Usage is recorded as `AdventureConversation` with the same rules as a draft: cancelled and
  refused answers count, estimated at 4 characters a token when the provider reports none.

The GM's message stays saved whatever happens to the answer (Requirement 2.1). The panel shows
it with "No answer was saved" under it, and the GM can send again.

## The prompt

`AdventureConversationPrompt`, in `Application/Prep`, cache-ordered like `AdventureDraftPrompt`:

1. **System prompt** (fixed): a planning partner for the GM, who is the author. Hidden truth
   may be used and is written for the GM, not as something the players are told. Planned, not
   played. Stay consistent with the record. Be brief and concrete; offer options when asked
   for ideas. When asked to write text for the adventure, use `PrepMarkup`'s conventions
   (read-aloud, prop, ask, stat block), so it can be put in a section as it stands. No
   citation markers.
2. **World context**: the retrieval block.
3. **The Adventure**: title, pitch, and its outline (each section's kind, title, and whether it
   is written).
4. **The section on screen**, if any: title, kind and body, cut to `ConversationSectionChars`.
5. **The conversation so far**: the history window, oldest first, as `GM:` / `Loremaster:`
   turns.
6. **The GM's new message.**

The history is a transcript inside the one user message, not separate chat turns:
`AiPromptRequest` stays one system prompt and one user message, so the ceiling, the executor
and `UnsupportedChatParameterTests` are untouched.

## Put in section

`AdventureService.PutInSectionAsync(PutInSectionCommand)`:
`{worldId, adventureId, sectionId, messageId, mode: Replace | Append, basedOnUpdatedAt}`.

- GM check, load, the message must be an Assistant message in this Adventure's Conversation
  (else 404), the stamp (else 409 `section_changed`).
- New body: Replace → the answer; Append → the existing body, a blank line, the answer. An
  unwritten section (the skeleton's template text, `AdventureSkeleton.IsUnfilled`) is replaced
  even on Append, so the template never survives above real text.
- `PrepBounds.Validate` on the result, else 400 `prep_too_large`.
- `IAdventureRepository.TryAcceptDraftAsync` with a revision whose `Instruction` is
  "Put in from the conversation" and whose `Model` is the answer's. That is the method a draft
  uses, so the revision cap, the trim and the stamp check are the same code, and Undo restores
  the previous body.

## Threads to use

`GET prep/{id}/threads` → `IConvergenceGaugeService.GetGaugeAsync` at the caller's role, the
first five candidates as `ThreadToUseResponse(Kind, Id, AnchorArtifactId, AnchorName, AnchorSlug?,
Description, Score)`. The Web shows the gauge's reasons with the existing `ConvergenceDisplay`,
the one place they are worded.

The Overview's draft prompt gains a "Threads ready to pay off" block of the top five, one line
each (anchor, description), only when the section being drafted is the Overview. It goes after
the world context, so the cache prefix is unchanged for every other section.
`AdventureDraftPrompt` takes the threads as input; `AdventureDraftingService` reads the gauge
only for an Overview draft.

The gauge's optional AI narration is never called from Prep.

## API

```
GET    api/worlds/{w}/prep/{id}/conversation                 GM → {messages[], isFull}
POST   api/worlds/{w}/prep/{id}/conversation/messages        GM {text, sectionId?} → SSE delta | done | error
DELETE api/worlds/{w}/prep/{id}/conversation                 GM → 204
POST   api/worlds/{w}/prep/{id}/sections/{sid}/put           GM {messageId, mode, basedOnUpdatedAt} → section
GET    api/worlds/{w}/prep/{id}/threads                      GM → ThreadToUseResponse[]
```

All five join `PrepEndpointTests.Routes()`, so the 403 sweep covers them. `{id}` is
Guid-constrained, so none of the literals can shadow an Adventure slug.

## Web

- **Adventure page**: a third column on wide screens, or a tab under the editor on narrow
  ones, titled *Talk it through*.
  - Messages oldest first.
  - The streaming answer is shown as plain text, as a draft's preview is.
  - A message box with Send and Stop.
  - Each saved answer has **Put in section** (a menu of sections, then Replace or Append),
    and the result reloads the section and its Undo count.
  - **Clear conversation** asks first.
- **Overview**: a *Threads to use* card above the instruction box when the Overview is
  selected, with **Use in this adventure** on each, which appends a line to the instruction
  box.
- The streamed answer uses the shared `StreamEventsAsync` reader; its event names are mirrored
  and pinned by a mirror test, as the draft's are.

## Export, deletion, visibility

- `WorldExportService`'s `prep.json` gains each Adventure's conversation (messages with role,
  content, model and time).
- `WorldRepository`'s world wipe deletes `ConversationMessages` and `Conversations`, before
  `Adventures`; `WorldDeletionServiceTests` covers both.
- No retriever, extractor or public route reads either table. `RetrievalScopeTests` gains a
  case that a Conversation's text never appears in a GM's Ask context.

## Pre-implementation checks

1. **Where does each rule live?**
   - The bounds live in `PrepOptions`.
   - The pool lives in `AiSpendPools`.
   - Put in section writes through `TryAcceptDraftAsync`, the one revision path.
   - The gauge's reasons are worded only by `ConvergenceDisplay`.
   - The SSE event names are pinned by a mirror test.
2. **A second caller?** Put in section and Draft share the repository method, so a third way
   to write a section (one-click PDF, if built, files and doesn't write) would get the
   revision and the stamp for free. Bibliotaph's path is pinned by Requirement 7's test.
3. **Null, zero, empty?**
   - A null `AdventureId` on a Conversation is "not Prep", and 3.1 never creates one.
   - A null `sectionId` on a turn is "no section on screen".
   - Zero is refused for every new bound.
   - An empty answer is refused, never saved.
4. **Unbounded?**
   - Messages are bounded by `MaxConversationMessages` × `MaxConversationMessageChars` plus
     the output ceiling.
   - The history sent is bounded by `ConversationHistoryChars`, and the gauge by its top five.
   - Every AI call has `DraftTimeoutSeconds`.
5. **Red first.** Every test below is checked by breaking what it covers. The named sabotages:
   - drop the GM check;
   - drop the history window;
   - save an answer on a cut-off;
   - write Put in section without a revision;
   - put `AdventureConversation` in the daily pool;
   - read a conversation from Ask's retriever.

## Testing

- `AdventureConversationServiceTests`:
  - the refusal order;
  - the message saved before the call;
  - the answer saved only on a clean end;
  - Hidden in context at GM, through the real `KeywordKnowledgeRetriever`;
  - the history window cut at whole messages;
  - the gate;
  - `conversation_full`;
  - usage under `AdventureConversation`.
- `AdventureConversationPromptTests`:
  - cache order;
  - the section cut;
  - the transcript format;
  - the PrepMarkup conventions named.
- `PutInSectionTests`:
  - Replace;
  - Append;
  - Append onto an unwritten section;
  - the stamp;
  - the bound;
  - Undo restores;
  - only an Assistant message of this Adventure.
- `AiSpendPoolsTests`: `AdventureConversation` → Prep.
- `AdventureRepositoryTests` / `WorldDeletionServiceTests`: cascade and wipe.
- `PrepEndpointTests`:
  - the five routes in the 403 sweep;
  - a streamed turn;
  - Put in section then Undo;
  - Clear;
  - threads.
- bUnit:
  - the panel streams and saves;
  - a failed answer says nothing was saved;
  - Put in section sends the stamp;
  - Clear confirms;
  - the threads card appends to the instruction box.
- `LibraryPrintPathTests` (Requirement 7): a print gets its `AdventureId` only through
  `RequestUploadAsync`, and no other code path sets `LibraryDocument.AdventureId`. The second
  half is checked by scanning `src/` for assignments, as `UnsupportedChatParameterTests` does.

## Design decisions

1. **One conversation per Adventure.** A list of named conversations is a second navigation
   inside a page that already has an outline. Clear covers "start over".
2. **Put in section is explicit.** The model never writes to a section on its own; the GM
   chooses the section and the mode, and Undo is there.
3. **Its own operation, the same pool.** `AdventureConversation` shows separately on Costs, so
   the GM can see what brainstorming costs against drafting, and the Prep budget caps both.
4. **The transcript rides in one user message.** It keeps one request shape for every AI call,
   and the ceiling and its scan untouched.
5. **One-click PDF is deferred**, and if built it is its own container. Chromium in the worker
   would make every extraction pay for its memory.
