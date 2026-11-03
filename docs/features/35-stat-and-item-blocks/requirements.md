# Requirements Document

## Introduction

Prep (feature 29) has one convention for game numbers: a fenced block whose info string is
`statblock`, holding free "Label: value" lines. It is system-agnostic and never computed. The
drafting and conversation prompts know it, the booklet prints it as a framed table (feature
34), and the extraction prompt says its lines "may become facts" on the Artifact it describes.

On 2026-11-03 Dave asked how Prep could include stat blocks and equipment blocks. Today the
convention cannot say:

- **whose** block it is. There is no name line, so the booklet can't head it and an export
  can't tell the extractor which Artifact it belongs to;
- **how it is grouped**. Traits, actions and reactions run together as one list;
- **what an item is**. There is no convention for a piece of equipment: a magic cloak, a
  ship's manifest entry, a cursed key.

On export, each "Label: value" line can become its own fact, so one creature can put twenty
proposals ("HP: 27", "Speed: 30 ft.") in Review.

Dave chose, on the decision card "How should Prep store stat and equipment blocks?", a
**richer text grammar, parsed**: Markdown stays the source of truth, with no new tables and no
migration. The alternative, structured StatBlock and Item records with a form editor, was
declined. The design also exports each block as **one fact** on the Artifact it names; Dave
was told this and did not object.

Ships as Nornis 3.6.

## Glossary

- **Stat block**: a `statblock` fence. It describes a creature or person in game terms.
- **Item block**: an `item` fence. It describes one piece of equipment.
- **Game block**: either of the two.
- **Name line**: a game block's first line when it starts with `# `.
- **Subtitle**: an optional line straight after the name line, written `*like this*`.
- **Group**: a run of entries under a `## Heading` line inside a stat block, such as Actions.
- **Entry**: a "Label: value" line. A line without a colon is a plain text line.
- **Block warning**: a short note the Adventure page shows under a section whose game block
  can't be filed or printed as written.

## Requirements

### Requirement 1 — The grammar

**User Story:** As a GM, I want to name a stat block, group its lines and describe equipment
in the same plain text I already write.

#### Acceptance Criteria

1. A game block's first non-empty line MAY be a name line, `# Name`.
2. The line straight after the name line MAY be a subtitle, `*text*`: kind, size, alignment,
   rarity, or whatever the game system says there.
3. Inside a stat block, a `## Heading` line SHALL start a group. Entries before the first
   group belong to no group.
4. An entry SHALL be a line with a colon after at least one character. The label is the text
   before the first colon and the value the text after it.
5. A line that is not a name, subtitle, group heading or entry SHALL be a plain text line.
6. Inside an item block, a line of `---` SHALL end the entries. Every line after it is the
   item's description.
7. No label SHALL be required, assumed or computed. The grammar holds for any game system.
8. A block written before this feature SHALL keep its meaning:
   - a block without a name line has no name;
   - a first entry labelled `Name` SHALL be read as the name line.
9. The grammar SHALL be defined in one place in Application, which every reader uses: the
   booklet, the block warnings and the export.

### Requirement 2 — Writing them

**User Story:** As a GM, I want the editor and the Loremaster to write game blocks the same
way.

#### Acceptance Criteria

1. The editor's Stat block button SHALL insert a stat block with a name line, a subtitle and
   a group, in the new grammar.
2. A new Item button SHALL insert an item block with a name line, a subtitle, two entries and
   a description.
3. The drafting prompt and the conversation prompt SHALL describe both blocks with the same
   text, defined once, including one worked example.
4. Both prompts SHALL tell the Loremaster:
   - to use the world's game system's usual labels if it has one, and plain labels otherwise;
   - to reuse a stat block or item the world record already holds instead of inventing new
     numbers;
   - not to copy a published book's stat block wholesale, but to name the book and page when
     one applies.

### Requirement 3 — Block warnings

**User Story:** As a GM, I want to know when a block won't file or print the way I meant,
without Nornis rewriting what I wrote.

#### Acceptance Criteria

1. Each section SHALL come with its block warnings, worked out from its saved text on every
   read. A draft, a Put in section, a save and an import therefore all get them.
2. A game block SHALL be warned about when:
   - it has no name, because Export to Nornis can't file it on anyone;
   - it has no entries and no text;
   - its export line would be cut (Requirement 5.4).
3. A warning SHALL name the block by its name, or by its position ("the second stat block").
4. Nornis SHALL NOT change the text to clear a warning.
5. The Adventure page SHALL show a section's warnings under its editor, and none when it has
   none.

### Requirement 4 — The booklet

**User Story:** As a GM, I want stat blocks and items to look like the ones in a published
adventure, in any system.

#### Acceptance Criteria

1. A stat block SHALL print its name as a heading bar, its subtitle in italics, then its
   entries, with each group under a small-caps rule.
2. Three or more consecutive entries with short labels and short values (STR 14, DEX 12, and
   so on) SHALL print as one row of cells, label above value.
3. An item block SHALL print as a card with its name, its subtitle in italics, its entries and
   then its description. The card SHALL NOT look like a prop.
4. A game block SHALL stay whole in one column when it fits. A longer one flows, as now.
5. A block without a name SHALL print as it does today.

### Requirement 5 — Export to Nornis

**User Story:** As a GM, I want an exported creature or item to arrive as one fact on the
right Artifact, not twenty.

#### Acceptance Criteria

1. The export body SHALL replace each named game block with one line:
   - `Stat block for "Captain Voss" (Shadowdark): AC 15; HP 27; Traits: Sea legs: …`
   - `Item "Cloak of the Tidecaller" (Shadowdark): Weight 1 lb; Value 400 gp. While worn, …`

   The game system in brackets is the Adventure's resolved game system. It is left out when
   there is none.
2. The extraction prompt SHALL file such a line as one fact on the named Artifact, a Character
   or creature for a stat block and an Item for an item block, with predicate `stat block` or
   `item details`.
3. An unnamed game block SHALL be exported as its lines, as today.
4. An export line SHALL be at most the fact value limit (2,000 characters). A longer one is cut
   at a line boundary and ends with "…". The block warning says so before the GM exports.
5. Drafting SHALL see these facts in the world record like any other fact (Requirement 2.4).

### Requirement 6 — Ship

1. Nornis 3.6: the change log, the Features page and the README. Row 35 in the features
   README.
