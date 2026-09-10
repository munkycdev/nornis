# Requirements Document

## Introduction

Thistlehold is a city in the codex. The record knows it from session notes: the party arrived,
bought supplies, met a magistrate. The Player's Guide in the Library has four pages on
Thistlehold — its districts, its council, its guilds, the river trade — and no player is ever
going to type that into a session note. The codex entry stays thin beside a book that says
everything, and the two never meet.

The Library was scoped as reference-only: indexed for the Loremaster to quote, never extracted
into canon. That was the right rule for a 300-page sourcebook, and it is still the rule for a
document as a whole. What it prevents, needlessly, is the GM choosing the four pages that are
about Thistlehold and saying *this is canon for my world*.

This feature lets the GM file an **excerpt** of a Library document as a **Source**. From there
nothing new happens: the ordinary extraction run produces a review batch, the GM accepts the
proposals, the facts cite the excerpt, and the accept-time summary refresh rewrites
Thistlehold's summary from a record that now knows what the guide knows. Ask picks up the facts
the way it picks up any others.

The governing constraints:

- **Whole documents are still never extracted.** The GM chooses the pages. The choosing is the
  gate.
- **Provenance is unchanged.** An excerpt is a Source; the facts it yields cite it like any
  other, and its own page says which document and pages it came from.
- **Visibility is the document's.** A party-visible guide yields a party-visible excerpt; a
  GM-only sourcebook yields a GM-only one. Nothing derived from a GM shelf reaches the party.
- **Review is not bypassed.** The excerpt goes through the queue and the proposal review that
  every other source does.

## Requirements

### Requirement 1 — An excerpt is a source

**User Story:** As a GM, I want the pages of a Library document that are about a thing in my
world to become a source in the record, so that what the book says can be extracted and cited.

#### Acceptance Criteria

1. WHEN a GM files an excerpt THEN a `Source` of type `LibraryExcerpt` SHALL be created in the
   world, carrying the document it came from and the page range it covers, with the passages'
   text as its body.
2. The excerpt's visibility SHALL be the document's visibility.
3. The excerpt SHALL be queued for extraction on creation, through the same path every other
   source takes to the queue; it SHALL NOT be a Draft the GM has to process separately.
4. The excerpt's title SHALL name the document and pages, and the codex entry it was filed for
   when there is one ("Thistlehold — Player's Guide, pp. 42–45").
5. A source of type `LibraryExcerpt` SHALL be readable on its own page like any source, and
   that page SHALL link to the document at the pages the excerpt covers.
6. Only a GM SHALL file an excerpt. A Player or Observer SHALL be refused with 403.
7. An excerpt SHALL be bounded: at most a fixed number of passages per excerpt, so the body
   stays under the source body limit and a single filing cannot swallow a book.

### Requirement 2 — Choosing the pages from the codex entry

**User Story:** As a GM on Thistlehold's page, I want Nornis to find the passages about
Thistlehold in the Library so I can tick the ones that belong and file them.

#### Acceptance Criteria

1. WHEN a GM opens "Enrich from the library" on an artifact THEN the Library SHALL be searched
   for passages matching the artifact, using the existing similarity search with the artifact's
   name and summary as the query, over every shelf the GM can see.
2. The results SHALL be listed grouped by document, each with its page and its text, and each
   selectable.
3. The GM SHALL be able to change the query and search again.
4. WHEN the GM files the selection THEN one excerpt source SHALL be created per document
   selected, filed for the artifact.
5. IF the world has no indexed documents THEN the dialog SHALL say so rather than show an
   empty list.

### Requirement 3 — Choosing the pages from the document

**User Story:** As a GM reading the Player's Guide in the Library, I want to file pages 42 to 45
as a source, optionally for a codex entry, because I already know which pages matter.

#### Acceptance Criteria

1. The document page SHALL offer a GM a page range and an optional codex entry, and SHALL file
   the passages that start within that range as one excerpt source.
2. IF the range holds no passages THEN filing SHALL be refused with a message that says so.
3. IF the range holds more passages than the excerpt bound THEN filing SHALL be refused with a
   message that asks for a narrower range.
4. Filing SHALL be refused for a document that is not indexed.

### Requirement 4 — Extraction knows what it is reading

**User Story:** As a GM, I want extraction to treat an excerpt as the world's written canon,
not as a night at the table, so that the proposals are the right shape.

#### Acceptance Criteria

1. WHEN the extraction pipeline reads a `LibraryExcerpt` source THEN the prompt SHALL say it
   is published setting material the GM chose deliberately, describing the world as it is
   rather than events that happened in play.
2. Facts extracted from an excerpt SHALL default to `Confirmed` rather than `Likely`.
3. WHEN the excerpt names the codex entry it was filed for THEN the prompt SHALL direct
   proposals at that entry rather than at a new artifact of the same name.
4. The pipeline SHALL NOT retrieve reference passages from the Library to ground an excerpt;
   the excerpt is the reference.
5. Every other rule of extraction — visibility mapping, one proposal per change, review gate,
   budget guard, usage recording — SHALL apply unchanged.

### Requirement 5 — Deleting the document keeps the record

**User Story:** As a GM, I want deleting a document from the Library to leave the excerpts it
produced and everything extracted from them in place.

#### Acceptance Criteria

1. WHEN a document is deleted THEN its excerpt sources SHALL remain, with their text and
   title, and their link to the document SHALL be cleared.
2. Facts and relationships citing an excerpt SHALL be unaffected.

### Requirement 6 — The rule is written down

#### Acceptance Criteria

1. `domain-model.md` SHALL carry a dated amendment recording that GM-chosen excerpts are
   extracted and whole documents are not, with the reasoning.
2. The `LibraryDocument` entity comment SHALL say the same.
3. `docs/future-features.md` SHALL no longer claim Ask ignores the Library; it has not since
   the reference-passage retriever shipped.
