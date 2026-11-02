# Requirements Document

## Introduction

Prep shipped in Nornis 3.0 (feature 29) with a per-section instruction box as its only way to
talk to the Loremaster. That box is enough to write a section. It is not enough to *think*: to
ask "who else could the cult have recruited?", get three answers, push back on one, and keep
the good line for Scene 4. Today that thinking happens in a chat window that has never seen
the world, which is the gap Prep was built to close.

Nornis 3.1 adds two things to Prep:

- **A side conversation** per Adventure. The GM brainstorms with the Loremaster beside the
  editor, grounded in the whole record, and puts any answer into a section with one click and
  an Undo.
- **Threads to use.** The Overview shows the hidden truths the convergence gauge ranks as
  closest to paying off, so an adventure can be built around what the world is ready to
  reveal.

Both are planning. Neither touches canon: the only way Prep reaches the record is still Export,
through review.

The two other items 3.0 staged for 3.1 are settled here:

- **The booklet reaching Bibliotaph** needs nothing from Prep. A print is an ordinary Library
  PDF, and the Library/Bibliotaph integration catalogues those (confirmed with that thread,
  2026-11-02). 3.1 pins the three things that path relies on.
- **One-click PDF** (the worker rendering the booklet with headless Chromium) is deferred. It
  would put a Chromium runtime into production to save two clicks for one GM. See Staging.

## Glossary

- **Conversation**: the saved chat between the GM and the Loremaster about one Adventure. The
  noun `domain-model.md` has described since the MVP, built now with an `AdventureId`.
- **Turn**: one GM message and the Loremaster's answer to it.
- **Put in section**: copying an answer into a section, replacing or appending, as an undoable
  revision.
- **Thread to use**: a hidden fact, relationship or artifact the convergence gauge ranks as
  ready to pay off, offered on the Overview.

## Requirements

### Requirement 1 — A conversation per Adventure

**User Story:** As a GM, I want to talk an adventure through with a Loremaster that knows my
world, and come back to that conversation tomorrow.

#### Acceptance Criteria

1. Each Adventure SHALL have at most one Conversation, created when the GM sends its first
   message.
2. The Conversation SHALL be shown in a panel on the Adventure page, beside the section being
   edited, oldest message first, and SHALL persist across visits and devices.
3. Only a caller whose current role in the world is GM SHALL read or write a Conversation. A
   Player, an Observer and a GM viewing as a Player SHALL get 403 before anything is read.
4. **Clear conversation** SHALL delete every message after a confirmation. It SHALL NOT touch
   the Adventure's sections or their revisions.
5. Deleting the Adventure SHALL delete its Conversation. Deleting the world SHALL delete
   every Conversation in it.

### Requirement 2 — A turn

**User Story:** As a GM, I want the answer to arrive as it is written, and never to lose my
question.

#### Acceptance Criteria

1. The GM's message SHALL be saved before the model is called, so a failed answer never loses
   the question.
2. The answer SHALL stream as it is written, and the GM SHALL be able to stop it.
3. An answer SHALL be saved only when it ends cleanly. One cut off by the output ceiling,
   stopped, empty, or failed SHALL NOT be saved, and the panel SHALL say so.
4. Each turn SHALL be grounded in the world's record retrieved at the caller's role (always
   GM, so Hidden truth is included), the Adventure's outline, the section on screen when the
   GM sent the message, and the most recent messages that fit a fixed window.
5. One answer SHALL stream at a time per Adventure. A second send while one streams SHALL be
   refused with 409, and the panel SHALL not offer it.

### Requirement 3 — Put in section

**User Story:** As a GM, when the Loremaster says something I want to keep, I want it in the
adventure without copying and pasting, and I want to be able to take it back.

#### Acceptance Criteria

1. Every saved answer SHALL offer **Put in section**: choose a section, then **Replace** or
   **Append**.
2. Putting an answer in a section SHALL keep the section's previous body as a revision, so
   **Undo** restores it, exactly as after a draft.
3. It SHALL be refused with 409 if the section changed since the GM last loaded it, and SHALL
   NOT overwrite a newer edit.
4. It SHALL respect the section's size bound; an answer that would break it SHALL be refused
   with the size found and the bound.
5. It SHALL make no AI call.

### Requirement 4 — Bounded and metered

**User Story:** As the person paying for the AI, I want conversation spend on the Prep budget,
visible, and impossible to run away.

#### Acceptance Criteria

1. Every turn SHALL be recorded as its own operation, `AdventureConversation`, in the Prep
   monthly pool, and SHALL be refused when that pool is spent or the global pause is on.
2. A turn SHALL carry the same output ceiling as a draft.
3. A message SHALL be at most 4,000 characters. A Conversation SHALL hold at most 400
   messages; at the cap the panel SHALL ask the GM to clear it.
4. The history sent with a turn SHALL be bounded by characters, newest first, whole messages
   only.

### Requirement 5 — Threads to use

**User Story:** As a GM, I want to build the next adventure around the secrets my world is
ready to give up.

#### Acceptance Criteria

1. The Overview section SHALL show up to five threads to use, taken from the convergence
   gauge, each with its anchor's name, what it says, and the gauge's reasons.
2. **Use in this adventure** SHALL add one line naming the thread to the instruction box. It
   SHALL NOT write to a section.
3. A plain Draft of the Overview SHALL include the top threads, bounded, in its prompt.
4. With no hidden material in the world, the Overview SHALL say so in one line.
5. Threads to use SHALL make no AI call of their own.

### Requirement 6 — Never canon

#### Acceptance Criteria

1. A Conversation SHALL never be extracted, SHALL never be read by Ask's retriever, and SHALL
   never appear on a public page or a shelf link.
2. The world export's `prep.json` SHALL include each Adventure's Conversation.

### Requirement 7 — The Bibliotaph path stays open

#### Acceptance Criteria

1. A Prep print SHALL be created only through `LibraryService`'s request-upload and confirm,
   which is where the Library/Bibliotaph integration's outbox hooks in.
2. A print SHALL keep its `AdventureId`.
3. A test SHALL pin both, so a later shortcut to filing can't bypass that path.

### Requirement 8 — The vocabulary is written down

#### Acceptance Criteria

1. `domain-model.md` SHALL carry a dated amendment recording that `Conversation` is built,
   with `AdventureId`, GM-only by current role, never extracted.

## Staging

| Version | Ships |
| --- | --- |
| **v3.1** | Requirements 1 to 8. |
| **Deferred** | One-click PDF: the booklet rendered to PDF in production with headless Chromium. If it is built, it is a separate scale-to-zero container (`ca-nornis-print`) with its own queue, rendering `AdventurePrintModel`'s HTML and filing through `LibraryService`. Chromium in the existing worker is rejected, because every extraction would pay for its memory. |
| **Later** (unchanged from 3.0) | Telling extraction which Adventure a session ran; printing only the props; bring-your-own AI keys. |

## Out of scope

- Several conversations per Adventure, or a conversation not tied to one.
- A conversation that writes to a section without the GM choosing **Put in section**.
- Conversations for Players. Ask remains the player-facing Loremaster.
