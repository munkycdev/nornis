# Design Document

> **Amendment (2026-11-02, Phase B):** what building the foundation changed. The text below is
> left as written.
>
> - **The outbox is delivered by the API, not the Worker.** The Worker scales to zero on queue
>   depth (`provision-azure.ps1`), so a poller there would sleep until an unrelated extraction
>   woke it, and a rename could wait hours. The API runs one replica that never scales to
>   zero, and already hosts the upload sweep and the continuity audit, so
>   `BibliotaphOutboxBackgroundService` lives there. The Worker still gets the Bibliotaph
>   client and the key ring, for extraction's reference books (Phase E).
> - **A reading record has one foreign key**, to its document (cascade). `WorldId`, `UserId`
>   and `PlayerId` are plain columns and the viewer's name is copied at the time
>   (`ViewerName`), so a log row never blocks a player's claim or removal.
> - **`Bibliotaph:Auth0Domain`** is its own setting, because the Worker has no `Auth0`
>   section. Configured means `BaseUrl`, `Auth0Domain`, `ClientId` and `ClientSecret` are set;
>   `ProviderClientId` and `WebOrigin` are API-only and are checked where they are used. The
>   `azp` guard is on whenever `ProviderClientId` is set, configured or not.
> - **Every bound refuses to start at zero**, and a configured integration without
>   `KeyBlobUri` refuses to start, as the Web does without its key ring
>   (`BibliotaphOptions.StartupProblem`).
> - **The token ring is built privately** (`BibliotaphTokenProtector.Create`), in its own
>   service collection, so it never mixes with a host's Data Protection keys. An unreadable
>   ciphertext reads as a refused grant, so a lost ring asks GMs to reconnect instead of
>   failing every call.
> - **A 403 that isn't `kept_from_ai` marks the connection Needs reconnect** too: it means a
>   scope is missing, and reconnecting is what grants it.
> - **The contract's list answers are wrapped**: `{ "books": [...] }` and `{ "pages": [...] }`.
>   The Bibliotaph spec now says so.
> - Infrastructure pins `System.Security.Cryptography.Xml` 10.0.14, which Data Protection
>   pulls at a vulnerable 10.0.11, as `Pkcs` is pinned.

> **Amendment (2026-11-02, Phase D):** what building the outbox and the provider changed. The
> text below is left as written.
>
> - **The capture reads the old values from the database.** Repositories save detached
>   entities with `Update`, which marks every property modified with the original equal to the
>   new value, so the change tracker can't say whether a name changed or an upload was pending.
>   `BibliotaphOutboxCapture` queries the stored world names and upload statuses instead, and
>   only once a connected creator is involved: one query for the worlds and one for the
>   connections when a world or exposed upload is in the save, one more for statuses.
> - **Deleting an Adventure queues `files-changed`** when it has a print, because its prints
>   are exposed through their `AdventureId` and lose it (`AdventureRepository.DeleteAsync`).
> - **`BibliotaphExposure.Rule` is an expression**, so the provider's query and the capture's
>   in-memory check are one rule.
> - **No pause gate on the outbox.** The operational pause stops AI spend in the Worker; the
>   outbox makes no AI calls.
> - **Delivery** (`BibliotaphOutboxDelivery`): no connection fails the events at once; every
>   other failure, a refused grant included, waits with backoff; and any event older than 7 days
>   is failed with an error log. Reconnecting or Resync sends a fresh `created` for each world,
>   which repairs what a given-up event would have said. Reading records are purged in Phase F,
>   where they are first written.
> - **Resync with no connection queues nothing** and still answers 202: the outbox holds rows
>   only for connected creators.
> - **File links** come from `IBlobStorageService.GenerateReadLinkAsync` with
>   `Bibliotaph:FileLinkMinutes` (60, refused above 60 at start-up). The user delegation key now
>   lives 2 hours, since a SAS dies with the key that signed it and the old 1-hour key could not
>   sign an hour-long link.
> - **A cursor Nornis didn't issue is a 400 `bad_cursor`.** The provider's reads go through
>   `IBibliotaphProviderRepository`.
> - **Blob CORS.** Bibliotaph's reader reads byte ranges from Nornis blob storage from its own
>   origin, so `provision-azure.ps1` adds a CORS rule for `BibliotaphBaseUrl` once (with
>   `BibliotaphProviderClientId` set).
> - **Open with Bibliotaph:** the spec's provider path uses the file name as the segment, and
>   Nornis doesn't keep file names unique within a world. The provider item id is the file id,
>   which is unique; Bibliotaph should key on that.

> **Amendment (2026-11-02, Phase E):** what building the Library rows and the Loremaster
> changed. The text below is left as written.
>
> - **The list keeps its shape.** `GET library` stays a bare array, which the Web and the shelf
>   already read. The strip's state comes from `GET api/worlds/{id}/library/bibliotaph`, which
>   reconciles first, inside the throttle, and answers `{ state, ownerName }`. The Library page
>   calls it before listing. A new state, `not_set_up`, covers Bibliotaph answering 404 because
>   the world's `created` event hasn't arrived yet. The shelf page does not reconcile: shelf
>   players see the rows as the GM's last visit left them.
> - **Warm-ups are `POST api/worlds/{id}/library/bibliotaph/warm`**, not a side effect of a
>   world-entry endpoint, because the Web never calls `GET worlds/{id}`. `WorldState` sends one
>   when it restores or switches the world. The queue is `BibliotaphWarmUps` (100, drop when
>   full, 10 minutes per world) and `BibliotaphWarmUpBackgroundService` sends them.
> - **Covers** go through `IBibliotaphCovers`, which uses `LibraryService.GetByIdAsync` for the
>   visibility check, so whoever may see a row may see its cover.
> - **`LibraryService` refuses download, delete and reindex on a Bibliotaph row** with 409
>   `in_bibliotaph`. A removed row is not listed, and `GetByIdAsync` answers 404 for it, so
>   every route that reads a row inherits that.
> - **The retriever** maps each page onto the rows the caller's scopes can see. That catches a
>   GMOnly page even if Bibliotaph ignored `entryIds`, a second guard behind the request
>   itself. When an upload Bibliotaph catalogued would be quoted twice, Bibliotaph's pages win
>   over the vector passages. A page's reference id is `passage:{rowId:N}-{pageIndex}`. Excerpt
>   search keeps to indexed chunks until Phase G gives Bibliotaph pages a page-range filing.
> - The resolver, the sync and its throttle are registered by `AddBibliotaph`, so the API and
>   the Worker share them. The Worker gains `IUserRepository` for the owner's name.
> - A reconcile that loses the unique entry index to another process logs the conflict and
>   keeps the rows; the next reconcile catches up.

> **Amendment (2026-11-02, Phase F):** what building reading changed. The text below is left
> as written.
>
> - **`page` on a renew address is a 0-based index**, as Bibliotaph's reader counts. It is
>   passed through as the ticket's `startPage` unchanged, and the API's `read` body takes
>   `startPage` 0-based too.
> - **An upload Bibliotaph catalogued opens in Bibliotaph's reader** like a Bibliotaph book,
>   because it has a document id there. An upload it hasn't catalogued answers the same route
>   with a download (`kind: download`), so a caller never has to choose.
> - **The reading log is purged by the API's outbox pass** (1,000 per pass), since the outbox
>   moved to the API in Phase B.
> - **The member read page waits for the world selection** before minting, so one visit
>   mints one ticket. It resolves the key in the current world. Shelf reads go through
>   `IShelfLinkService.GetDocumentAsync`, which applies the shelf's own visibility check.
> - "Opened by" lists the last 50 opens, marks shelf-link opens, and is GM only.

## Overview

Nornis keeps the Library page, its rows and its GMOnly / PartyVisible chip. What changes is
where a sourcebook comes from and who reads it:

```
Nornis Web ──bearer (Web client)──▶ Nornis API ──bearer (Nornis→Bibliotaph client)──▶ Bibliotaph /api/nornis/v1
                                       ▲  │                                              │
                                       │  └─ outbox rows ──▶ Nornis Worker ──webhook─────┘
Bibliotaph NornisProvider ──bearer (Bibliotaph client, published:read)──▶ Nornis API /api/bibliotaph/v1
```

- **One Auth0 grant in each direction.** Nornis holds a refresh token for the Bibliotaph API,
  per connected GM. Bibliotaph holds a refresh token for the Nornis API (its storage
  provider), per connected GM. Neither app ever reads the other's subject as an identity.
- **The API owns the connection.** The code exchange, the stored refresh token and every
  call to Bibliotaph run in the API or the Worker. The Web never sees a Bibliotaph token: it
  asks the API for addresses and redirects the browser to them.
- **The outbox is a SQL table the Worker polls.** See *Design decisions*: the design
  proposal said Service Bus; this is the deviation and its reason.
- **The Library rows are a cache of Bibliotaph's answer**, reconciled when the page loads.
  Visibility lives only on the Nornis row. Bibliotaph never learns which books are shared.
- **Retrieval goes through the one interface it goes through today**,
  `IReferencePassageRetriever`, with a Bibliotaph implementation in front of the vector one.
  After the cutover (Phase H) the vector one is deleted.

The contract (paths, fields, codes) is
[Bibliotaph: Nornis integration spec](https://claude.ai/artifact/TjE2qTGmB1WcBZV4GxcKYB). This
document only says how Nornis meets it.

## Where each rule lives (pre-implementation check 1)

| Rule | The one place |
| --- | --- |
| Whether the integration is on | `BibliotaphOptions.IsConfigured` (Application). False when `Bibliotaph:BaseUrl`, `ClientId`, `ClientSecret` or `ProviderClientId` is unset; every Bibliotaph path checks it and the Profile section is hidden. |
| Whose Bibliotaph a world uses | `BibliotaphConnectionResolver.ForWorldAsync(worldId)`: the connection of `World.CreatedByUserId`, or none for a demo or template world. Retriever, Library sync, tickets, covers and excerpts all ask it. |
| The access token for a connection | `IBibliotaphTokenSource` (Infrastructure): refresh-token grant, in-memory cache until a minute before expiry, `invalid_grant` turns the connection Needs reconnect. Nothing else talks to Auth0's token endpoint for Bibliotaph. |
| Turning a Bibliotaph refusal into Nornis state | `BibliotaphClient` (Infrastructure). It maps 401 `not_connected` to `BibliotaphFailure.NotConnected` and marks the connection, 503 `opening` to `Opening`, 403 `kept_from_ai` to `KeptFromAi`, a timeout or 5xx to `Unreachable`. Callers switch on the failure, never on status codes. |
| What reaches the outbox | `BibliotaphOutboxCapture` (Infrastructure, beside `SlugAssigner`). Tracked changes are captured in `NornisDbContext.SaveChangesAsync`; the two `ExecuteDelete` paths (`WorldRepository.DeleteAsync`, `LibraryDocumentRepository.DeleteAsync`) call its explicit method inside their own transaction. |
| Which uploads Bibliotaph can see | `BibliotaphExposure.IsExposed(LibraryDocument)` (Domain, static): Origin Upload, Status Stored or Indexed or Indexing or IndexFailed (anything past PendingUpload), and Kind Sourcebook or Other, or an `AdventureId`. The provider endpoints and the outbox capture both read it. |
| Who may read a Bibliotaph book | `LibraryService.GetByIdAsync`, unchanged in meaning: GM sees all, Player and Observer see PartyVisible. Ticket minting calls it first; the shelf calls the shelf's existing visibility check. A removed row (`RemovedAt` set) is a 404 for everyone. |
| What a player's question may search | `BibliotaphReferencePassageRetriever`: GM scope sends no `entryIds`; any scope without GMOnly sends the entry ids of the world's live PartyVisible rows, and makes no call when there are none. |
| Which Bibliotaph app may call which endpoint | Two authorization policies in `Auth0Extensions`: the default policy refuses a token whose `azp` is `Bibliotaph:ProviderClientId`; the `Bibliotaph` policy requires that `azp` and scope `published:read`. |
| Excerpt size | `BibliotaphOptions.MaxExcerptPages` (12), and the existing `SourceService.MaxBodyChars`, which the composed body is checked against. |

## What null, zero and empty mean (pre-implementation check 3)

| Value | Meaning |
| --- | --- |
| No `BibliotaphConnection` row for a user | Not connected. No events are queued for their worlds, and their worlds' Library pages show uploads only. |
| `BibliotaphConnection.Status` NeedsReconnect | Bibliotaph or Auth0 refused the stored grant. Events are still queued (the connection exists) and wait; reads are skipped and the page says "Reconnect Bibliotaph". |
| `BibliotaphConnection.LastUsedAt` null | Connected but never called. |
| `LibraryDocument.BibliotaphEntryId` null | Not in Bibliotaph's collection. On an Upload row, Bibliotaph hasn't catalogued it (yet, or it isn't exposed). Always set on a Bibliotaph row. |
| `LibraryDocument.RemovedAt` null | Live. Set means the book left the collection: hidden, unreadable, unsearchable, but the row stays so excerpts keep their `LibraryDocumentId`. |
| `LibraryDocument.CoverName` null | Bibliotaph has no cover; the page shows the kind's icon. |
| `LibraryDocument.RemoteTitle` | The title Bibliotaph last sent. When `Title` equals it, the next reconcile updates both; when the GM renamed the row, only `RemoteTitle` moves. |
| `entryIds` absent on a passages call | The whole collection (GM scope only). An empty PartyVisible set never makes a call, so absent never means "players, everything". |
| `BibliotaphWorldEvent.DeliveredAt` null | Not delivered. `FailedAt` set means given up after 7 days. |
| `LibraryReadingRecord.UserId` null | Opened from a shelf link; `PlayerId` is then set. Exactly one of the two is set. |
| `BibliotaphOptions.MaxExcerptPages` 0 | Refused at start-up, as the Prep bounds are. |

## What is bounded (pre-implementation check 4)

| Thing | Bound |
| --- | --- |
| Every call to Bibliotaph | `HttpClient.Timeout` 10 s as the backstop, and a per-call budget below it: passages 4 s, books list 5 s, page text 8 s, ticket 5 s, cover 5 s and 2 MB, warm-up 2 s, webhook 15 s. No call retries inline; only the outbox retries. |
| Calls to Auth0 for tokens | One refresh per connection per access-token lifetime per process, through a per-connection `SemaphoreSlim`, so a burst of questions waits for one refresh. |
| Library reconciles | At most one per world per 60 s per API process (in-memory stamp; one API replica today). |
| Warm-ups | A bounded channel of 100 (drop when full), at most one per world per 10 minutes. |
| The outbox | Rows only for connected creators. Delivered and failed rows purged after 7 days; undelivered rows fail after 7 days. Batches of 100. |
| Backfill | One `created` per world the user created, queued in one save. Dave has a handful; a user with thousands would still drain at 100 per poll. |
| The reading log | Purged after 90 days by the Worker's outbox tick, in batches of 1,000. |
| Connect state | Data-protected, with a 10-minute expiry inside it. No table. |
| Excerpts | 12 pages (`MaxExcerptPages`) and the source body limit. |
| Provider listings | Paged by cursor, 100 per page. |
| The connection list a GM can create | One connection per user (unique index on `UserId`). |

## What happens with a second caller (pre-implementation check 2)

- **Two Worker replicas deliver the same event.** Harmless: the event id is the idempotency
  key on Bibliotaph's side. So the outbox needs no claim or lease, and the Worker selects with
  no lock. If the Worker ever scales out, the duplicate rate rises, never correctness.
- **The API and the Worker refresh the same grant.** Refresh-token rotation is **off** on the
  Nornis → Bibliotaph application, as it is for the Web (see `Auth0TokenRefresher`). With it
  on, two processes refreshing one token race, and Auth0's reuse detection would revoke the
  whole family. With it off, both get valid access tokens from the same refresh token.
- **Two tabs reconcile one world.** The in-memory stamp is set before the call, so the second
  load reads the rows as they are. A reconcile that loses a race on the unique
  `(WorldId, BibliotaphEntryId)` index catches the conflict, reloads and moves on.
- **A GM connects twice in two tabs.** The second callback replaces the stored grant (upsert
  on `UserId`) and revokes the first. Backfill runs twice; Bibliotaph's `created` is
  idempotent.
- **A co-GM (not the creator) opens the Library.** They see the creator's collection through
  the creator's connection. They can set visibility and titles on the Nornis rows, as for
  uploads. They cannot connect a Bibliotaph to someone else's world.

## Domain

### `BibliotaphConnection` (new)

| Field | Notes |
| --- | --- |
| `Id` | |
| `UserId` | FK User, cascade. Unique. |
| `ProtectedRefreshToken` | Data Protection ciphertext, purpose `Nornis.Bibliotaph.RefreshToken`. Never logged, never in a response. |
| `Status` | `BibliotaphConnectionStatus`: Connected, NeedsReconnect. |
| `ConnectedAt`, `StatusChangedAt` | |
| `LastUsedAt` | Written at most every 5 minutes, as Bibliotaph does for devices. |

No Bibliotaph subject or account id is stored. What the GM sees ("Connected since…") needs
nothing more.

### `BibliotaphWorldEvent` (new, the outbox)

| Field | Notes |
| --- | --- |
| `Id` | The `eventId` sent to Bibliotaph. |
| `UserId` | The creator whose connection delivers it. FK User, cascade. |
| `WorldId` | **Plain Guid, no FK**, so a world's deletion can't take its own `deleted` event with it. |
| `Kind` | `BibliotaphWorldEventKind`: Created, Renamed, Deleted, FilesChanged. Serialized as `created`, `renamed`, `deleted`, `files-changed`. |
| `WorldName` | Cut to 200 characters. |
| `OccurredAt` | |
| `Attempts`, `NextAttemptAt`, `LastError` | `LastError` is a code and status, never a body. |
| `DeliveredAt`, `FailedAt` | |

Index `(DeliveredAt, FailedAt, NextAttemptAt)` for the poll, and `(UserId, OccurredAt)`.

### `LibraryReadingRecord` (new)

`Id`, `WorldId` (FK World, cascade), `LibraryDocumentId` (FK, cascade), `UserId?` (FK User,
NoAction), `PlayerId?` (FK Player, NoAction), `OpenedAt`. Index
`(LibraryDocumentId, OpenedAt)`. The world deletion graph and the document delete remove
these explicitly, as they do other satellites.

### `LibraryDocument` (changed)

- `Origin`: `LibraryDocumentOrigin` Upload (0, every existing row) or Bibliotaph.
- `BibliotaphEntryId`, `BibliotaphDocumentId`: strings, 64 characters, opaque. Unique filtered
  index on `(WorldId, BibliotaphEntryId)`.
- `RemoteTitle`, `CoverName`, `RemovedAt`, as in the null table.
- A Bibliotaph row has `Kind` Sourcebook, `Status` Stored, empty `BlobPath` and `FileName`,
  `SizeBytes` 0, `PageCount` from Bibliotaph, and `UploadedByUserId` set to the creator.
- An upload that Bibliotaph catalogued keeps `Origin` Upload and gains the entry and document
  ids, so it appears once, can still be downloaded, and is read and searched through
  Bibliotaph like the rest.

Migration **`AddBibliotaph`**, additive only. Dave applies it to prod before the Phase B PR
merges (docs/runbooks/migration-missed.md).

## Application

### `IBibliotaphClient`

One method per contract endpoint, each returning `BibliotaphResult<T>` (value or
`BibliotaphFailure`). Contract records live in `Nornis.Application/Bibliotaph/Contracts`, named
as the spec names the fields, with page indexes kept 0-based at this boundary and converted to
1-based page numbers in exactly one place, `BibliotaphPages.ToPageNumber`.

### `BibliotaphConnectService`

- `BeginAsync(userId)` → the Bibliotaph `/connect/nornis` address. Its `return` is
  `{Web origin}/profile/bibliotaph?state={state}`. `state` is a Data Protection payload with
  the user id, a PKCE verifier and a 10-minute expiry.
- `AuthorizeUrlAsync(userId, state)` → the Auth0 authorize address for the Bibliotaph API
  audience, `scope=openid books:read tickets:write worlds:write offline_access`, the PKCE
  challenge, `redirect_uri=https://api.nornis.app/bibliotaph/callback`, `state` as given.
  Refuses a state for another user or past its expiry.
- `CompleteAsync(code, state)` → exchanges the code with the client secret and verifier,
  validates the ID token (issuer, audience = `Bibliotaph:ClientId`, signature from the
  tenant's JWKS, lifetime), refuses when its `sub` isn't the state user's
  `Auth0SubjectId`, upserts the connection, and queues `created` for each world the user
  created, in one save.
- `DisconnectAsync(userId)` → revokes the refresh token at Auth0's `/oauth/revoke`
  (best effort, logged), deletes undelivered events and the connection.

### `BibliotaphLibrarySync`

`ReconcileAsync(worldId, ct)` → `BibliotaphShelfState` (Ok, NotConnected, NeedsReconnect,
Opening, Unreachable, plus the creator's display name). `LibraryService.ListAsync` calls it
first when the creator is connected, then lists rows as today, minus removed ones. The list
response gains `bibliotaph: { state, ownerName }`.

Matching, in order: an entry with `nornisFileId` matches that upload row; an entry whose
`entryId` matches a row updates it; any other entry inserts a GMOnly Bibliotaph row; a row
whose entry is absent gets `RemovedAt`. A removed row that comes back clears `RemovedAt`
**and resets to GMOnly**, since the GM may have shared it under different circumstances.

### `BibliotaphReferencePassageRetriever`

Implements `IReferencePassageRetriever`, decorating the vector retriever until the cutover.

- No connection for the creator → the inner retriever (uploads only), unchanged.
- Otherwise one call to `passages`, mode `ask`, limit 10, with `entryIds` per the rule above.
  Pages map to `KnowledgePassage` with `DocumentId` from the row whose entry id matches,
  `Page` from `pageIndex + 1`, and the new `PageLabel` from `pageLabel` (the printed number,
  which is what the citation shows). A page whose entry has no live row is dropped; for a GM
  question one reconcile is attempted first, inside the 60-second throttle.
- `ChunkId` becomes `Guid?` on `KnowledgePassage`: null for a Bibliotaph page.
- Failure → no passages, and the result says the reference books were unavailable.

The interface changes from returning a list to returning `ReferencePassages
{ Passages, Unavailable }`. `KnowledgeContext` gains `ReferenceBooksUnavailable`, and
`LoremasterService.AssembleCaveats` adds "The reference books couldn't be reached, so this
answer uses world memory only." The three callers (Loremaster, extraction, excerpt search)
change with it; Prep's drafting and conversation use the knowledge retriever, not this one.

Public Ask stays out because `IncludeLibrary` is false there, unchanged.

### `BibliotaphReadingService`

`OpenAsync(worldId, documentKey, member | shelfPlayer, startPage?)`:

1. Resolve the row through `LibraryService.GetByIdAsync` (members) or the shelf's visibility
   check (shelf link). Removed or not visible is a 404.
2. An Upload row with no entry id → today's download, unchanged.
3. Otherwise mint a ticket with the creator's connection: `documentId`, `startPage`
   (0-based), `viewerName` (member display name or player name, cut to 100), and
   `renew` = `{Web origin}/library/{key}/read` or `/shelf/{code}/read/{documentId}`.
4. Write a `LibraryReadingRecord` and return the ticket address. The Web redirects.

`ListOpensAsync(documentId)`: GM only, the last 50 opens with names, for the document page.

### `LibraryExcerptService` (changed)

- `SearchAsync` with a connected creator calls `passages` in mode `search`; the candidates
  carry the document id and page number instead of a chunk id.
- `FileAsync` for a Bibliotaph-read row takes a page range (1-based, at most
  `MaxExcerptPages`), reads the text through `pages`, and composes the body with the existing
  `LibraryExcerptComposer`, which gains an overload over page texts. `kept_from_ai` → 403
  with that code and Bibliotaph's reason. Everything after (Draft, `MarkReadyAsync`, review)
  is unchanged.
- The **Send to Nornis** page (`/bibliotaph/excerpt`) maps Bibliotaph's `documentId` to the
  Nornis row (reconciling once if needed), shows the page texts, lets the GM trim the range
  to 12 pages and pick a codex entry, then calls `FileAsync`.

## API

### For the Web (default policy)

| Route | Notes |
| --- | --- |
| `GET api/bibliotaph/connection` | `{ configured, status, connectedAt, lastUsedAt }` |
| `POST api/bibliotaph/connection/begin` | `{ url }` for `/connect/nornis` |
| `POST api/bibliotaph/connection/authorize` | `{ state }` → `{ url }` for Auth0 |
| `DELETE api/bibliotaph/connection` | Disconnect |
| `POST api/worlds/{worldId}/library/{documentId}/read` | `{ startPage? }` → `{ url }` |
| `GET api/worlds/{worldId}/library/{documentId}/opens` | GM only |
| `GET api/worlds/{worldId}/library/{documentId}/cover` | Streams Bibliotaph's cover |
| `POST api/shelf/{code}/documents/{documentId}/read` | Anonymous like the shelf, `{ url }` |
| `GET api/worlds/{worldId}/library/bibliotaph-document/{bibliotaphDocumentId}` | For the Send to Nornis page: the Nornis row, after one reconcile if unknown |
| `GET api/worlds/{worldId}/library/{documentId}/pages?from=&to=` | GM only, 1-based, at most 12 |

The existing excerpt search and file routes keep their shapes and gain the page-range body.

### The callback (anonymous)

`GET bibliotaph/callback?code=&state=` → `BibliotaphConnectService.CompleteAsync`, then a
redirect to `{Web origin}/profile?bibliotaph=connected`, or `=refused` / `=failed`. It is the
only anonymous Bibliotaph route; it has no body and sets no cookie.

### For Bibliotaph (`Bibliotaph` policy), under `api/bibliotaph/v1`

`me`, `worlds`, `worlds/{worldId}/files`, `files/{fileId}/link`, `resync`, exactly as the
contract lists them. `worlds` and `files` page by an opaque cursor (the last id seen, base64)
at 100 per page. `files` lists rows passing `BibliotaphExposure.IsExposed`, with `etag` =
`UpdatedAt` ticks. `link` reuses `IBlobStorageService.GenerateDownloadSasUrlAsync` with a
one-hour expiry. A world the caller didn't create, or a file not exposed, is a 404.

`UserProvisioningMiddleware` never creates a user for a Bibliotaph-client token: an unknown
subject on these routes is a 401.

### Authorization

`Auth0Extensions` adds an `azp` requirement to the default policy (refuse
`Bibliotaph:ProviderClientId`) and registers the `Bibliotaph` policy. The existing 403 sweep
test gains a pass that calls every route with a Bibliotaph-client token and expects 403,
except the five provider routes, and calls those five with a Web token and expects 403.

### Configuration (Container Apps secrets, Dave)

| Setting | API | Worker |
| --- | --- | --- |
| `Bibliotaph__BaseUrl` (`https://bibliotaph.online`) | ✓ | ✓ |
| `Bibliotaph__Audience` (`https://bibliotaph.online/api`) | ✓ | ✓ |
| `Bibliotaph__ClientId`, `Bibliotaph__ClientSecret` (the Nornis → Bibliotaph application) | ✓ | ✓ |
| `Bibliotaph__ProviderClientId` (Bibliotaph's own application) | ✓ | |
| `Bibliotaph__WebOrigin` (`https://nornis.app`) | ✓ | |
| `Bibliotaph__KeyBlobUri` (a new private container `bibliotaph-keys`, granted to the API and Worker identities only) | ✓ | ✓ |

`scripts/provision-azure.ps1` gains the container, the two grants and the settings.

## Worker

`BibliotaphOutboxWorker` (BackgroundService): every 15 s, read up to 500 due rows, group by
user, send each user's rows oldest first in batches of 100, then:

- 2xx → `DeliveredAt`.
- `not_connected` or `invalid_grant` → connection NeedsReconnect; rows stay pending with
  their backoff.
- anything else → `Attempts++`, `NextAttemptAt = now + min(1 h, 1 min × 2^(Attempts−1))`,
  and past 7 days since `OccurredAt`, `FailedAt` and an error log naming the world and kind.

The same tick purges outbox rows delivered or failed over 7 days ago and reading records over
90 days old. It runs under `PausableProcessing` like the other workers, so the operational
pause stops it.

## Web

- **Profile › Bibliotaph**: the three states, Connect (calls `begin`, navigates), Reconnect
  (same), Disconnect (confirm). `/profile/bibliotaph?state=&bibliotaph=` is the return page:
  on `connected` it calls `authorize` and navigates to Auth0; on `full` or `invite-required`
  it explains; anything else shows a plain failure.
- **Library page**: Bibliotaph rows render with a cover (through a Web endpoint
  `GET /library-covers/{worldId}/{documentId}`, cookie-authenticated, which forwards to the
  API with the member's token and sends `Cache-Control: private, max-age=86400`), the chip,
  and Open instead of Download. A strip above the list says "Books from {owner}'s Bibliotaph"
  or the failure state. Delete is not offered on a Bibliotaph row; its help text says to take
  the book out of the collection in Bibliotaph.
- **`/library/{key}/read`** and **`/shelf/{code}/read/{documentId}`**: call `read` with
  `?page=` and redirect, or explain the refusal. These are the renew addresses.
- **Document page**: "Opened by" for the GM.
- **`/bibliotaph/excerpt`**: the confirm page described above.
- Opening a world sends a warm-up through the API (the world-entry endpoint queues it).

## Testing strategy

Against a stand-in Bibliotaph (`FakeBibliotaphHandler`, an `HttpMessageHandler` answering the
contract shapes), as the AI tests use a stand-in model. Each bullet is failing first, then
passing (pre-implementation check 5).

- **Authorization**: the sweep described under *Authorization*; a Bibliotaph token with the
  wrong scope is 403 on the provider routes; an unknown subject is 401 and creates no user.
- **Connect**: a state for another user, an expired state, and an ID token whose `sub` isn't
  the user's are refused; success stores a protected token (the column never holds the raw
  token) and queues one `created` per created world, none for demo or template worlds.
- **Outbox capture**: create, rename and delete a world, confirm and delete an exposed upload,
  file a print; each writes exactly one event, in the same save; a Map or Handout upload and
  an unconnected creator write none; deleting a world leaves its `deleted` event in place.
- **Outbox delivery**: batching at 100, backoff values, failure at 7 days, `not_connected`
  flips the connection and keeps the rows, the purge.
- **Reconcile**: insert GMOnly, update untouched title, keep a GM's rename, match an upload by
  `nornisFileId`, remove and re-add (back to GMOnly), throttle, timeout falls back to rows.
- **Retriever**: a Player's question sends only PartyVisible entry ids and nothing when there
  are none (sabotage: send all and watch the GMOnly book appear); a GM's sends none; a timeout
  gives the caveat; extraction with a PartyVisible source scope never searches GMOnly books.
- **Reading**: a player can't open a GMOnly or removed book; a shelf link opens only shared
  books; every open is recorded; the renew address is the page's own.
- **Provider endpoints**: handouts, maps and pending uploads never listed; another user's world
  is 404; paging through 250 rows.
- **Excerpts**: the 12-page bound, `kept_from_ai`, page numbers 1-based in the title.

## Design decisions

1. **An outbox table polled by the Worker, not Service Bus.** The design proposal said
   events go "through Nornis's Service Bus outbox". A Service Bus send can't join the SQL
   transaction, so a reliable send needs an outbox table either way; once that table exists,
   the Worker reading it and calling the webhook is the whole job, and a queue in between
   adds a hop, a second retry policy and a dead-letter queue to watch, for nothing.
2. **The API, not the Web, completes the code flow.** The Web already does OIDC, but doing
   this one there would put the Bibliotaph refresh token in the process that renders pages
   and holds every member's session. The API is the process that uses it.
3. **A dedicated Auth0 application** for Nornis → Bibliotaph, confidential, with
   authorization code and refresh token grants, rotation off, callback
   `https://api.nornis.app/bibliotaph/callback`. Its secret goes to the API and Worker only.
   Reusing the Web's application would put that secret in the API too and tie the two flows'
   settings together.
4. **A separate Data Protection ring** in its own container, not the Web's ring: the Web
   doesn't need to decrypt the token, so it can't.
5. **Stateless connect state** (protected payload) rather than a table: the code is
   single-use at Auth0, the ID-token subject check binds the flow to the user, and a table
   would need its own sweep.
6. **Everyone opens a Bibliotaph book through a ticket, the creator included.** The contract
   has no address for Bibliotaph's own reader, and a single path is easier to secure. The
   creator can open the book in Bibliotaph directly; a later contract change could add a
   "read in Bibliotaph" link for them.
7. **`entryIds` for players is built from Nornis rows**, so a book is searchable for players
   only once a reconcile has made it a row and a GM has shared it. That is the intended
   order: nothing a GM hasn't seen in Nornis reaches a player.
