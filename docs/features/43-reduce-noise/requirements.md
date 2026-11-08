# Requirements Document

## Introduction

On 2026-11-08 Dave described a persistent sense that the knowledge base Nornis builds "gets hung
up on unimportant details, calls out storylines which aren't really something the table is
driving or really needing to track", and that he could not see how to cue the AI in without
putting in more review work than he already does. His own example: a witch pulled his character
aside and warned him that a player character, Koves, an undead witch, and her son were unnatural.
Extraction created a Storyline about Koves's nature, which now shows as an open, ongoing arc. To
Dave it is a fact about Koves: the player will keep exploring the character, nobody will take
notes towards resolving a storyline about her.

Reading the code against that complaint (thread "Reduce noise in the knowledge base"):

- The extraction prompt is tuned for recall. It allows fifty proposals a source, warns off
  "incidental detail" in one sentence, and pushes storylines in three: they are "first-class",
  the model should "say so" whenever a source "advances, opens, or closes an arc", and "an Event
  that matters almost always belongs to an arc". A model told that an unlinked Event is a mistake
  creates a Storyline to link it to.
- The model never sees what the table cares about. It is shown the artifacts the source names
  and the fifty most recently updated, but not a storyline's status, so a Dormant or Resolved
  storyline reads exactly like an Active one and can be re-animated or duplicated.
- Review captures almost no signal, and the queue's friction points the wrong way: Reject
  confirms, Accept all does not, and confidence is shown but drives nothing.

Symbaroum's own numbers (dump of 2026-11-08): 101 sources became 497 artifacts, 942 facts and
512 relationships. 1,962 proposals were accepted and 31 rejected. 65 Storylines were created
and none rejected; 56 of them are now Resolved, closed by hand on the artifact pages, with no
retrospective or wrap-up batch in the history. Confidence did not separate the two piles:
25 of the 31 rejections carried confidence 0.9 or more.

Dave agreed a direction on 2026-11-08: fix the volume at extraction time first, without adding a
single review action, and drop what the model itself calls incidental rather than filing it in
a hidden tier. The check-in and editorial brief he proposed (the AI asks a few steering questions
every few sessions; the answers become standing rules the prompt reads) is the next feature, not
this one.

Ships as Nornis 3.21. No migration.

## Glossary

- **Significance**: the model's judgment of what the table will do with a proposal, one of
  Core, Supporting or Incidental. Not confidence, which says how sure the model is that the
  source establishes it. A proposal can be certain and incidental.
- **Incidental**: mentioned once with no consequence. Scene dressing, passers-by, prices and
  provisions, routine travel, rules talk, a passing mood. Incidental proposals are discarded
  before anything is filed; the source keeps the text.
- **Storyline**: an arc the table is pursuing: someone, the party or a force acting on the party,
  intends something, and there is a conceivable moment at which it resolves. Both halves are
  required. A character's development, nature or reputation, the world reacting to who a
  character is, mood, foreshadowing and one-off warnings are facts and relationships on the
  Character or Faction concerned, never storylines.
- **Retired storyline**: a Storyline whose status is Dormant or Resolved. The GM decided the
  table is done with it.
- **Triage**: the server-side step that removes Incidental proposals from a parsed extraction
  before any ReviewProposal is written.

## Requirements

### Requirement 1 — Every proposal says how much it matters

**User Story:** As a GM, I want extraction to judge what matters to my table before it asks me
to, so the review queue holds what the record would be poorer without.

#### Acceptance Criteria

1. The extraction schema SHALL require a `significance` of `Core`, `Supporting` or `Incidental` on
   every proposal, beside `confidence` and `quote` at the proposal level, never inside
   `proposedValue`, so the payload stays within the sixteen fields the Anthropic adapter fits.
2. The prompt SHALL define the three values in terms of what the table will do with the
   proposal, not how interesting it is, and SHALL say that Incidental proposals are discarded
   before anyone sees them, that a source made of texture may produce nothing, and that a
   detail's significance must not be inflated to keep it.
3. The prompt SHALL say that a CreateArtifact an at-least-Supporting fact or relationship
   depends on is itself at least Supporting.
4. A parsed proposal with no `significance`, as a non-strict OpenAI-compatible endpoint might
   send, SHALL be treated as Supporting. A value outside the three SHALL fail the parse like any
   other bad enum, so the retry loop asks again.
5. Significance SHALL NOT be stored on `ReviewProposal` in this feature. Tiering the queue by it
   is deferred with the queue work.

### Requirement 2 — Incidental is dropped before it is filed

**User Story:** As a GM, I want what the model itself calls incidental never to reach my queue.

#### Acceptance Criteria

1. After a response parses and validates, and before any ReviewBatch or ReviewProposal is
   written, triage SHALL remove every Incidental proposal.
2. Triage SHALL keep an Incidental CreateArtifact that a kept proposal references by name
   (`artifactName`, `artifactAName` or `artifactBName`, compared with `ArtifactNameKey`), so a
   kept fact never lands at accept time on an artifact nothing proposed.
3. A response whose proposals are all Incidental SHALL complete as an empty batch, exactly as a
   response with no proposals does today: the source is Processed, nothing waits in review, and
   the call is metered.
4. The extraction log SHALL record how many proposals triage dropped and the names of any
   dropped CreateArtifacts, so a GM asking "where did X go" can be answered.
5. The eval runner (`tests/Nornis.Evals`) SHALL apply the same triage, so its counts match the
   Worker's.
6. Map extraction, whose proposals carry no significance, SHALL be unaffected.

### Requirement 3 — A Storyline is something the table is pursuing

**User Story:** As a GM, I want Nornis to open a storyline only when my table has taken
something up, and to record the rest as facts about the people it concerns.

#### Acceptance Criteria

1. The prompt SHALL define a Storyline as an arc with an actor who intends something and a
   conceivable resolution, both required.
2. The prompt SHALL name what is not a Storyline however much tension it carries: a character's
   development, nature, reputation or inner conflict (a player character's included); the world
   reacting to who a character is; mood, foreshadowing, portents, a one-off warning nothing has
   acted on; and a question nobody at the table has set out to answer. It SHALL say these are
   facts and relationships on the Character or Faction concerned, with the witch's warning as the
   worked example.
3. The prompt SHALL say to prefer advancing an existing Storyline to creating one, that a source
   rarely opens more than one new Storyline, and never to propose more than two.
4. The line "An Event that matters almost always belongs to an arc" SHALL be removed. An Event
   SHALL be linked to a Storyline only when that Storyline is genuinely what it advances; an
   Event with no arc behind it is fine unlinked.
5. An open question SHALL be proposed only when the party is pursuing the answer: they asked it,
   investigated it or decided to find out. A question the text leaves hanging that nobody has
   taken up SHALL be left alone.

### Requirement 4 — Retired storylines stay retired

**User Story:** As a GM, when I have closed a storyline, I want extraction to know, so the next
note neither re-animates it nor opens a twin.

#### Acceptance Criteria

1. The Existing World Artifacts list SHALL show `Status: Dormant` or `Status: Resolved` under a
   retired storyline, as it shows `Part of:`. Active artifacts SHALL show no status line, so the
   common case costs no tokens.
2. The prompt SHALL say never to propose a new Storyline about the same matter as a retired one
   and never to add open questions to it, and that if this source plainly reopens it the right
   proposal is UpdateArtifact setting status Active, with the rationale saying what reopened it.
3. Retired storylines SHALL stay in the context (today only Archived artifacts are excluded), so
   the model can see what to leave alone.

### Requirement 5 — Measured, not asserted

**User Story:** As the person who will read Symbaroum's next session notes, I want to know the
change did what it says.

#### Acceptance Criteria

1. The prompt builder, parser, triage and context tests SHALL cover each rule above (prompt
   text present; the removed line absent; significance parsed, defaulted and refused; an
   Incidental create promoted by a dependent; all-Incidental yields an empty batch; the status
   line rendered for Dormant and omitted for Active).
2. The dump script from the thread (`artifacts/kb-dump/Dump-WorldKb.ps1`, not committed) SHALL be
   the before-and-after measure on Symbaroum: proposals per session note, Storyline creates per
   note, and the share of artifacts with a single source behind them.
3. The eval runner comparison (old prompt against new on a handful of Symbaroum notes) is wanted
   but waits on the provider keys feature 39 still lacks in the cloud container.
