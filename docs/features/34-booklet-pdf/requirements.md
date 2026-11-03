# Requirements Document

## Introduction

A Prep adventure prints as a booklet (feature 29). Today the booklet is an HTML page that the
browser lays out. Filing it to the Library takes three steps: print to a PDF on the GM's own
machine, save it, and upload it through the File to Library dialog. Feature 30 deferred a
one-click version because it needed headless Chromium in production.

Dave walked Nornis 3.4 on 2026-11-03 with *Everybody Dies at the Slurping Spa* and asked for
two things:

- **One-click filing**: "save to the blob storage and import it to the library without the
  step of printing to a local file and then loading it manually".
- **More natural page breaks**: "bulleted list items crash into the vertical separator.
  There are weird large gaps of empty space all over the place. Kind of feels like column
  breaks aren't intuitive to me."

Nornis 3.5 makes the booklet a PDF that Nornis lays out itself with a .NET PDF library in the
API. That one PDF is what the GM previews, downloads, prints and files.

Dave made two choices on 2026-11-03:

- He chose a **.NET PDF library in the API** over a separate scale-to-zero print container
  running headless Chrome. The cost is that the booklet's layout is now written in C#, not
  CSS. The HTML booklet is retired, so there is still only one layout.
- He chose **two columns with continuous flow**, over a single column.

### What the Slurping Spa PDF showed

Each defect traces to how the browser laid out the HTML:

| What Dave saw | Cause |
| ------------- | ----- |
| Bullets crash into the separator | List markers sit outside the text box. In the right column they hang into the column rule. In the left column they are clipped, so those bullets vanish. |
| Large gaps everywhere | Every section is its own two-column block under a full-width heading, balanced on its own. A box that will not fit the rest of a page jumps to the next page and strands the space above it. A section a few lines over a page leaves a nearly blank page. |
| Column breaks are not intuitive | A section that crosses a page reads left, right, then left again on the next page, and full-width section headings cut across the columns. |
| Lines run together ("20-25 minutes **Starting location:**") | The editor and the rest of Nornis treat a single newline as a line break. The booklet's Markdown pipeline treated it as a space. |
| `[ ]` printed as text | Task-list items were not recognised. |

## Glossary

- **Booklet**: the Adventure laid out for print, as one PDF.
- **Booklet model**: the booklet's structure as typed blocks (cover, chapters, headings,
  paragraphs, lists, tables, boxes, stat blocks), built from the Adventure and its print
  details. It has no fonts, sizes or pages.
- **Box**: a read-aloud passage, a prop (an in-world document) or a question to the table.
  These are `PrepMarkup`'s three blockquote conventions.
- **Flow**: text runs down the left column, then the right column, then on to the next page.

## Requirements

### Requirement 1 — The booklet is a PDF Nornis lays out

**User Story:** As a GM, I want the booklet I see to be the booklet I print and file.

#### Acceptance Criteria

1. The booklet SHALL be a PDF rendered by the API from the Adventure and its print details
   (feature 32).
2. The print page SHALL show that PDF, with **Download** and **File to Library**. Printing on
   paper SHALL use the PDF viewer's own Print.
3. The HTML booklet SHALL be retired: its route, its renderer and its print stylesheet.
4. The cover SHALL keep feature 32's content: the title, the game system and edition, the
   levels, the author, the pitch, and the credits line.
5. A section that has not been written SHALL be left out, as it is today, and a Chapter SHALL
   always print its title.
6. Only a caller whose current role is GM SHALL get the booklet.
7. The same Adventure, unchanged, SHALL give the same booklet each time, apart from the PDF's
   creation date.

### Requirement 2 — Natural page breaks

**User Story:** As a GM, I want the booklet to read like a printed module, without gaps or
columns that jump around.

#### Acceptance Criteria

1. The cover SHALL have a page to itself.
2. Every Chapter SHALL start a new page. Its title SHALL span both columns.
3. Within a Chapter, text SHALL flow: down the left column, then the right, then the next page.
   Section titles (Scene, Notes and the others) SHALL sit inside the column, never across the
   page.
4. Sections before the first Chapter SHALL flow the same way on the page after the cover.
5. A heading SHALL NOT be the last thing in a column. It SHALL move to the next column with
   the start of what follows it.
6. A box or stat block SHALL stay whole in one column when it fits in one. One taller than
   that SHALL continue into the next column, rather than leave a column empty.
7. A table SHALL repeat its header row in each column it continues into. A row SHALL NOT split.
8. A bullet, number or checkbox SHALL sit inside its column, never in the gutter or margin.
9. A gap SHALL appear only at the foot of a column, when the next box, row or heading does not
   fit there.

### Requirement 3 — The booklet prints what was written

**User Story:** As a GM, I want my line breaks, lists and checklists printed as I wrote them.

#### Acceptance Criteria

1. A single newline inside a paragraph SHALL print as a line break, as it shows in the editor.
2. A task-list item (`- [ ]`, `- [x]`) SHALL print with an empty or a ticked box.
3. Bold, italic, strikethrough and inline code SHALL print as such. A link SHALL print its text.
4. A Markdown image SHALL print as its alt text. The API SHALL NOT fetch any URL while
   rendering.
5. Raw HTML in a section SHALL print as text, as it does today.
6. A character that the booklet's fonts cannot draw SHALL fall back to a fallback font, or
   print as a blank glyph. It SHALL NOT fail the booklet.

### Requirement 4 — Filing takes one click

**User Story:** As a GM, I want to file the booklet to the Library without saving and
uploading a file myself.

#### Acceptance Criteria

1. **File to Library** on the print page SHALL file the booklet in one click, with no file
   dialog.
2. The filed print SHALL be what feature 29 and feature 32 made a print:
   - a GM-only Handout, titled "{title}, printed {date}";
   - recording its Adventure, and becoming the Adventure's last print;
   - keeping its print details, resolved at filing.
3. The filed PDF SHALL be rendered by the same code as the preview, from the Adventure as it
   stands when the GM clicks.
4. After filing, the page SHALL say so and link to the Library document.
5. A print SHALL be filed only by a GM, and only for an Adventure in that world.
6. A booklet over the Library's upload size limit SHALL be refused with nothing stored.
7. Filing a print by uploading a file (a Library upload that names an Adventure) SHALL be
   retired. One path files prints.

### Requirement 5 — Nothing else changes

#### Acceptance Criteria

1. Existing filed prints SHALL stay as they are.
2. A filed print SHALL reach Bibliotaph as feature 32 describes, with its `book` block.
3. The Prep list's Published filter SHALL keep meaning "filed to the Library".
4. Ordinary Library uploads SHALL be unchanged.

## Out of scope

- **Page size.** The booklet is US Letter. A4 is a small follow-up (one setting) if anyone
  asks.
- **Printing only the props**, deferred since feature 30.
- **Images in the booklet.** Sections rarely hold them, and fetching them would make the API
  fetch URLs a GM typed.
- **A Chrome-based print service.** Dave chose the library.
- **Widow and orphan control inside long paragraphs.** The library has no line-level control.
  Requirement 2's rules cover headings, boxes and rows, which caused the gaps Dave saw.

## Staging

| Phase | What ships | Migration |
| ----- | ---------- | --------- |
| A | This spec | No |
| B | Booklet model, PDF renderer and fonts, the booklet route, and the print page showing the PDF with Download. The HTML booklet is retired. | No |
| C | One-click File to Library, retiring the upload path for prints, and the Nornis 3.5 version bump | No |

Neither phase has a migration.
