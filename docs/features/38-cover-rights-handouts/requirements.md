# Requirements Document

## Introduction

The booklet (feature 34) opens with a typeset cover: title, system, levels, author and pitch.
It has no art, no rights page, and nothing the table holds in their hands. On 2026-11-03 Dave
asked for the next steps towards a publishable book:

- **Cover art.** "Prompt the Prep AI agent to create a front page graphic which might look like
  a professionally designed cover." He chose **AI art plus upload** on the decision card.
- **A rights page.** "A secondary page which acts something like a published book's copyright
  and rights page, showing that it was created by Nornis Press, give the author name, the date,
  and other metadata. It should also indicate that it was generated using AI."
- **An AI-use checkbox** in Book details that "becomes greyed out once AI is interacted with".
  He refined it: a GM who pastes in their own adventure and only has Prep structure it has not
  used AI, so the box locks only when AI **wrote words or made art**, never for conversation
  alone or for structuring.
- **Handouts.** The Slurping Spa transcript produced printable props, pregens and maps beside
  the booklet. Dave suggested a picker over the Library's maps and handouts, which feature 36
  makes possible without flooding the Library.

Ships as Nornis 3.9, after features 36 and 37. It needs one migration, which Dave applies.

## Glossary

- **Cover art**: one image per Adventure, either generated or uploaded, drawn behind or above
  the cover's type.
- **Art brief**: the text an image is generated from. The Loremaster writes it, and the GM can
  edit it.
- **Cover layout**: one of three arrangements of art and type on the cover.
- **Rights page**: the booklet's second page, carrying credits, the date, the AI statement and
  the notices.
- **AI use**: what the Adventure records about AI's part in it: whether it **wrote text**,
  whether it **made art**, and whether the GM declared AI use themselves.
- **Handout**: a Library document (a Handout or a Map, an image or a PDF) chosen for the
  Adventure's booklet appendix, or a prop box printed on its own page.

## Requirements

### Requirement 1 — AI use

**User Story:** As a GM, I want my book to say truthfully whether AI was involved, without my
having to remember.

#### Acceptance Criteria

1. Book details SHALL show **"AI was used to make this adventure"** as a checkbox.
2. The Adventure SHALL record **AI wrote text** when any of these changes its text:
   - Draft or Revise;
   - Put in section, or Put in draft;
   - applying a suggested edit (feature 37).
3. The Adventure SHALL record **AI made art** when a generated image becomes its cover.
4. Either record SHALL tick the box and disable it, with a line saying why ("The Loremaster
   drafted text in this adventure"). A record SHALL never be cleared, even by Undo.
5. These SHALL NOT record AI use:
   - the side conversation on its own;
   - a split or gather (feature 37);
   - Import Markdown;
   - the Loremaster's split plan;
   - generating cover options that the GM doesn't choose.
6. The GM SHALL be able to tick the box themselves (for AI used outside Nornis) and untick
   their own tick while nothing is recorded.
7. Adventures that already have AI-drafted text SHALL be recorded as AI wrote text when the
   migration runs. Section revisions are only made by AI drafts and Put in section, so an
   Adventure with any revision qualifies.
8. AI use SHALL be in the print snapshot (`PrintDetails`) and in the book details Bibliotaph
   receives (feature 32).

### Requirement 2 — The rights page

#### Acceptance Criteria

1. Every booklet SHALL have a rights page straight after the cover.
2. It SHALL carry:
   - the title;
   - "Nornis Press";
   - "© {year} {author}";
   - the game system, edition and levels;
   - "Printed {date}" (the print's date);
   - "Made with Nornis {version}".
3. It SHALL carry an AI statement that says exactly what was recorded:
   - "Text written with AI assistance."
   - "Cover art generated with AI."
   - both, as "Text written and cover art generated with AI assistance";
   - "AI was used in making this adventure." for the GM's own tick only;
   - "No AI was used to write this adventure's text or art." when nothing is recorded or
     ticked.
4. It SHALL carry a non-affiliation line: game system names are trademarks of their owners,
   and this adventure is not affiliated with or endorsed by them.
5. The cover's credits line (type, publisher, year) SHALL move to the rights page.
   *Amended 2026-11-07 (Dave): the type ("Nornis adventure") is no longer printed; the line
   reads "Nornis Press · {year}". The type stays in the print details and Bibliotaph metadata.*

### Requirement 3 — Cover art

**User Story:** As a GM, I want a cover that looks like a published adventure's.

#### Acceptance Criteria

1. The Adventure page SHALL have a **Cover** card with **Generate**, **Upload** and **Remove**.
2. **Generate** SHALL first show an art brief written by the Loremaster from the title, pitch
   and Overview. The GM can edit it before generating. The brief SHALL avoid Hidden truth,
   because a cover can be seen by players once the book is shared.
3. A generation SHALL make two options. The GM picks one, generates again, or cancels.
4. Generated art SHALL contain no lettering. Nornis sets all type itself.
5. **Upload** SHALL accept a PNG or JPEG of at most 10 MB. An uploaded cover SHALL NOT record
   AI use.
6. The GM SHALL choose one of three cover layouts:
   - **Full bleed**: the art fills the page under a title band;
   - **Framed**: the art sits in a frame with the title above;
   - **Classic module**: the title sits at the top, the art in the middle and the details below.
   
   The booklet without art keeps today's typeset cover.
7. Image generation SHALL be charged to the Prep pool at a configured price per image, and
   SHALL stop at a per-Adventure cap (`PrepOptions.MaxCoverImagesPerAdventure`, 12).
8. The art brief SHALL NOT name living artists or imitate a publisher's trade dress.
9. A filed print SHALL carry its cover, so Bibliotaph's thumbnail is the cover.

### Requirement 4 — Handouts

**User Story:** As a GM, I want the maps and handouts for this adventure printed with it.

#### Acceptance Criteria

1. The Adventure page SHALL have a **Handouts** card. It SHALL list the Adventure's chosen
   handouts in order, with **Add from Library**, **Upload** and **Remove**, and drag to reorder.
2. **Add from Library** SHALL open a picker of the world's Maps and Handouts. The Adventure's own
   documents (feature 36) come first, then the rest, with the Library's category chips and
   search.
3. **Upload** from the card SHALL file the document to the Library already linked to the
   Adventure (feature 36), and add it as a handout.
4. The booklet SHALL end with a **Handouts and maps** appendix:
   - each image on its own page, rotated to landscape when it is wider than it is tall;
   - each PDF handout's pages appended as they are.
5. **Print props** on the print page SHALL make a separate PDF with each prop box (`[!PROP]`) on
   its own page in large type, for cutting out and handing over.
6. Choosing a handout SHALL NOT change its visibility. The booklet is GMOnly when filed, as
   today.
