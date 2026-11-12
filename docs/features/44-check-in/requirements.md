# Requirements Document

## Introduction

Feature 43 (Nornis 3.21) cut the volume at extraction time: every proposal carries a
significance, the incidental ones are dropped unseen, and a Storyline has to be an arc the table
is pursuing. What it could not do is tell the model what *this* table cares about. The prompt
still knows nothing about the world's premise, nothing about the decisions the GM has made in the
review queue, and nothing about which of the storylines already on the books are being played
and which are character colour that extraction once mistook for an arc.

Dave's own proposal (2026-11-08, thread "Reduce noise in the knowledge base"): every few
sessions the AI puts a short set of questions to him about what it is tracking, and his answers
steer it. "Just a quick course-correction opportunity. It can ask about things it thinks might or
might not be material." On 2026-11-12 he asked for a way to kick that off by hand, and settled
the open choices: ship the questions and the standing instructions together; ask about storylines,
one-source entries and texture patterns; surface the check-in every three dated sessions; make the
number of questions configurable; and keep a larger list somewhere he can work through "if I'm
feeling particularly ambitious some day".

The canonical example stays the witch's warning. A witch warned Dave's character that a player
character, Koves, an undead witch, and her son were unnatural. Extraction opened a Storyline,
"Koves's nature", which has sat Active ever since. The check-in should ask: *is Koves's nature
something the table is pursuing, or background on Koves?* Answering *Background* records the
warning on Koves and retires the storyline, and the GM can add, in one line, the general rule
that a player character's development is recorded on the character and never as a storyline.
From then on extraction reads that line on every note.

Ships as Nornis 3.42. One migration (`AddCheckIn`), which Dave applies before the deploy.

## Glossary

- **Check-in**: one round of questions Nornis puts to the GM about what it is tracking. A
  check-in holds every question the evidence produced; the first few are *asked* (shown on
  Home) and the rest wait on the Check-in page.
- **Question**: one pattern-level question about a subject, with a fixed set of answers. Never
  about a single fact. Three kinds: a **Storyline** question (is the table pursuing this arc), an
  **Entry** question (is this one-source entry worth keeping) and a **Pattern** question (should
  Nornis keep recording this kind of detail).
- **Answer**: the GM's choice. It does two things: an immediate action on the record, filed
  through the normal apply path with provenance, and optionally a standing instruction.
- **Standing instruction**: one line the GM has told Nornis about what to track or how to record
  it, kept per world. The set of them is the world's **editorial brief**, which the extraction
  prompt reads on every source.
- **Dated session**: a processed source with a session date. The check-in's cadence counts
  these, not calendar days: the world advances by play.
- **Due**: a check-in is due when the configured number of dated sessions has landed since the
  last one (or since the world began). Due means the Home card offers it; nothing is generated
  or spent until the GM asks.

## Requirements

### Requirement 1 — The GM can ask for a check-in at any time, and is offered one on cadence

**User Story:** As a GM, I want to kick off the questions whenever I like, and to be reminded
every few sessions without having to remember.

#### Acceptance Criteria

1. A **Check-in** page (`/check-in`, GM group in the sidebar) SHALL carry an **Ask me now**
   button that starts a check-in immediately.
2. Home SHALL show a Check-in card when a check-in is due or when the open check-in still has
   unanswered asked questions. A due card offers Ask me now; an open card shows the asked
   questions in place, each answerable with one click, like the session wrap-up.
3. A check-in SHALL be due when at least `CheckIn:CadenceSessions` (default 3) dated sessions
   have a session date later than the latest dated session the previous check-in saw, or, for a
   world with no check-in yet, when the world has that many dated sessions at all.
4. Nothing SHALL be generated or spent on cadence alone. The AI call happens when the GM asks.
5. Starting a check-in while one is open SHALL close the open one; its unanswered questions are
   kept as history but no longer shown.
6. Everything about the check-in SHALL be GM-only: the page, the card, the endpoints and the
   service refuse any lower role.

### Requirement 2 — Questions come from evidence, and the AI picks the few worth asking

**User Story:** As a GM, I want the questions to be about things Nornis has reason to doubt,
phrased so I can answer at a glance, and few enough to answer in a minute.

#### Acceptance Criteria

1. Candidates SHALL be gathered deterministically from the record, with no AI involved and
   bounded per kind:
   - Active storylines that have gone quiet (the continuity signal's threshold) or that were
     opened from a single source, at most 15, quietest and newest first;
   - entries of other types with exactly one source behind them and their facts, whose source
     is older than the last `CadenceSessions` dated sessions, at most 10, fewest facts and
     oldest first;
   - predicates that recur across the world (at least 8 facts across at least 3 entries, the
     structural predicates `open question`, `goal` and `development` excluded), at most 5, most
     facts first.
2. A subject answered in either of the two most recent check-ins SHALL NOT be a candidate
   again, so a storyline the GM said is being pursued is not asked about next time.
3. One AI call SHALL choose which candidates to ask about, at most the world's configured
   question count, and write for each a one-sentence question, a one-line *why* grounded in the
   evidence given, an optional suggested standing instruction, and, for a Storyline candidate
   that reads as background on a particular Character or Faction among its related entries,
   that entry as the suggested place to record it. The call is given the world's existing
   standing instructions so it does not ask what is already settled, and the Storyline test
   from feature 43 so it applies the same definition the extractor uses.
4. The candidates the AI did not pick SHALL be kept on the same check-in with a plain
   templated question, so the Check-in page lists everything Nornis could have asked, asked
   ones first.
5. The number of questions asked SHALL be a per-world setting, **Questions per check-in** on
   the Processing tab of World settings, 1 to 10, default 5.
6. The AI call SHALL be metered as its own operation type (`CheckIn`), on the Ask model, against
   the world's daily budget, behind the budget guard and the pause gate like the retrospective.
   A refused or failed call starts no check-in and tells the GM why.
7. A world with no candidates SHALL start a check-in with no questions rather than call the AI,
   and the card says Nornis had nothing to ask.

### Requirement 3 — An answer acts on the record and can leave a standing instruction

**User Story:** As a GM, I want one click to do the obvious thing, and a line of text to make it
the rule from now on.

#### Acceptance Criteria

1. A Storyline question SHALL offer **Pursuing**, **Background**, **Done** and **Skip**:
   - Pursuing changes nothing on the record;
   - Background with an entry chosen (the AI's suggestion preselected, any entry of the world
     selectable) merges the storyline into that entry through the existing merge, so its facts
     move onto the character or faction and the storyline is archived; Background with no entry
     sets the storyline Dormant;
   - Done sets the storyline Resolved, which settles its facts as the wrap-up does;
   - Skip records nothing.
2. An Entry question SHALL offer **Keep**, **Archive** and **Skip**. Archive sets the entry's
   status Archived, which the entry page can undo; nothing is deleted.
3. A Pattern question SHALL offer **Keep**, **Stop** and **Skip**. Stop writes the standing
   instruction that this kind of detail is not recorded; existing facts stand.
4. Every status change SHALL be filed as an accepted `UpdateArtifact` proposal on a synthetic
   source and batch of kind `CheckIn`, through `SyntheticBatchWriter`, so it has the provenance
   and the undo of any reviewed change. The merge goes through `IArtifactMergeService` for the
   same reason.
5. Any answer except Skip MAY carry a standing instruction, prefilled from the AI's suggestion
   and editable before it is saved. It is written only if the GM leaves it non-empty.
6. A question SHALL be answerable once. Answering an answered question SHALL fail with 409 and
   change nothing. An answer whose action fails SHALL leave the question unanswered.
7. The question SHALL record what its answer did: the batch it filed and the instruction it
   wrote, so the Check-in page can say "Set Dormant" or "Merged into Koves" under it.

### Requirement 4 — The editorial brief is the GM's, and extraction reads it

**User Story:** As a GM, I want to see and edit what I have told Nornis, and I want every
future note extracted under it.

#### Acceptance Criteria

1. The Check-in page SHALL list the world's standing instructions with where each came from
   (a check-in answer, with the question, or typed by hand) and a Remove on each, and SHALL let
   the GM add one by typing it.
2. An instruction SHALL be at most 300 characters, and a world SHALL hold at most 40. Adding a
   41st SHALL fail with 409 and a message to remove one first; the cap is `CheckIn:MaxRules`.
3. The extraction system prompt SHALL carry a **Standing Instructions** section when the world
   has any, rendered by one function the check-in prompt reuses, saying that these come from the
   GM and take precedence over the general guidance: a detail an instruction says is not
   tracked is Incidental, and a thing an instruction says how to record is recorded that way.
4. The section SHALL be absent when the world has no instructions, so a world that never checks
   in sees the prompt it sees today, and the eval runner's requests are unchanged.
5. The brief SHALL NOT be read by Ask, the retrospective or the continuity audit in this feature.

### Requirement 5 — Tested where it matters

#### Acceptance Criteria

1. Candidate gathering SHALL be a pure function over a hand-built data set, with tests for each
   kind's rule, the caps, the ordering, the exclusion of recently answered subjects and the
   structural-predicate exclusion.
2. The service SHALL have tests for the role gate, the budget gate, due-ness at the boundary,
   closing the previous check-in, persisting picked and unpicked questions, refusing an unknown
   candidate id or an out-of-list record-on entry from the model, every answer's action and its
   provenance, the once-only rule, the rule cap, and the brief landing in the extraction request.
3. The AI client SHALL have schema and parse tests, and its schema SHALL join
   `StrictSchemaDialectTests`.
4. The controller SHALL have round-trip tests for start, answer and the rules, and the 403 for a
   player.
5. The prompt builder SHALL have tests for the section present with the instructions and absent
   without.
