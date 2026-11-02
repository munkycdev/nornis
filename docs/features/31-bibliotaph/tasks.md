# Tasks

One PR per phase, each shippable alone and each behind `BibliotaphOptions.IsConfigured`, so
prod stays as it is until Dave sets the secrets. Phases C to G need Bibliotaph's matching
step to test end to end; until then they are tested against the stand-in. Phase H waits for
Bibliotaph's half to be live.

**Status: specified 2026-11-02; Phases A to G done 2026-11-02.** Ships as Nornis v3.2.

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

- [x] C1. `BibliotaphConnectService` (begin, authorize, complete, disconnect) with its tests.
- [x] C2. The connection routes and the anonymous callback.
- [x] C3. Profile › Bibliotaph and the return page.

## Phase D — The outbox and the provider endpoints

- [x] D1. `BibliotaphOutboxCapture` in `SaveChangesAsync`, plus the explicit calls in the two
      `ExecuteDelete` paths and the Adventure delete. Capture tests.
- [x] D2. `BibliotaphOutboxBackgroundService` in the API (design.md, Phase B amendment):
      delivery, backoff, failure, purge; no pause gate (Phase D amendment). Tests.
- [x] D3. `api/bibliotaph/v1/me|worlds|worlds/{id}/files|files/{id}/link|resync`. Tests, and the
      sweep: every other route refuses a Bibliotaph token, these refuse a Web token.

## Phase E — Library rows and the Loremaster

- [x] E1. `BibliotaphLibrarySync` and the Library's `bibliotaph` state (its own route; Phase E amendment).
- [x] E2. Covers: the API route and the Web endpoint.
- [x] E3. Library page: Bibliotaph rows, the owner strip, Open.
- [x] E4. `ReferencePassages` return type, `KnowledgePassage.PageLabel` and nullable
      `ChunkId`, the caveat; `BibliotaphReferencePassageRetriever` registered in the API and
      Worker. Tests, including the player sabotage.
- [x] E5. Warm-up channel and background service.

## Phase F — Players read shared books

- [x] F1. `BibliotaphReadingService`, `LibraryReadingRecord` writes and the 90-day purge.
- [x] F2. The read routes for members and shelf links; the renew pages in the Web.
- [x] F3. "Opened by" on the document page.

## Phase G — Excerpts

- [x] G1. `LibraryExcerptService` over Bibliotaph pages; composer overload; the 12-page bound.
- [x] G2. Enrich from Library in search mode.
- [x] G3. `/bibliotaph/excerpt`, the Send to Nornis page.
- [x] G4. v3.2: version bump, Features page, changelog, README. The repo has no user guide,
      so there was no Library chapter to change; the Features page and README carry it.

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
