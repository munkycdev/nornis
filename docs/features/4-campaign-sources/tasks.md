# Implementation Plan: Campaign Sources

## Overview

This plan implements Source CRUD operations, visibility enforcement, processing status state machine, and extraction queue integration for the Nornis API. The domain entity (`Source`), enums (`SourceType`, `VisibilityScope`, `SourceProcessingStatus`), and a partial repository implementation already exist. This plan adds the Application service layer (`ISourceService`/`SourceService`), extends the repository with Update and Delete operations, creates the extraction queue client abstraction, builds the API controller and DTOs, and adds comprehensive tests.

## Tasks

- [x] 1. Extend Domain and Infrastructure layer
  - [x] 1.1 Add `UpdateAsync` and `DeleteAsync` to `ISourceRepository` interface and `SourceRepository` implementation
    - Add `Task<Source> UpdateAsync(Source source, CancellationToken cancellationToken = default)` to the domain interface
    - Add `Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)` to the domain interface
    - Implement both in `SourceRepository` using EF Core
    - _Requirements: 3.1, 4.1_

  - [x] 1.2 Create `IExtractionQueueClient` interface in `Nornis.Application/Messaging/`
    - Define `Task SendExtractionMessageAsync(Guid sourceId, Guid campaignId, CancellationToken ct)`
    - Create `ExtractionMessage` record with `SourceId` and `CampaignId` properties
    - _Requirements: 7.1, 7.4_

  - [x] 1.3 Create `ServiceBusExtractionQueueClient` in `Nornis.Infrastructure/Messaging/`
    - Implement `IExtractionQueueClient` using Azure Service Bus SDK
    - Serialize `ExtractionMessage` as JSON and send to `source-extraction` queue
    - Accept `ServiceBusClient` via DI constructor injection
    - _Requirements: 7.1, 7.2, 7.4_

- [x] 2. Implement Application layer commands and service
  - [x] 2.1 Create Application command models in `Nornis.Application/Models/`
    - Implement `CreateSourceCommand` record with CampaignId, Title, Type, Visibility, CreatingUserId, CreatingUserRole, Body, Uri, OccurredAt
    - Implement `UpdateSourceCommand` record with SourceId, CampaignId, ActingUserId, ActingUserRole, and optional Title, Body, Uri, OccurredAt, Type, Visibility
    - Implement `MarkSourceReadyCommand` record with SourceId, CampaignId, ActingUserId, ActingUserRole
    - _Requirements: 1.1, 3.1, 6.1_

  - [x] 2.2 Create `ISourceService` interface in `Nornis.Application/Services/`
    - Define `CreateAsync`, `GetByIdAsync`, `UpdateAsync`, `DeleteAsync`, `ListByCampaignAsync`, `MarkReadyAsync` methods
    - All methods return `AppResult<T>` or `AppResult` and accept `CancellationToken`
    - _Requirements: 1.1, 2.1, 3.1, 4.1, 5.1, 6.1_

  - [x] 2.3 Implement `SourceService` in `Nornis.Application/Services/`
    - Inject `ISourceRepository`, `ICampaignMemberRepository`, `IExtractionQueueClient`
    - Implement input validation: Title (1–200 non-blank), Body (≤100,000), Uri (≤2,048), SourceType/VisibilityScope enum validation
    - Implement role enforcement: Observer cannot create/update/delete; Player cannot set GMOnly visibility
    - Implement ownership enforcement: only creator or GM can update/delete/mark-ready
    - Implement visibility filtering: Private (creator + GMs), GMOnly (GMs only), PartyVisible (all members)
    - Implement visibility-as-not-found pattern (return not-found, not forbidden, for invisible sources)
    - Implement processing status guards: updates blocked when Queued/Processing/Processed; deletes blocked when Queued/Processing
    - Implement state machine transitions using static `ValidTransitions` dictionary
    - Implement mark-ready: Draft→Ready, enqueue extraction message, transition to Queued on success
    - Implement failed enqueue handling: leave at Ready, return error
    - Implement list ordering: CreatedAt descending
    - _Requirements: 1.1–1.10, 2.1–2.7, 3.1–3.9, 4.1–4.5, 5.1–5.6, 6.1–6.4, 7.1–7.3, 8.1–8.4, 9.1–9.5_

  - [x] 2.4 Write property test: Source Creation Field Mapping
    - **Property 1: Source Creation Field Mapping**
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4**

  - [x] 2.5 Write property test: Invalid Titles Are Rejected
    - **Property 2: Invalid Titles Are Rejected**
    - **Validates: Requirements 1.5, 3.4**

  - [x] 2.6 Write property test: Players Cannot Set GMOnly Visibility
    - **Property 3: Players Cannot Set GMOnly Visibility**
    - **Validates: Requirements 1.9, 3.6, 9.5**

  - [x] 2.7 Write property test: Only Creator or GM Can Mutate a Source
    - **Property 4: Only Creator or GM Can Mutate a Source**
    - **Validates: Requirements 1.8, 3.2, 4.2, 6.3**

- [x] 3. Checkpoint - Application layer core verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement visibility and status enforcement tests
  - [x] 4.1 Write property test: Visibility Enforcement on Get
    - **Property 5: Visibility Enforcement on Get**
    - **Validates: Requirements 2.2, 2.3, 2.4, 2.7, 9.1, 9.2, 9.3, 9.4**

  - [x] 4.2 Write property test: Visibility Enforcement on List
    - **Property 6: Visibility Enforcement on List**
    - **Validates: Requirements 5.1, 5.2, 5.3**

  - [x] 4.3 Write property test: Processing Status Guards on Update
    - **Property 7: Processing Status Guards on Update**
    - **Validates: Requirements 3.3**

  - [x] 4.4 Write property test: Processing Status Guards on Delete
    - **Property 8: Processing Status Guards on Delete**
    - **Validates: Requirements 4.3**

  - [x] 4.5 Write property test: Processing Status State Machine
    - **Property 9: Processing Status State Machine**
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4**

  - [x] 4.6 Write property test: Mark Ready Enqueues and Transitions to Queued
    - **Property 10: Mark Ready Enqueues and Transitions to Queued**
    - **Validates: Requirements 6.1, 7.1, 7.2**

  - [x] 4.7 Write property test: Failed Enqueue Leaves Source at Ready
    - **Property 11: Failed Enqueue Leaves Source at Ready**
    - **Validates: Requirements 7.3**

  - [x] 4.8 Write property test: List Ordering
    - **Property 12: List Ordering**
    - **Validates: Requirements 5.4**

- [x] 5. Implement API layer request/response contracts
  - [x] 5.1 Create request DTOs in `Nornis.Api/Contracts/Requests/`
    - Implement `CreateSourceRequest` record with Title, Type, Visibility, Body, Uri, OccurredAt
    - Implement `UpdateSourceRequest` record with optional Title, Body, Uri, OccurredAt, Type, Visibility
    - _Requirements: 1.1, 3.1_

  - [x] 5.2 Create response DTOs in `Nornis.Api/Contracts/Responses/`
    - Implement `SourceResponse` record with Id, CampaignId, Type, Title, Body, Uri, OccurredAt, CreatedAt, CreatedByUserId, Visibility, ProcessingStatus
    - Implement `SourceListItemResponse` record with Id, CampaignId, Type, Title, OccurredAt, CreatedAt, CreatedByUserId, Visibility, ProcessingStatus
    - _Requirements: 1.1, 2.1, 5.5_

- [x] 6. Implement SourcesController
  - [x] 6.1 Create `SourcesController` in `Nornis.Api/Controllers/`
    - `POST /api/campaigns/{campaignId}/sources` — Parse enum strings, map to CreateSourceCommand, call SourceService.CreateAsync, return 201 with SourceResponse
    - `GET /api/campaigns/{campaignId}/sources` — Call SourceService.ListByCampaignAsync, return list of SourceListItemResponse
    - `GET /api/campaigns/{campaignId}/sources/{sourceId}` — Call SourceService.GetByIdAsync, return SourceResponse
    - `PUT /api/campaigns/{campaignId}/sources/{sourceId}` — Parse and validate enum strings, map to UpdateSourceCommand, call SourceService.UpdateAsync, return SourceResponse
    - `DELETE /api/campaigns/{campaignId}/sources/{sourceId}` — Call SourceService.DeleteAsync, return 204
    - `POST /api/campaigns/{campaignId}/sources/{sourceId}/ready` — Map to MarkSourceReadyCommand, call SourceService.MarkReadyAsync, return SourceResponse
    - Apply `CampaignMemberActionFilter` for all endpoints
    - Map AppResult errors to HTTP status codes: 400 (validation), 403 (forbidden/insufficient_role), 404 (not_found), 409 (invalid_status/invalid_transition), 502 (enqueue_failed)
    - Return 400 for invalid enum strings before calling service
    - _Requirements: 1.1–1.10, 2.1–2.7, 3.1–3.9, 4.1–4.5, 5.1–5.6, 6.1–6.4, 7.1–7.3, 10.1–10.4_

- [x] 7. Wire up DI registration and routing
  - [x] 7.1 Update `Program.cs` to register new services and configure routing
    - Register `ISourceService` → `SourceService` in DI container
    - Register `IExtractionQueueClient` → `ServiceBusExtractionQueueClient` in DI container
    - Configure Azure Service Bus client in DI (from configuration/Key Vault)
    - Map SourcesController routes with CampaignMemberActionFilter
    - _Requirements: 10.1, 10.3_

- [x] 8. Checkpoint - API layer verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Integration tests
  - [x] 9.1 Set up integration test infrastructure for Sources in `Nornis.Api.Tests`
    - Register `FakeExtractionQueueClient` implementing `IExtractionQueueClient` (records messages, configurable to throw)
    - Add helper methods for creating test sources and campaign members with various roles
    - _Requirements: 7.1, 7.3_

  - [x] 9.2 Write integration tests for source creation endpoint
    - Test valid creation by GM returns 201 with correct SourceResponse fields
    - Test valid creation by Player returns 201
    - Test creation by Observer returns 403
    - Test invalid SourceType string returns 400
    - Test invalid VisibilityScope string returns 400
    - Test empty/whitespace title returns 400
    - Test title exceeding 200 chars returns 400
    - Test body exceeding 100,000 chars returns 400
    - Test uri exceeding 2,048 chars returns 400
    - Test Player creating GMOnly source returns 400
    - Test ProcessingStatus is Draft on creation
    - _Requirements: 1.1–1.10, 10.1, 10.2_

  - [x] 9.3 Write integration tests for source retrieval endpoints
    - Test GM can get any source regardless of visibility
    - Test Player can get PartyVisible source
    - Test Player can get own Private source
    - Test Player cannot get other user's Private source (returns 404)
    - Test Player cannot get GMOnly source (returns 404)
    - Test Observer can get PartyVisible source
    - Test Observer cannot get Private or GMOnly source (returns 404)
    - Test non-existent source returns 404
    - Test non-member returns 403
    - _Requirements: 2.1–2.7, 9.1–9.4, 10.1, 10.2_

  - [x] 9.4 Write integration tests for source update endpoint
    - Test creator can update own source fields
    - Test GM can update any source
    - Test non-creator Player cannot update returns 403
    - Test Observer cannot update returns 403
    - Test update with invalid title returns 400
    - Test Player setting GMOnly visibility returns 400
    - Test update blocked when Queued/Processing/Processed returns 409
    - Test partial update modifies only specified fields
    - _Requirements: 3.1–3.9, 9.5_

  - [x] 9.5 Write integration tests for source delete endpoint
    - Test creator can delete own source
    - Test GM can delete any source
    - Test non-creator Player cannot delete returns 403
    - Test delete blocked when Queued/Processing returns 409
    - Test delete allowed when Draft/Ready/Processed/Failed
    - Test non-existent source returns 404
    - _Requirements: 4.1–4.5_

  - [x] 9.6 Write integration tests for source list endpoint
    - Test GM sees all sources regardless of visibility
    - Test Player sees PartyVisible and own Private sources
    - Test Observer sees only PartyVisible sources
    - Test ordering is CreatedAt descending
    - Test empty campaign returns empty list
    - _Requirements: 5.1–5.6_

  - [x] 9.7 Write integration tests for mark-ready and enqueue endpoint
    - Test mark-ready from Draft succeeds and transitions to Queued
    - Test mark-ready from non-Draft status returns 409
    - Test non-creator non-GM cannot mark ready returns 403
    - Test queue failure leaves source at Ready and returns 502
    - Test extraction message contains correct SourceId and CampaignId
    - _Requirements: 6.1–6.4, 7.1–7.3_

- [x] 10. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- The domain entity (`Source`), enums (`SourceType`, `VisibilityScope`, `SourceProcessingStatus`), and base repository already exist — this plan adds the missing repository methods, Application service, extraction queue client, API controller, and tests
- Property tests use FsCheck.NUnit with in-memory repository fakes and a configurable fake `IExtractionQueueClient`
- Integration tests use `WebApplicationFactory<Program>` with in-memory EF Core and a fake `IExtractionQueueClient`
- Checkpoints ensure incremental validation before progressing to the next layer
- Visibility enforcement returns 404 (not 403) to prevent information leakage about source existence
- The `CampaignMemberActionFilter` already exists and handles campaign-scoped authorization — Sources controller reuses it

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "2.1", "2.2"] },
    { "id": 2, "tasks": ["2.3", "5.1", "5.2"] },
    { "id": 3, "tasks": ["2.4", "2.5", "2.6", "2.7"] },
    { "id": 4, "tasks": ["4.1", "4.2", "4.3", "4.4", "4.5", "4.6", "4.7", "4.8"] },
    { "id": 5, "tasks": ["6.1"] },
    { "id": 6, "tasks": ["7.1"] },
    { "id": 7, "tasks": ["9.1"] },
    { "id": 8, "tasks": ["9.2", "9.3", "9.4", "9.5", "9.6", "9.7"] }
  ]
}
```
