# Requirements Document

## Introduction

A Prep adventure has a title and a pitch, and nothing else a library would catalogue it by.
Bibliotaph, where a printed booklet ends up (feature 31), describes every book with the same
handful of fields: title, game system, edition, type, levels, publisher, authors and year. When
it catalogues a Nornis print today, it has only the PDF to go on, so it guesses ("Suggested")
and leaves the rest unset.

Nornis 3.4 lets the GM give an adventure those details, prints them in the booklet, and hands
them to Bibliotaph exactly, so a filed booklet arrives in the library already described. Dave
chose this on 2026-11-02 ("Booklet and provider contract"), over printing them in the booklet
only.

The Bibliotaph half is one small change on Bibliotaph's side, made in Dave's Bibliotaph project:
read the new optional `book` block when cataloguing a file from the Nornis storage provider.
Nornis ships first and is complete without it. Until Bibliotaph reads the block, the details
reach Bibliotaph only through the booklet's own pages, where its suggester already looks.

## Glossary

- **Details**: an Adventure's bibliographic fields: game system, edition, type, levels,
  publisher, authors and year. The title is the Adventure's own and is not repeated here.
- **Print details**: the details as they stood when a booklet was filed to the Library, kept
  with that Library document and never changed afterwards.
- **Book block**: the optional `book` object on a file in the Nornis storage provider's
  listing, which carries a print's details to Bibliotaph.

## Requirements

### Requirement 1 — An adventure has details

**User Story:** As a GM, I want to record who wrote an adventure, for what system and levels,
and when, as I would for any book on my shelf.

#### Acceptance Criteria

1. An Adventure SHALL have game system, edition, type, levels, publisher, authors (a list of
   names) and year, each optional.
2. An unset game system SHALL mean "the world's game system". The page SHALL show the world's
   value, marked as coming from the world, and a print SHALL use it.
3. An unset type SHALL mean "Adventure". The page SHALL show Adventure, marked as the default,
   and a print SHALL use it.
4. Every other unset field SHALL mean "not set". It SHALL NOT be printed or sent.
5. Each field SHALL be bounded (design.md, Bounds), and a value outside its bound SHALL be
   refused with 400 and nothing saved.
6. Only a caller whose current role is GM SHALL read or change the details, as for the rest
   of the Adventure.

### Requirement 2 — Editing the details

**User Story:** As a GM, I want to fill the details in one at a time, the way Bibliotaph's
book page works.

#### Acceptance Criteria

1. The Adventure page SHALL show a **Details** card listing the seven fields, each with its
   value or "Not set", and an edit button that edits that field alone.
2. Saving one field SHALL leave the other six as they were.
3. Clearing a field SHALL unset it. Clearing game system or type SHALL return it to its
   default (Requirement 1.2 and 1.3).

### Requirement 3 — The booklet prints them

**User Story:** As a GM, I want the printed booklet to look like a published adventure, with
its credits on it.

#### Acceptance Criteria

1. The booklet's cover SHALL show, under the title, the game system and edition, the levels,
   and the authors, each only when set.
2. The booklet SHALL end its front matter with a credits line: type, publisher and year, each
   only when set.
3. The print page's document title SHALL be the Adventure's title alone, so a PDF saved from
   it is titled after the adventure.

### Requirement 4 — A print keeps its details

**User Story:** As a GM, I want a booklet I filed last month to stay described as it was
printed, even after I change the adventure.

#### Acceptance Criteria

1. Filing a print to the Library SHALL store the print details on the new Library document:
   the details with game system and type already resolved (Requirement 1.2, 1.3).
2. Print details SHALL never change after filing. Changing the Adventure's details SHALL NOT
   change any earlier print's.
3. A Library document that is not a print SHALL have no print details.

### Requirement 5 — Bibliotaph receives them exactly

**User Story:** As a GM, I want a filed booklet to arrive in Bibliotaph already described,
not guessed at.

#### Acceptance Criteria

1. The Nornis storage provider's file listing SHALL carry a `book` block on each file that is
   a print: the Adventure's title as it was printed, and the print details.
2. A file that is not a print SHALL carry no `book` block. Everything else in the listing
   SHALL be unchanged, so a Bibliotaph that doesn't read the block sees no difference.
3. A field that is unset SHALL be sent as null. The contract SHALL say that null means "Nornis
   has no value", so Bibliotaph may keep suggesting that field.
4. The contract document SHALL be amended before the code, as feature 31 requires.

### Requirement 6 — Import Markdown reads them

**User Story:** As a GM with an adventure already in Markdown, I want its details to come in
with it.

#### Acceptance Criteria

1. Import Markdown SHALL read an optional front matter block at the very start of the first
   file: a `---` line, `key: value` lines, and a closing `---` line.
2. The keys SHALL be `system` (or `game_system`), `edition`, `type`, `levels`, `publisher`,
   `authors` (comma-separated, or a `- name` list) and `year`. Other keys SHALL be ignored.
3. The block SHALL NOT become part of any section's body.
4. A value that breaks a bound SHALL be left unset, not refused, so an import never fails over
   its front matter. The import result SHALL say which keys were left out.
5. A file with no front matter SHALL import exactly as it does today.

### Requirement 7 — The details travel with the world

1. The world export's `prep.json` SHALL include each Adventure's details, and each print's
   print details where the Library documents are exported.
2. Deleting an Adventure or a world SHALL need no new step: the details live on rows that
   are already deleted.

## Out of scope

- A fixed list of types. Type is free text with suggestions until Bibliotaph's own list is
  confirmed (design.md, Open questions).
- Sending details for Library uploads that are not prints. Bibliotaph keeps suggesting those.
- Using the details in AI drafting. The game system already reaches the drafting prompt from
  the world. Using the Adventure's own is a small follow-up if Dave wants it.

## Staging

| Phase | What ships | Migration |
| ----- | ---------- | --------- |
| A | This spec, the domain-model amendment, the contract amendment and the Bibliotaph note | No |
| B | Storage for both column sets, the details route, and the Details card | `AddAdventureDetails`, additive |
| C | Booklet, print snapshot, provider `book` block, Import Markdown front matter, export, and the Nornis 3.4 version bump | No |

Phase B's migration must be generated after feature 33's `AddUserSignIns` (PR #82), so the
model snapshot stays linear. It is applied to production by hand before B merges.
