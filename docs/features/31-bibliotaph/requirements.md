# Requirements Document

## Introduction

The Library was built so the Loremaster could quote rulebooks: a GM uploads a PDF, Nornis
chunks and embeds it, and answers cite "Title, p. N". It works, but it is a second, weaker
copy of something the GM already has. **Bibliotaph** (bibliotaph.online) is the GM's own
sourcebook library: it catalogues books where they live (OneDrive, Dropbox, Google Drive,
Box, WebDAV), searches by words and by meaning, reads them by byte range, and never keeps a
copy. It shares the MunkyC Auth0 tenant with Nornis.

This feature makes the two apps aware of each other. Sourcebooks are catalogued in Bibliotaph
and appear on the Nornis Library page; Nornis stops indexing PDFs. The Library page itself
stays, with the same GMOnly / PartyVisible chip, and handouts and maps stay in Nornis.

The design was settled with Dave on 2026-11-02 in
[Bibliotaph replaces the Nornis Library: design proposal](https://claude.ai/artifact/3hXBap2VxErwtAkmJeeShc).
Bibliotaph's half is specified in
[Bibliotaph: Nornis integration spec](https://claude.ai/artifact/TjE2qTGmB1WcBZV4GxcKYB) and
built in its own repo. **That spec is the contract between the two apps**: an endpoint shape
changes there first, then here.

### The worked example

1. Dave opens **Profile › Bibliotaph** and presses **Connect Bibliotaph**. The browser goes to
   bibliotaph.online and back, normally without a prompt. Bibliotaph now has a collection
   for each world Dave created, including *Gribbits Detective Agency*.
2. In Bibliotaph he drags *Player's Guide* and *Monster Manual* into the Gribbits
   collection. The next time the Gribbits Library page opens, both books are there with
   their covers, GMOnly.
3. He sets *Player's Guide* to PartyVisible. A player opens it from the Library page and
   reads it in Bibliotaph's guest reader. No Bibliotaph account, no download.
4. A player asks the Loremaster how grappling works. The answer quotes *Player's Guide*,
   p. 195. It never quotes *Monster Manual*, which is GMOnly.
5. On Thistlehold's codex entry Dave uses **Enrich from Library**, ticks two pages of the
   *Player's Guide*, and files them. They become a `LibraryExcerpt` source and go through
   review as today.
6. Dave renames the world. Within a minute the Bibliotaph collection has the new name.

The governing constraints:

- **Nothing is linked by `sub`.** Each app accepts only a token it validated and maps it
  through its own tables. A Bibliotaph-held token for the Nornis API can reach only the
  Bibliotaph endpoints.
- **Nornis decides who may read; Bibliotaph decides what can be read.** Visibility stays a
  Nornis setting. Bibliotaph only answers for books in a world's collection.
- **Bibliotaph is optional and can be absent.** With no connection, or Bibliotaph down, the
  Library page, uploads, handouts and the Loremaster keep working without reference books.
- **No new unbounded thing.** The outbox, the reading log and every remote call have a bound
  and a timeout, named below.

Versions follow the app. This feature ships as **Nornis v3.2**, after Prep's v3.0 and v3.1.

## Requirements

### Requirement 1 — Connecting Bibliotaph

**User Story:** As a GM, I want to connect my Bibliotaph once, from Nornis, without copying
keys around.

#### Acceptance Criteria

1. **Profile** SHALL show a **Bibliotaph** section when the integration is configured: Not
   connected, Connected (since, last used), or Needs reconnect, with Connect, Reconnect and
   Disconnect.
2. Connect SHALL send the browser to Bibliotaph's `/connect/nornis` and, on return, run an
   authorization-code flow with PKCE for the Bibliotaph API audience with the scopes
   `openid books:read tickets:write worlds:write offline_access`. The callback SHALL be
   served by the API, which exchanges the code and stores the refresh token.
3. The callback SHALL refuse a code whose ID token subject is not the Auth0 subject of the
   Nornis user who started the flow, so one person can't attach another person's Bibliotaph.
4. The refresh token SHALL be stored encrypted with Data Protection, under a key ring that
   the API and the Worker share and the Web cannot read.
5. Connecting SHALL queue a `created` world event for every world the user created, so
   Bibliotaph makes the collections.
6. Disconnect SHALL revoke the refresh token at Auth0, delete the connection and drop the
   user's undelivered events. Collections and books in Bibliotaph SHALL stay.
7. When Bibliotaph answers `not_connected`, the connection SHALL become **Needs reconnect**,
   and the Library page SHALL say so to the GM.

### Requirement 2 — Keeping Bibliotaph's collections current

**User Story:** As a GM, I want Bibliotaph's world collections to follow my worlds without me
doing anything.

#### Acceptance Criteria

1. Creating, renaming or deleting a world, and adding or removing a file Bibliotaph can see
   (Requirement 6), SHALL queue an event in the same database transaction as the change.
2. Events SHALL be queued only for a world whose creator has a connection, and never for a
   demo or template world.
3. The Worker SHALL deliver queued events to Bibliotaph's webhook, at most 100 per call,
   oldest first per user, and retry with exponential backoff from 1 minute to 1 hour.
4. An event not delivered within 7 days SHALL be marked failed and logged as an error.
   Delivered and failed events SHALL be deleted after 7 days.
5. Bibliotaph's settings can ask Nornis to resync: Nornis SHALL then queue `created` for
   every world the user created.

### Requirement 3 — Bibliotaph books on the Library page

**User Story:** As a GM, I want the books in a world's collection to show up on its Library
page, with the same sharing control as an uploaded PDF.

#### Acceptance Criteria

1. When the Library page loads for a world whose creator is connected, Nornis SHALL read the
   collection's books from Bibliotaph and reconcile its own rows: a new book becomes a
   `LibraryDocument` of origin Bibliotaph, GMOnly; a book that left the collection is marked
   removed and hidden; a book whose file is a Nornis upload is matched to that upload's row,
   so it appears once.
2. The read SHALL be skipped if the same world was read less than 60 seconds ago, and SHALL
   time out after 5 seconds. If it fails, the page SHALL show the rows from the last read and
   a note that Bibliotaph couldn't be reached.
3. A Bibliotaph book SHALL show its cover, title and visibility chip. A GM SHALL be able to
   set GMOnly or PartyVisible, rename the Nornis title and open the book. Deleting is done in
   Bibliotaph by taking the book out of the collection.
4. In a world with several GMs, every member SHALL see the creator's collection, and the page
   SHALL say whose Bibliotaph the books come from.

### Requirement 4 — The Loremaster quotes Bibliotaph

**User Story:** As a member, I want answers to quote the books my GM shared, with page
citations, as they do today.

#### Acceptance Criteria

1. When the world's creator is connected, the Loremaster and extraction SHALL take reference
   passages from Bibliotaph's passages endpoint instead of Nornis's own index.
2. A GM's question SHALL search the whole collection. A Player's or Observer's SHALL search
   only the world's PartyVisible books, and SHALL not call Bibliotaph when there are none.
3. The call SHALL time out after 4 seconds. On a timeout, an error, `opening` or
   `not_connected`, the answer SHALL be given from world memory alone and SHALL say that the
   reference books weren't reachable.
4. Citations SHALL keep the "Title, p. N" form, linking to the Nornis document page. N is
   the book's printed page label where Bibliotaph has one.
5. Public Ask SHALL never include reference books, as today.

### Requirement 5 — Players read shared books

**User Story:** As a player, I want to read the books my GM shared, the way I could if they
brought them to the table.

#### Acceptance Criteria

1. Opening a PartyVisible Bibliotaph book SHALL check membership and visibility, ask
   Bibliotaph for a reading ticket with the creator's connection, and redirect to it.
2. A player on a standing shelf link SHALL be able to open the same books from the shelf.
3. The ticket request SHALL carry a renew address back to the same Nornis page, which checks
   again before minting the next ticket.
4. Every open SHALL be logged with the world, document, and member or shelf-link player. The
   GM SHALL see who opened a book on its page. Log rows SHALL be deleted after 90 days.
5. Uploaded files keep today's behaviour: GMs and permitted players download them.

### Requirement 6 — Nornis as a storage provider for Bibliotaph

**User Story:** As a GM, I want books I upload to Nornis to be catalogued in Bibliotaph like
the rest of my library.

#### Acceptance Criteria

1. The API SHALL serve `/api/bibliotaph/v1/me`, `/worlds`, `/worlds/{worldId}/files`,
   `/files/{fileId}/link` and `/resync`, for tokens issued to Bibliotaph's Auth0 application
   with scope `published:read`.
2. A token issued to Bibliotaph's application SHALL be refused on every other Nornis
   endpoint, and a token issued to the Nornis Web SHALL be refused on these.
3. `/worlds` SHALL list the worlds the caller created. `/files` SHALL list a world's stored
   uploads of kind Sourcebook or Other, and Prep prints (a Handout with an `AdventureId`).
   Never another handout, never a map, never a pending upload.
4. `/files/{fileId}/link` SHALL return a read-only SAS address valid for one hour.

### Requirement 7 — Excerpts from Bibliotaph

**User Story:** As a GM, I want to file pages of a Bibliotaph book as an excerpt, from Nornis
or from Bibliotaph.

#### Acceptance Criteria

1. **Enrich from Library** SHALL search Bibliotaph in `search` mode when the creator is
   connected, and file the ticked pages' text as a `LibraryExcerpt` source.
2. Bibliotaph's **Send to Nornis** SHALL open `/bibliotaph/excerpt`, which shows the pages'
   text for the GM to confirm, then files it the same way. GM only.
3. An excerpt SHALL be at most 12 pages, and its body SHALL stay within the source limit.
   Bibliotaph may send a range of up to 40 pages; the GM trims it to 12 before filing.

### Requirement 8 — Retiring Nornis's own indexing

**User Story:** As the operator, I want Nornis to stop paying to embed PDFs once Bibliotaph
does the work.

#### Acceptance Criteria

1. The cutover SHALL run only after Bibliotaph's half is live and Dave's worlds are connected.
2. It SHALL delete the Sourcebook and Other uploads made before Nornis v3.2 shipped, and
   their blobs (Dave's books are already in Bibliotaph), and remove the
   chunk table, the indexing worker and queue, the embedding client and its options.
   Excerpts filed from a deleted upload keep their title and text, which were copied at
   filing; their `LibraryDocumentId` becomes null, as it does today.
3. Handouts, maps and Prep prints SHALL be untouched.
