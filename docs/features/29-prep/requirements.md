# Requirements Document

## Introduction

The first session of the Gribbits Detective Agency was written in a chat window. The GM typed
a pitch, something like *"opening one-shot for a new detective agency: four 3rd-level
provisional detectives sent after a missing guest at a Waterdeep spa where two earlier teams
quit for silly reasons; it's really an oblex cult under the baths and everybody dies; comedy
first, 2.5 hours, players know they won't survive"*. Then they went back and forth for an
evening and ended up copying five Markdown files out of the chat log: *Everybody Dies at the
Slurping Spa*, 9,124 words, 211 lines of read-aloud, about 100 table rows, a session clock
with four hard checkpoints, and no stat blocks.

The chat had never seen the world. It didn't know that Gribbits runs the agency from Beggar
Court, that Bickle is a cart that complains about traffic, or that the elder oblex is the
secret everything hangs on. Nornis knows all of that, or will once the GM has played a
session, and it knows which parts the players haven't found out yet. A general chat tool can't
have that grounding. It's the reason to draft adventures inside Nornis.

**Prep** is a GM-only place to write the next adventure with the Loremaster's help, grounded
in the whole record, Hidden truth included, since the GM is the author. The record is an
**Adventure**. The GM can do two things with a finished one:

- **Print** it as a booklet in a classic tabletop-book layout, with read-aloud boxes, props,
  stat blocks and equipment lists. The booklet is filed GM-only, and the GM can choose to
  share it with the players.
- **Export** it into Nornis as GM-authority knowledge. The Adventure becomes a GM-only Source,
  extraction proposes its NPCs, places, factions and secrets, the GM reviews them, and Reveal
  later decides what the players learn.

That closes a loop: prep becomes canon, and canon grounds the next prep.

### The worked example

How the spa would have been written with Prep.

1. **The pitch.** In the world *Gribbits Detective Agency* the GM opens Prep, chooses **New
   adventure**, types the title and the pitch above, and sets the campaign. The Adventure
   opens already laid out with the house skeleton: Overview, Session clock, Chapter 1 with
   five Scene sections, Quick reference.
2. **Threads.** On a new world there are none, and Prep says so. By the third adventure the
   convergence gauge would offer the threads closest to paying off (v3.1). In v3.0 the
   Overview's draft reads the world's canon itself.
3. **The skeleton, filled one section at a time.** The GM drafts the Session clock ("150
   minutes, eight beats, checkpoints at 0:20, 0:55, 1:15 and 1:45"), accepts it and edits
   two times by hand. On Scene 3, *The Maltby Case*, they ask *"draft the read-aloud and the
   likely questions; Gribbits is curt"*, and the draft streams into the section. They revise
   the read-aloud with *"shorter, and Gribbits mispronounces Maltby"* and dislike the result,
   so they press Undo. Nothing is generated for the whole adventure at once.
4. **The booklet.** Print opens a two-column book view: boxed read-aloud, the in-world staff
   notices set as props, the clock as a table, each chapter starting on a new page. The GM
   prints it to PDF and files it to the Library, where it sits GM-only. Later they share it
   with the players, after a warning that it was drafted with Hidden truth.
5. **The export.** The GM exports the Adventure. The review queue fills with Gribbits,
   Bickle, Howel Maltby, Porter, Bryn, Daggit, Rusty, Bargomosh, the Thalasso Natatorium and
   Turgen's Lane. The elder oblex and the cult arrive as Hidden truth. The eight scenes don't
   show up as Events, because they haven't happened yet.

Because the spa already exists as Markdown, v3.0 also lets the GM **import** it, and that
import is the acceptance walk for this feature.

The governing constraints:

- **Prep is the GM's alone.** An Adventure is never visible to a Player or an Observer.
  Only a print can be shared, and only when the GM deliberately chooses to.
- **The Adventure comes first, the chat second.** The Adventure on the page is the work. The
  AI drafts or revises one section on the GM's instruction. There is no button that generates
  an adventure from one line.
- **An Adventure is never canon.** It changes the record only through Export, which is an
  ordinary Source that goes through review. Nothing bypasses the queue.
- **Store and display, never compute.** Stat blocks and equipment lists are text with a
  layout. No rules engine, and no single game system assumed.
- **Prep spend is its own pool.** It has a monthly cap of its own and can't use up the
  world's daily budget that extraction depends on.

Versions follow the app: this feature ships as **Nornis v3.0** (MajorVersion 2 → 3), with
**v3.1** and **Later** as below.

## Requirements

### Requirement 1 — Prep is GM-only

**User Story:** As a GM, I want my prep kept where my players can never stumble on it.

#### Acceptance Criteria

1. The nav SHALL show **Prep** as the first item of the GM group, which is already hidden
   from non-GMs by `GmOnly`.
2. Every Prep endpoint SHALL refuse a Player or an Observer with 403, and SHALL refuse a GM
   viewing as a player (`X-Nornis-View-As`), the way GM endpoints already fail closed.
3. Access SHALL follow the caller's *current* GM role in the world. A GM who is demoted
   loses access to the Adventures they wrote.
4. An Adventure SHALL carry no visibility of its own. It is GM-only by definition.

### Requirement 2 — An Adventure and its sections

**User Story:** As a GM, I want my adventure laid out in sections I can reorder, edit and
draft one at a time.

#### Acceptance Criteria

1. An Adventure SHALL belong to one world, MAY name one campaign of that world, and SHALL
   have a title, a slug and an optional pitch.
2. An Adventure SHALL be an ordered list of sections. Each section has a kind (Overview,
   Session clock, Chapter, Scene, Quick reference, Notes), a title and a Markdown body.
3. A new Adventure SHALL start from the house skeleton: Overview, Session clock, Chapter 1,
   Scenes 1 to 3 (each body pre-filled with `### Goal`, `### Read-aloud`, `### Likely
   questions` and `### End state`), and Quick reference. The skeleton is a template, so the
   GM may delete, add, rename, re-kind and reorder any section.
4. The GM SHALL edit a section's body directly in the existing editor, with the existing
   unsaved-edit backup.
5. Bounds: at most 200 sections per Adventure, 20,000 characters per section and 100,000
   characters across the Adventure, the same limit as a Source body, so any Adventure can be
   exported. A save over a bound SHALL be refused with a message that names the bound.
6. Deleting an Adventure SHALL ask for confirmation. Its prints in the Library and its
   exported Sources SHALL remain, with their link to it cleared.

### Requirement 3 — Markup

**User Story:** As a GM, I want read-aloud, props, questions and stat blocks to look
different in print without learning a markup language.

#### Acceptance Criteria

1. A plain blockquote SHALL be read-aloud.
2. A blockquote whose first line is `[!PROP]` SHALL be an in-world prop (a notice, letter or
   sign), and one whose first line is `[!ASK]` SHALL be a question to the players.
3. A fenced block tagged `statblock` SHALL be a stat block or equipment list: a name line,
   then free `Label: value` lines. It SHALL be displayed as written and never computed or
   validated against any game system.
4. Pipe tables, headings, lists and paragraphs SHALL be ordinary Markdown.
5. The editor SHALL offer buttons that insert each of the four marked elements, and every
   element SHALL survive the editor round-trip unchanged.

### Requirement 4 — Import Markdown

**User Story:** As a GM with an adventure already in files, I want to bring it into Prep
rather than retype it.

#### Acceptance Criteria

1. The GM SHALL be able to create an Adventure from one or more pasted or uploaded Markdown
   files, in the order given, with no AI involved.
2. Import SHALL split on `#` headings. A `#` line that repeats the first file's first `#`
   line (the adventure's title) SHALL start a new Chapter section. Every other `#` section
   SHALL become a Scene when its heading begins "Scene", and a Notes section otherwise.
3. Import SHALL apply the bounds of Requirement 2. An import over them SHALL be refused with
   the size found and the bound.
4. The five Slime Spa files SHALL import into five chapters with every read-aloud line,
   table and checkpoint intact.

### Requirement 5 — Draft and revise a section

**User Story:** As a GM, I want the Loremaster to draft or revise one section at a time on my
instruction, grounded in my world, and I want to undo it.

#### Acceptance Criteria

1. On any section except a Chapter, the GM SHALL be able to **Draft** an empty section, or
   **Revise** a section with an instruction of at most 2,000 characters.
2. The draft SHALL stream into the section as it is written and replace the body only when
   it completes. A cancelled or failed draft SHALL leave the body as it was.
3. The prompt SHALL carry the world's knowledge as the GM may see it, Hidden truth included,
   along with the Adventure's outline (titles and kinds), its Overview and pitch, the
   neighbouring sections and the section itself. It SHALL never include another world's data.
4. Before an AI write replaces a body, the old body SHALL be saved as a revision with the
   instruction that replaced it. **Undo** SHALL restore the latest revision. At most 20
   revisions SHALL be kept per section, oldest dropped first.
5. A draft SHALL be refused with 409 if the section changed since the GM's copy of it was
   loaded, or if another draft is already running on that section.
6. There SHALL be no action that drafts the whole Adventure.

### Requirement 6 — Prep has its own budget

**User Story:** As a GM, I want drafting to have its own monthly cap, so an evening of prep
never stops tonight's notes from being extracted.

#### Acceptance Criteria

1. Every drafting call SHALL be metered under its own operation type, `AdventureDrafting`,
   in the AI usage ledger, and SHALL appear on its own line on the cost page.
2. A world SHALL have a monthly Prep budget. Null SHALL inherit the configured default
   ($10.00). Zero SHALL block drafting. A positive value SHALL cap drafting spend in that
   calendar month (UTC).
3. Drafting spend SHALL NOT count toward the world's daily AI budget, and the daily budget
   SHALL NOT block drafting.
4. The global AI pause SHALL stop drafting, as it stops every other AI call.
5. A refused draft SHALL say which limit refused it and when that limit resets.
6. Each drafting call SHALL carry an output ceiling of 2,500 tokens.

### Requirement 7 — Print

**User Story:** As a GM, I want a finished adventure to look like a proper tabletop book that
I can read at the table.

#### Acceptance Criteria

1. Print SHALL open a print view of the Adventure: two columns, read-aloud in a box, props
   set as in-world documents, questions set apart, stat blocks in a framed block, tables
   ruled, and a new page at each Chapter. The style SHALL be Nornis's own and SHALL NOT copy
   any publisher's trade dress.
2. In v3.0 the GM SHALL print the view to PDF with the browser and then **File to Library**,
   which uploads that PDF through the existing upload path as a Handout.
3. A filed print SHALL start GM-only and SHALL record the Adventure it came from.
4. Making a print visible to players SHALL require a confirmation that says the booklet was
   drafted with Hidden truth and may contain it.
5. Each print SHALL be a new Library document whose title carries the date. An earlier print
   SHALL NOT be replaced.

### Requirement 8 — Export to Nornis

**User Story:** As a GM, I want what my adventure establishes about the world to become part
of the record, so that later prep, the Loremaster and my players' reveals all build on it.

#### Acceptance Criteria

1. Export SHALL create a new Source of type `AdventureExport`, GM-only, in the Adventure's
   world and campaign, whose body is a snapshot of the Adventure's sections in order. The GM
   MAY export the whole Adventure or one Chapter.
2. The Source SHALL reach the extraction queue through the same path every source takes, and
   its proposals SHALL go to the review queue like any other source's.
3. The extraction prompt SHALL say that the source is the GM's own prep. It SHALL extract
   what is true of the world (people, places, factions, items, relationships, secrets as
   Hidden truth) and SHALL NOT propose planned scenes as Events.
4. Each export SHALL be a new Source. Exporting again after edits SHALL rely on the existing
   dedup to bind repeats to existing canon.
5. The Source SHALL record the Adventure it came from. Deleting the Adventure clears that
   link and leaves the Source and everything extracted from it in place.

### Requirement 9 — The world's lifecycle covers Prep

#### Acceptance Criteria

1. Deleting a world SHALL delete its Adventures, sections and revisions.
2. World export SHALL include Adventures and their sections as a category, and world import
   SHALL restore them.
3. The demo-world template SHALL carry no Adventures.

### Requirement 10 — The vocabulary is written down

#### Acceptance Criteria

1. `domain-model.md` SHALL carry a dated amendment adding Adventure (and its sections and
   revisions) and the `AdventureExport` source type, with the same care the doc gives
   Storyline and Reveal.
2. The same amendment SHALL record that `Conversation`, described as optional and deferred,
   is still unbuilt in v3.0 and is the shape v3.1 builds for Prep.
3. `ai-extraction.md` SHALL carry a dated amendment recording that drafting writes to an
   Adventure, never to canon, and that its only path into canon is Export through review.

## Staging

| Version | Ships |
| --- | --- |
| **v3.0** | Requirements 1 to 10. |
| **v3.1** | A persisted side conversation per Adventure (the deferred `Conversation`, with an `AdventureId`) that brainstorms and writes into sections; "threads to use" from the convergence gauge; Print rendered to PDF by the worker (headless Chromium), so filing takes one click; the booklet reaching Bibliotaph by whatever the Library thread settles. |
| **Later** | Telling extraction which Adventure a session ran, so proposals match the plan; printing only the props as a player handout; bring-your-own AI keys. |

## Out of scope

- Generating an adventure from one line.
- Rules computation of any kind, including stat-block validation, encounter math and CR.
- Players seeing or editing an Adventure.
- Copying a published book's layout, fonts or marks.
- Replacing the Library with Bibliotaph (its own thread). Prep files prints through the
  Library's upload path, the one seam that work would change.
