# Tasks

One PR per phase, each shippable alone and each behind `BibliotaphOptions.IsConfigured`, so
prod stays as it is until Dave sets the secrets. Phases C to G need Bibliotaph's matching
step to test end to end; until then they are tested against the stand-in. Phase H waits for
Bibliotaph's half to be live.

**Status: specified 2026-11-02; Phases A and B done 2026-11-02.** Ships as Nornis v3.2.

Dave's part, outside the repo: the Auth0 setup in the Bibliotaph spec plus the Nornis →
Bibliotaph application (design.md, decision 3), the Container Apps settings (design.md,
*Configuration*), and applying `AddBibliotaph` to prod before the Phase B PR merges.

## Phase A — Spec

- [x] A1. `requirements.md`, `design.md` and this file.
- [x] A2. `domain-model.md` amendment: `LibraryDocument.Origin` and the Bibliotaph fields,
      `BibliotaphConnection`, `BibliotaphWorldEvent`, `LibraryReadingRecord`.
- [x] A3. `security-and-permissions.md` amendment: the `azp` guard, the Bibliotaph policy,
      the token ring, reading tickets.
- [x] A4. Row 31 in `docs/features/README.md`.
- [x] A5. The Bibliotaph spec's Auth0 step 4 names the dedicated application and its callback.

## Phase B — Foundation

- [x] B1. Domain: the three entities, `LibraryDocumentOrigin`, the new `LibraryDocument`
      fields, `BibliotaphExposure`. EF configurations per design.md (no FK on the outbox's
      `WorldId`; the reading record in the world graph and the document delete). Enum tests.
- [x] B2. Migration `AddBibliotaph`. **Applied to prod by Dave before the PR merges.**
- [x] B3. `BibliotaphOptions` with every bound, zero refused at start-up; `IsConfigured`.
- [x] B4. The token ring (`Bibliotaph:KeyBlobUri`, application name `nornis-bibliotaph`) in the
      API and Worker; `provision-azure.ps1` gains the container, grants and settings.
- [x] B5. `IBibliotaphClient`, the contract records, `BibliotaphClient` with per-call budgets
      and the failure mapping; `IBibliotaphTokenSource` with the cache and per-connection
      gate; `FakeBibliotaphHandler` in the test projects.
- [x] B6. The `azp` guard and the `Bibliotaph` policy; the sweep test both ways;
      `UserProvisioningMiddleware` never provisions for a Bibliotaph token.

## Phase C — Connecting

- [ ] C1. `BibliotaphConnectService` (begin, authorize, complete, disconnect) with its tests.
- [ ] C2. The connection routes and the anonymous callback.
- [ ] C3. Profile › Bibliotaph and the return page.

## Phase D — The outbox and the provider endpoints

- [ ] D1. `BibliotaphOutboxCapture` in `SaveChangesAsync`, plus the explicit calls in the two
      `ExecuteDelete` paths. Capture tests.
- [ ] D2. `BibliotaphOutboxBackgroundService` in the API (design.md, Phase B amendment):
      delivery, backoff, failure, purge, pause. Tests.
- [ ] D3. `api/bibliotaph/v1/me|worlds|worlds/{id}/files|files/{id}/link|resync`. Tests.

## Phase E — Library rows and the Loremaster

- [ ] E1. `BibliotaphLibrarySync` and the list response's `bibliotaph` state.
- [ ] E2. Covers: the API route and the Web endpoint.
- [ ] E3. Library page: Bibliotaph rows, the owner strip, Open.
- [ ] E4. `ReferencePassages` return type, `KnowledgePassage.PageLabel` and nullable
      `ChunkId`, the caveat; `BibliotaphReferencePassageRetriever` registered in the API and
      Worker. Tests, including the player sabotage.
- [ ] E5. Warm-up channel and background service.

## Phase F — Players read shared books

- [ ] F1. `BibliotaphReadingService`, `LibraryReadingRecord` writes and the 90-day purge.
- [ ] F2. The read routes for members and shelf links; the renew pages in the Web.
- [ ] F3. "Opened by" on the document page.

## Phase G — Excerpts

- [ ] G1. `LibraryExcerptService` over Bibliotaph pages; composer overload; the 12-page bound.
- [ ] G2. Enrich from Library in search mode.
- [ ] G3. `/bibliotaph/excerpt`, the Send to Nornis page.
- [ ] G4. v3.2: version bump, Features page, changelog, README, the user guide's Library
      chapter.

## Phase H — Cutover (after Bibliotaph's half is live)

- [ ] H1. Dave connects and checks Gribbits: books, a shared book read as a player, a
      Loremaster citation, an excerpt, a world rename.
- [ ] H2. A one-off script (`scripts/`) deleting pre-v3.2 Sourcebook and Other uploads and
      their blobs. Dave runs it.
- [ ] H3. Remove `LibraryChunk`, `LibraryIndexingWorker`, the indexing queue and its client,
      `AzureOpenAiEmbeddingClient` and its options, the vector retriever, Reindex; migration
      `RemoveLibraryChunks`. Dave applies it to prod before the merge.
- [ ] H4. Steering and docs follow: `ai-extraction.md`, `architecture.md`,
      `observability-and-costs.md` (no embedding spend), `azure-hosting.md` (the queue).
