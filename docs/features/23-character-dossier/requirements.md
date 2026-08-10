# Requirements Document

## Introduction

A world member's `Character` has existed since feature 3 and can already be linked to the
AI-extracted `Character` artifact describing the same fictional person. The link works in one
direction only: `ArtifactDetail.PlayedBy` tells a reader of *Tavrin the artifact* which members
play him. Nothing goes the other way. A character is a row on the profile page — a name, a
description, a link — and there is nowhere to go and nothing to read.

This feature makes a character somewhere to go: **`/characters/{id}`, showing what the record
knows about this person, beside what their player wrote down themselves.**

The motivating table is a paper one. A group playing without a VTT has no digital sheet
anywhere, so the ordinary answer — "your character sheet lives in D&D Beyond" — does not apply.
What they lose is not their stats, which are on the paper in front of them, but everything the
paper is bad at: what the party looted three sessions ago, which faction owes them a favour,
what the sheet looked like before the level-up, and where the sheet is when it is not on the
table.

**The governing constraint is that Nornis stores and displays, and never computes.** No
arithmetic, no validation against a rules system, no field whose unit Nornis understands. A
number here is an opaque string, exactly as `ArtifactFact.Value` is today. This is what keeps
the feature on the correct side of the standing "no RPG rules engine" and "no multi-system
rules automation" non-goals in `product-vision.md`: the product declines to know what AC means,
in every system, permanently. A field Nornis interprets is a system Nornis must support.

Delivered in phases:

- **Phase A — The record.** The page, and what the knowledge graph already knows about this
  character, grouped for someone reading about a person. No schema change.
- **Phase B — The written sheet.** A free-form, uninterpreted block the player maintains, and
  the one decision about who may read it.
- **Phase C — Sheet snapshots.** Photographs of the paper sheet, dated, kept as ordinary
  sources so the existing ingest and visibility rules apply unchanged.
- **Phase D — What the record has that your sheet does not.** Deferred and genuinely optional;
  specified here so Phase B does not foreclose it.

## Requirements

### Requirement 1: A Character Is Somewhere To Go

**User Story:** As a world member, I want to open a character and read about them, so the
character is a place in the app rather than a label on my profile.

#### Acceptance Criteria

1. THE system SHALL provide a character detail view addressed by character id within a world.
2. THE view SHALL be readable by every member of the world, including Observers.
3. THE view SHALL show the character's name, description, owning member, and the campaigns it
   belongs to.
4. WHERE the character is linked to an artifact, THE view SHALL show the record derived from
   that artifact (Requirement 2).
5. WHERE the character is not linked to an artifact, THE view SHALL render without the record
   section and SHALL NOT present the absence as an error or a failure state.
6. THE existing character management on the profile page SHALL remain the place characters are
   created, renamed, linked, and deleted. This view SHALL NOT become a second one.

### Requirement 2: What The Record Knows

**User Story:** As a player, I want to see what the sources say my character has, knows, and is
tangled up in, so I do not have to remember which session note it was in.

#### Acceptance Criteria

1. THE record section SHALL derive entirely from the linked artifact's existing facts,
   relationships, and connected artifacts. It SHALL NOT introduce a second store of character
   state.
2. THE connected artifacts SHALL be grouped by artifact type for reading — items, places,
   factions, storylines, and other people — rather than presented as one undifferentiated list.
3. EVERY element shown SHALL be filtered to the *reading* member's visibility, using the same
   rule that governs the artifact detail view. A player SHALL NOT see a `GMOnly` fact about a
   character by opening it through this view rather than through `/artifacts/{id}`.
4. THE visibility applied SHALL be the reader's own, and SHALL NOT be widened because the
   reader owns the character.
5. EACH element SHALL remain traceable to its supporting sources, as it is on the artifact
   detail view.
6. THE grouping SHALL be bounded: a character connected to an unbounded number of artifacts
   SHALL render a bounded page with the remainder reachable through the artifact detail view.

### Requirement 3: The Written Sheet

**User Story:** As a player at a paper table, I want somewhere to type my character's current
state, so it is legible, backed up, and readable on my phone between sessions.

#### Acceptance Criteria

1. THE system SHALL store one free-form text block per character, authored by a member.
2. THE block SHALL be uninterpreted: THE system SHALL NOT parse it, validate it against any
   game system, compute any value from it, or render any field-level structure of its own.
3. THE block SHALL be length-bounded, and THE bound SHALL be enforced server-side.
4. THE block SHALL be editable by the character's owning member and by GMs, using the same
   ownership rule that already governs renaming and deleting a character.
5. BY DEFAULT the block SHALL be readable only by its owning member and by GMs.
6. THE owning member SHALL be able to share the block with the whole world, and to unshare it.
7. WHERE the block has never been written, THE view SHALL offer to start one rather than render
   an empty panel.
8. THE block SHALL NOT be indexed for, or supplied to, the Loremaster. It carries no provenance
   and must not be quotable as though it did.

### Requirement 4: Sheet Snapshots

**User Story:** As a player whose sheet is on paper, I want to photograph it into the world, so
I have it when the paper is not in front of me and can see what it used to say.

#### Acceptance Criteria

1. THE system SHALL let a member attach an existing source to a character as a dated snapshot
   of that character's sheet.
2. THE snapshot SHALL carry the date the sheet is a record *of*, which MAY differ from when the
   source was uploaded.
3. THE snapshots SHALL be listed newest first, and the list SHALL be bounded.
4. A snapshot's readability SHALL be the underlying source's own visibility, unchanged. THE
   feature SHALL NOT introduce a second visibility rule for the same bytes.
5. Attaching and detaching a snapshot SHALL follow the same ownership rule as Requirement 3.4.
6. Detaching a snapshot SHALL NOT delete the source. The source ledger remains the record.
7. WHERE a source attached as a snapshot is deleted, THE attachment SHALL disappear with it
   rather than leave a dangling row.

### Requirement 5: Extraction Stays Narrative (constrains Phases A–C)

**User Story:** As a GM, I want a photographed sheet to enrich the world without flooding it
with numbers, so the codex stays a record of the story.

#### Acceptance Criteria

1. WHERE a sheet snapshot is processed by ordinary extraction, THE resulting proposals SHALL be
   subject to the existing review workflow with no special case.
2. THE feature SHALL NOT add an extraction path, prompt, or schema of its own in Phases A–C.
3. THE feature SHALL NOT auto-accept any proposal arising from a snapshot.

### Requirement 6: Unreconciled Items (Phase D, deferred)

**User Story:** As a player, I want to notice when the record says I picked something up that
my sheet never recorded, so drift between the story and my sheet is visible.

#### Acceptance Criteria

1. THE system SHALL identify artifacts the record connects to this character which are not
   mentioned in the written sheet.
2. THE comparison SHALL be a plain text presence test over artifact names. It SHALL NOT parse
   the sheet's structure, and SHALL NOT infer quantity, ownership, or state.
3. THE result SHALL be presented as an observation, never as a correction, and SHALL NOT modify
   the sheet.
4. THE comparison SHALL obey Requirement 2.3: it SHALL NOT reveal an artifact the reader may
   not see.

## Out of Scope (this feature)

- **Any typed mechanical field** — hit points, armour class, ability scores, spell slots,
  currency, encumbrance, level. The written sheet holds these as text or not at all. This is
  the feature's central constraint, not an omission.
- **Any arithmetic or validation.** No totals, no maxima, no "you have 3 remaining".
- **System-aware templates.** A 5e sheet layout is a rules dependency wearing a UI costume.
- **A character-scoped Loremaster** ("ask what my character knows"). It is the natural next
  feature and this one is its prerequisite, but it needs a per-character knowledge boundary
  that does not exist yet — feature 17's deferred *per-player / per-character reveal*.
- **Character-scoped visibility of world knowledge.** The party remains one audience, as in
  features 17, 21 and 22.
- **OCR of a sheet into structured fields.** Requirement 5 exists to forbid exactly this.
- **NPC or GM-side dossiers.** An NPC is an artifact and already has a page.
- **Uploading from this page.** Snapshots attach existing sources; capture stays in the one
  place that already owns it.
