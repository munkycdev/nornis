# Requirements Document

## Introduction

The Library is one flat list. Bibliotaph books (feature 31), uploaded maps and handouts, and
Prep prints (features 29 and 34) sit in a single run, newest first, with only an icon to tell
them apart. That was fine with a handful of books. It won't hold up once Prep makes handouts
and maps for each adventure, which is where Prep is going next (features 37 and 38).

On 2026-11-03 Dave said he didn't want to "flood the Library with materials that are relevant
to a single one-shot and obscure everything else", and that enhancing the Library with
categories and filters is "actually worth doing". He agreed to the shape proposed in the Prep
thread:

- a **category** for every document, with filter chips like the Prep page's (feature 29, PR #86);
- an optional **"belongs to an adventure"** link. A document with the link stays out of the
  default view and shows under that adventure's filter.

A Prep handout picker (feature 38) then lists an adventure's own documents first. This
feature builds the Library half; the picker is 38's.

Ships as Nornis 3.7. It needs one migration, which Dave applies.

## Glossary

- **Category**: what a document is: Book, Map, Handout, Print or Other. It is today's
  `LibraryDocumentKind` (Sourcebook, Map, Handout, Other), plus Print, which is decided by
  `AdventureId` and never stored.
- **Adventure document**: a document linked to one Adventure by its **adventure link**. A print
  is always its Adventure's document (feature 34 sets `AdventureId`). An upload may be given a
  link.
- **Shelf**: the Library's default view: every document that is not an adventure document,
  plus prints.
- **Filter chip**: one of the row of chips above the list that narrows it.

## Requirements

### Requirement 1 — Categories

**User Story:** As a GM, I want to see only my maps, or only my books, without scrolling past
everything else.

#### Acceptance Criteria

1. Every document SHALL show its category as a label, as well as the icon it has today.
2. The Library SHALL show filter chips: **All**, **Books**, **Maps**, **Handouts**, **Prints**
   and **Other**, with each chip's count. Choosing a chip SHALL show only that category.
3. A chip whose count is zero SHALL be hidden, except **All**.
4. A Bibliotaph row SHALL be a Book. A print SHALL be a Print, whatever kind it was uploaded as.
5. The GM SHALL be able to change an upload's category (Map, Handout, Other or Book). A
   Bibliotaph row's or a print's category SHALL NOT change.
6. The chosen chip SHALL be remembered for the browser, as the Prep page's is.

### Requirement 2 — Adventure documents

**User Story:** As a GM, I want the handouts and maps I make for one adventure to stay with that
adventure, not bury my books.

#### Acceptance Criteria

1. The GM SHALL be able to link an upload to one of the world's Adventures, or clear the link,
   from the document's page and in the upload dialog.
2. An upload with an adventure link SHALL NOT show on the Shelf. It SHALL show under All and
   under its category chip only while that adventure's filter is chosen.
3. Prints SHALL show on the Shelf, as they do today. They are a world's publications, not
   working material.
4. The Library SHALL offer an **Adventure** filter: a menu of the world's Adventures that have
   documents. Choosing one SHALL show that adventure's documents (uploads and prints) and SHALL
   combine with the category chips.
5. Deleting an Adventure SHALL clear its uploads' links, so they return to the Shelf. It SHALL
   NOT delete them.
6. A player SHALL NOT see an adventure link, the Adventure filter, or an Adventure's title.
   Players see GMOnly documents as they do today (they don't), and see PartyVisible adventure
   documents on the Shelf, because the link is GM organisation and Prep is GM-only.
7. The Adventure page SHALL list its documents with links to them.

### Requirement 3 — Finding a document

**User Story:** As a GM, I want to find a document by name when the shelf gets long.

#### Acceptance Criteria

1. The Library SHALL have a search box that filters by title, ignoring case. It SHALL combine
   with the chips and the Adventure filter.
2. The list SHALL be sortable by **Newest** (the default) or **Title**.
3. Filtering and sorting SHALL happen in the browser over the list the page already loads. The
   Library has no paging today and a world's documents are few.

### Requirement 4 — Nothing else moves

#### Acceptance Criteria

1. Visibility (GMOnly or PartyVisible), Bibliotaph reading, excerpts and the Loremaster's
   reference search SHALL behave exactly as today. Neither a category nor a link changes who
   can read or search a document.
2. A print's `AdventureId` SHALL stay set only by `LibraryService.FilePrintAsync`
   (`PrepBoundaryTests`). An upload's adventure link SHALL be its own column, so a linked
   handout is never mistaken for a print.
