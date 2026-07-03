# Implementation Plan: Review Proposal Workflow

## Overview

This plan implements the Review Proposal Workflow — the API and application service layer for reviewing, accepting, rejecting, and editing ReviewProposals. The implementation follows clean architecture: ReviewsController in the API layer delegates to ReviewService in the Application layer, which orchestrates authorization, visibility filtering, proposal validation (ProposalValidator), knowledge graph mutation (ProposalApplicator), batch lifecycle management, idempotency, and transactional consistency. Domain repository interfaces are extended for filtered queries, and Infrastructure provides the implementations.

Existing domain entities (ReviewProposal, ReviewBatch, Source, Artifact, ArtifactFact, ArtifactRelationship, SourceReference), enums, and base repository interfaces are already in place. This plan adds the review-specific application service, validator, applicator, API controller with DTOs, extended repository methods, and comprehensive tests including FsCheck.NUnit property-based tests.

## Tasks

- [x] 1. Define application models, commands, and validation interfaces
  - [x] 1.1 Create application command and result records in `Nornis.Application/Models/`
    - Create `ReviewQueueQuery` record with CampaignId, ActingUserId, ActingUserRole, FilterByBatchId (nullable)
    - Create `ReviewQueueResult` record with Proposals list, HasMore bool
    - Create `AcceptProposalCommand`, `RejectProposalCommand`, `EditProposalCommand` records
    - Create `BatchAcceptCommand`, `BatchRejectCommand` records
    - Create `AcceptProposalResult`, `RejectProposalResult`, `EditProposalResult` records
    - Create `BatchOperationResult` with Succeeded list and Failed list
    - Create `BatchFailureDetail` record with ProposalId, Code, Message
    - _Requirements: 1.1, 2.1, 3.1, 4.1, 5.1_

  - [x] 1.2 Create `IProposalValidator` interface and payload records in `Nornis.Application/Validation/`
    - Define `IProposalValidator` with `AppResult ValidateProposedValue(string json, ReviewChangeType changeType)`
    - Create `CreateArtifactPayload`, `UpdateArtifactPayload`, `MergeArtifactPayload` records
    - Create `AddFactPayload`, `UpdateFactPayload` records
    - Create `AddRelationshipPayload`, `UpdateRelationshipPayload` records
    - _Requirements: 2.10, 4.3_

  - [x] 1.3 Create `IProposalApplicator` interface in `Nornis.Application/Application/`
    - Define `IProposalApplicator` with `Task<AppResult<ApplyResult>> ApplyAsync(ReviewProposal proposal, ReviewBatch batch, CancellationToken ct)`
    - Create `ApplyResult` record with EntityId (Guid) and TargetType (SourceReferenceTargetType)
    - _Requirements: 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 9.1, 9.2, 9.3, 9.5_

  - [x] 1.4 Create `IReviewService` interface in `Nornis.Application/Services/`
    - Define `ListReviewQueueAsync`, `AcceptProposalAsync`, `RejectProposalAsync`, `EditProposalAsync`, `BatchAcceptAsync`, `BatchRejectAsync`
    - _Requirements: 1.1, 2.1, 3.1, 4.1, 5.1, 5.2_

- [x] 2. Extend repository interfaces and implementations
  - [x] 2.1 Add `ListReviewQueueAsync` to `IReviewProposalRepository` and implement in `ReviewProposalRepository`
    - Add `Task<(IReadOnlyList<ReviewProposal> Proposals, bool HasMore)> ListReviewQueueAsync(Guid campaignId, IReadOnlyList<Guid> allowedSourceIds, Guid? filterByBatchId, int limit, CancellationToken ct)`
    - Implement paginated query joining ReviewBatch to filter by campaign and allowed sources, ordered by CreatedAt ascending within batches
    - _Requirements: 1.1, 1.2, 1.8, 1.9, 1.11_

  - [x] 2.2 Add `UpdateCompletedAsync` to `IReviewBatchRepository` and implement in `ReviewBatchRepository`
    - Add `Task UpdateCompletedAsync(Guid id, DateTimeOffset completedAt, CancellationToken ct)`
    - Implement update setting Status=Completed and CompletedAt
    - _Requirements: 8.2_

- [x] 3. Implement ProposalValidator
  - [x] 3.1 Implement `ProposalValidator` in `Nornis.Application/Validation/`
    - Deserialize JSON to appropriate payload record based on ChangeType
    - Validate CreateArtifact: Name required (1–200 chars), Type must be valid ArtifactType enum string
    - Validate UpdateArtifact: at least one field non-null
    - Validate MergeArtifact: SourceArtifactId required (non-empty GUID)
    - Validate AddFact: Predicate required (1–500 chars), Value required (1–4000 chars)
    - Validate UpdateFact: at least one field non-null
    - Validate AddRelationship: ArtifactAId and ArtifactBId required (non-empty GUIDs), Type required (1–200 chars)
    - Validate UpdateRelationship: at least one field non-null
    - Enforce total JSON length ≤ 32,768 characters
    - Return appropriate AppError on validation failure
    - _Requirements: 2.10, 4.3_

  - [x] 3.2 Write unit tests for ProposalValidator
    - Test each ChangeType with valid payloads → success
    - Test CreateArtifact missing Name → error
    - Test CreateArtifact Name >200 chars → error
    - Test CreateArtifact invalid Type string → error
    - Test UpdateArtifact all fields null → error
    - Test MergeArtifact empty SourceArtifactId → error
    - Test AddFact missing Predicate/Value → error
    - Test AddFact Predicate >500 chars, Value >4000 chars → error
    - Test AddRelationship missing ArtifactAId/ArtifactBId/Type → error
    - Test UpdateRelationship all fields null → error
    - Test JSON >32768 chars → error
    - Test malformed JSON → error
    - _Requirements: 2.10, 4.3_

- [x] 4. Implement ProposalApplicator
  - [x] 4.1 Implement `ProposalApplicator` in `Nornis.Application/Application/`
    - Inject IArtifactRepository, IArtifactFactRepository, IArtifactRelationshipRepository, ISourceReferenceRepository, ISourceRepository
    - Implement ChangeType dispatch: CreateArtifact, UpdateArtifact, MergeArtifact, AddFact, UpdateFact, AddRelationship, UpdateRelationship
    - CreateArtifact: create Artifact with fields from payload, CampaignId from batch, Status=Active, resolve visibility with source fallback, update proposal TargetId
    - UpdateArtifact: load existing artifact by TargetId, update non-null fields, set UpdatedAt
    - MergeArtifact: update target artifact fields, reassign facts and relationships from source artifact, remove self-referencing relationships, archive source artifact
    - AddFact: create ArtifactFact with ArtifactId=TargetId and fields from payload
    - UpdateFact: load existing fact by TargetId, update non-null fields, set UpdatedAt
    - AddRelationship: create ArtifactRelationship with fields from payload, CampaignId from batch
    - UpdateRelationship: load existing relationship by TargetId, update non-null fields, set UpdatedAt
    - Create SourceReference for each applied mutation with SourceId from batch, TargetType, TargetId
    - Return ApplyResult with EntityId and TargetType
    - _Requirements: 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7_

  - [x] 4.2 Write unit tests for ProposalApplicator
    - Test CreateArtifact creates artifact with correct fields and updates proposal TargetId
    - Test UpdateArtifact updates only non-null fields and sets UpdatedAt
    - Test MergeArtifact reassigns facts and relationships, removes self-refs, archives source
    - Test AddFact creates fact with correct ArtifactId from TargetId
    - Test UpdateFact updates only specified fields
    - Test AddRelationship creates relationship with correct fields
    - Test UpdateRelationship updates only specified fields
    - Test SourceReference created for each mutation
    - Test visibility defaults to source visibility when not specified in payload
    - Test target not found returns validation error for Update operations
    - Test artifact not found returns validation error for AddRelationship
    - _Requirements: 2.2–2.8, 7.5, 9.1–9.6_

- [x] 5. Checkpoint - Validation and applicator verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement ReviewService (core orchestration)
  - [x] 6.1 Implement `ReviewService` — ListReviewQueue in `Nornis.Application/Services/`
    - Inject IReviewProposalRepository, IReviewBatchRepository, ISourceRepository, IArtifactRepository, IArtifactFactRepository, IArtifactRelationshipRepository, ISourceReferenceRepository, IUnitOfWork, IProposalValidator, IProposalApplicator
    - Implement visibility filtering: GM sees all sources, Player sees only own sources, Observer gets empty list
    - Compute allowedSourceIds based on role and source VisibilityScope
    - Validate FilterByBatchId exists in campaign if provided (return not-found if not)
    - Delegate to repository ListReviewQueueAsync with limit=200
    - Return ReviewQueueResult with proposals and HasMore indicator
    - _Requirements: 1.1–1.12_

  - [x] 6.2 Implement `ReviewService` — AcceptProposalAsync
    - Load proposal by Id, load batch, load source
    - Check visibility (invisible → not-found)
    - Check authorization (Observer → 403, Player without ownership → 403)
    - Handle idempotent case: already Accepted → return existing data
    - Handle conflicting state: Rejected → 409 error
    - Guard: only Pending or Edited allowed
    - Validate ProposedValueJson via IProposalValidator
    - Begin transaction via IUnitOfWork
    - Apply mutation via IProposalApplicator
    - Update proposal: Status=Accepted, ReviewedAt=UtcNow, ReviewedByUserId
    - Commit transaction
    - Update batch lifecycle (Pending→InReview on first review, InReview→Completed when all terminal)
    - Return AcceptProposalResult with CreatedEntityId
    - _Requirements: 2.1–2.12, 6.1–6.7, 7.4, 8.1–8.5, 9.7, 10.1, 10.3, 11.1–11.4_

  - [x] 6.3 Implement `ReviewService` — RejectProposalAsync
    - Load proposal, batch, source
    - Check visibility and authorization (same rules as accept)
    - Handle idempotent case: already Rejected → return existing data
    - Handle conflicting state: Accepted → 409 error
    - Guard: only Pending or Edited allowed
    - Update proposal: Status=Rejected, ReviewedAt=UtcNow, ReviewedByUserId
    - Update batch lifecycle
    - Return RejectProposalResult
    - _Requirements: 3.1–3.6, 6.1–6.7, 7.4, 8.1–8.5, 10.2, 10.3_

  - [x] 6.4 Implement `ReviewService` — EditProposalAsync
    - Load proposal, batch, source
    - Check visibility and authorization
    - Handle conflicting state: Accepted or Rejected → 409 error
    - Guard: only Pending or Edited allowed
    - Validate new ProposedValueJson via IProposalValidator
    - Replace ProposedValueJson on proposal
    - Update proposal: Status=Edited, ReviewedAt=UtcNow, ReviewedByUserId
    - Update batch lifecycle (Pending→InReview on first edit)
    - No knowledge graph mutation
    - Return EditProposalResult
    - _Requirements: 4.1–4.6, 6.1–6.7, 7.4, 8.1_

  - [x] 6.5 Implement `ReviewService` — BatchAcceptAsync and BatchRejectAsync
    - Validate batch size (1–50), deduplicate Ids
    - Process each proposal sequentially in request order
    - For each proposal: load, check visibility/auth, apply accept/reject logic
    - Invisible proposals reported as not-found in failed list
    - Already in matching terminal state → idempotent success (add to succeeded)
    - Conflicting terminal state or other error → add to failed with reason
    - Return BatchOperationResult with succeeded/failed partition
    - _Requirements: 5.1–5.6, 7.6, 10.1, 10.2, 11.3, 11.4_

- [x] 7. Checkpoint - ReviewService core logic verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Implement API layer
  - [x] 8.1 Create request and response DTOs in `Nornis.Api/Contracts/`
    - Create `EditProposalRequest` record with ProposedValueJson string
    - Create `BatchAcceptRequest` record with ProposalIds list
    - Create `BatchRejectRequest` record with ProposalIds list
    - Create `ReviewProposalResponse`, `ReviewQueueResponse`, `AcceptProposalResponse`, `RejectProposalResponse`, `EditProposalResponse`, `BatchOperationResponse`, `BatchFailureItem` records
    - _Requirements: 1.7, 2.1, 3.1, 4.1, 5.1_

  - [x] 8.2 Create `ReviewsController` in `Nornis.Api/Controllers/`
    - Apply `[ApiController]`, `[Route("api/campaigns/{campaignId}/reviews")]`
    - Apply CampaignMemberActionFilter for campaign membership enforcement
    - GET `/proposals` — list review queue, accept optional `batchId` query parameter
    - POST `/proposals/{proposalId}/accept` — accept single proposal
    - POST `/proposals/{proposalId}/reject` — reject single proposal
    - POST `/proposals/{proposalId}/edit` — edit proposal, accept EditProposalRequest body
    - POST `/proposals/batch-accept` — batch accept, accept BatchAcceptRequest body
    - POST `/proposals/batch-reject` — batch reject, accept BatchRejectRequest body
    - Extract CampaignMember from HttpContext, derive userId from JWT claims
    - Map AppResult to appropriate HTTP status codes (200, 400, 403, 404, 409, 500)
    - _Requirements: 1.1, 2.1, 3.1, 4.1, 5.1, 6.5, 6.6, 12.1–12.6_

  - [x] 8.3 Register ReviewService, ProposalValidator, ProposalApplicator in DI container
    - Register IReviewService → ReviewService (scoped)
    - Register IProposalValidator → ProposalValidator (singleton)
    - Register IProposalApplicator → ProposalApplicator (scoped)
    - _Requirements: 2.1, 3.1, 4.1_

- [x] 9. Checkpoint - API layer verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Unit tests for ReviewService
  - [x] 10.1 Write unit tests for ReviewService — ListReviewQueue
    - Test GM sees all pending proposals regardless of source author
    - Test Player sees only proposals from own sources
    - Test Observer gets empty list
    - Test GMOnly source proposals hidden from Players
    - Test Private source proposals hidden from non-creator non-GMs
    - Test PartyVisible proposals visible to authorized reviewers
    - Test ordering: CreatedAt ascending within batches, batches ordered by CreatedAt ascending
    - Test pagination: max 200 returned, HasMore=true when more exist
    - Test FilterByBatchId returns only matching batch proposals
    - Test non-existent BatchId returns not-found
    - Test empty queue returns empty list with no error
    - _Requirements: 1.1–1.12_

  - [x] 10.2 Write unit tests for ReviewService — AcceptProposalAsync
    - Test happy path: Pending → Accepted with correct metadata
    - Test Edited → Accepted applies edited JSON
    - Test idempotent: already Accepted returns success with existing data
    - Test conflicting: Rejected → accept returns 409 error
    - Test invisible proposal returns not-found
    - Test Observer returns 403
    - Test Player unauthorized (different source owner) returns 403
    - Test invalid ProposedValueJson returns validation error
    - Test target entity not found returns validation error
    - Test SourceReference created on acceptance
    - Test batch transitions Pending→InReview on first accept
    - Test batch transitions InReview→Completed when all terminal
    - _Requirements: 2.1–2.12, 6.1–6.4, 7.4, 8.1–8.2, 9.7, 10.1, 10.3, 11.1_

  - [x] 10.3 Write unit tests for ReviewService — RejectProposalAsync
    - Test happy path: Pending → Rejected with correct metadata
    - Test Edited → Rejected succeeds
    - Test no knowledge graph changes on rejection
    - Test idempotent: already Rejected returns success
    - Test conflicting: Accepted → reject returns 409 error
    - Test invisible proposal returns not-found
    - Test batch transitions on first rejection
    - _Requirements: 3.1–3.6, 7.4, 8.1, 10.2, 10.3_

  - [x] 10.4 Write unit tests for ReviewService — EditProposalAsync
    - Test happy path: JSON replaced, Status → Edited
    - Test no knowledge graph mutation on edit
    - Test invalid JSON or >32768 chars returns validation error
    - Test Accepted/Rejected proposals cannot be edited (409)
    - Test edited proposal subsequently accepted applies edited JSON
    - Test batch transitions on first edit
    - _Requirements: 4.1–4.6, 8.1_

  - [x] 10.5 Write unit tests for ReviewService — BatchAcceptAsync and BatchRejectAsync
    - Test each proposal processed individually following single-proposal logic
    - Test partial failure reports correct succeeded/failed partition
    - Test duplicate Ids deduplicated
    - Test batch size <1 or >50 returns validation error
    - Test authorization checked per proposal
    - Test sequential processing in request order
    - Test invisible proposals reported as not-found in failed list
    - Test already-accepted proposals treated as idempotent success in batch accept
    - Test already-rejected proposals treated as idempotent success in batch reject
    - _Requirements: 5.1–5.6, 7.6, 10.1, 10.2, 11.3_

- [x] 11. Checkpoint - Unit test verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Property-based tests for ReviewService
  - [x] 12.1 Set up test infrastructure: in-memory repository fakes and FsCheck generators
    - Create/extend InMemoryReviewProposalRepository with ListReviewQueueAsync
    - Create/extend InMemoryReviewBatchRepository with UpdateCompletedAsync
    - Create/extend InMemoryArtifactRepository, InMemoryArtifactFactRepository, InMemoryArtifactRelationshipRepository, InMemorySourceReferenceRepository
    - Create InMemorySourceRepository for source visibility and ownership lookup
    - Create InMemoryUnitOfWork with configurable transaction failure
    - Create FsCheck generators: ValidCreateArtifactPayload, ValidAddFactPayload, ValidAddRelationshipPayload, InvalidProposedValueJson, ReviewScenario (campaign + sources + batches + proposals + members), CampaignRole, VisibilityScope, ReviewProposalStatus, ReviewChangeType
    - _Requirements: All (test infrastructure)_

  - [x] 12.2 Write property test: Visibility Filtering
    - **Property 1: Visibility Filtering**
    - Generate campaigns with mixed-visibility sources owned by different users; create pending proposals; request queue as GM/Player/Observer; assert GM sees all, Player sees only own-source proposals, Observer sees zero
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 7.1, 7.2, 7.3**

  - [x] 12.3 Write property test: Authorization Enforcement
    - **Property 2: Authorization Enforcement**
    - Generate proposals from mixed-author sources; attempt accept/reject/edit as GM/Player/Observer; assert GM always authorized, Player authorized only for own sources, Observer always denied with 403
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4**

  - [x] 12.4 Write property test: Invisible Proposals Treated as Not-Found
    - **Property 3: Invisible Proposals Treated as Not-Found**
    - Generate proposals from GMOnly/Private sources; attempt operations as unauthorized users; assert not-found error (not forbidden)
    - **Validates: Requirements 3.5, 7.4, 7.6**

  - [x] 12.5 Write property test: Accept Transitions Status and Sets Metadata
    - **Property 4: Accept Transitions Status and Sets Metadata**
    - Generate random Pending/Edited proposals; accept by authorized reviewer; assert Status=Accepted, ReviewedAt set to approximately current UTC, ReviewedByUserId set
    - **Validates: Requirements 2.1**

  - [x] 12.6 Write property test: CreateArtifact Acceptance Creates Correct Artifact
    - **Property 5: CreateArtifact Acceptance Creates Correct Artifact**
    - Generate valid CreateArtifact payloads; accept; assert artifact created with matching Name, Type, Summary, Visibility, Confidence, CampaignId, Status=Active, proposal TargetId updated
    - **Validates: Requirements 2.2, 9.1**

  - [x] 12.7 Write property test: Update Acceptance Updates Existing Entity
    - **Property 6: Update Acceptance Updates Existing Entity**
    - Generate existing artifacts/facts/relationships and Update proposals; accept; assert only specified fields changed, UpdatedAt set, other fields unchanged
    - **Validates: Requirements 2.3, 2.5, 2.7**

  - [x] 12.8 Write property test: Add Acceptance Creates Correct Entity
    - **Property 7: Add Acceptance Creates Correct Entity**
    - Generate valid AddFact and AddRelationship proposals with valid target references; accept; assert entities created with correct field mapping
    - **Validates: Requirements 2.4, 2.6, 9.2, 9.3**

  - [x] 12.9 Write property test: MergeArtifact Reassigns and Archives
    - **Property 8: MergeArtifact Reassigns and Archives**
    - Generate two artifacts with facts and relationships; create merge proposal; accept; assert target updated, facts/relationships reassigned, self-referencing relationships removed, source artifact archived
    - **Validates: Requirements 9.5**

  - [x] 12.10 Write property test: Accept Creates SourceReference
    - **Property 9: Accept Creates SourceReference**
    - Accept proposals of each ChangeType; assert SourceReference created with correct SourceId (from batch), TargetType, TargetId
    - **Validates: Requirements 2.8**

  - [x] 12.11 Write property test: Reject Transitions Without Knowledge Graph Changes
    - **Property 10: Reject Transitions Without Knowledge Graph Changes**
    - Generate random Pending/Edited proposals; reject; assert Status=Rejected, ReviewedAt/ReviewedByUserId set, no Artifact/ArtifactFact/ArtifactRelationship/SourceReference created or modified
    - **Validates: Requirements 3.1, 3.2**

  - [x] 12.12 Write property test: Edit Replaces JSON Without Mutating Knowledge Graph
    - **Property 11: Edit Replaces JSON Without Mutating Knowledge Graph**
    - Generate Pending/Edited proposals with valid new JSON; edit; assert ProposedValueJson replaced, Status=Edited, no knowledge graph entity changes
    - **Validates: Requirements 4.1, 4.6**

  - [x] 12.13 Write property test: Edited Proposals Allow Subsequent Accept or Reject
    - **Property 12: Edited Proposals Allow Subsequent Accept or Reject**
    - Generate edited proposals; accept or reject; assert both operations succeed with correct behavior
    - **Validates: Requirements 4.2**

  - [x] 12.14 Write property test: Batch Processes Each Proposal Correctly
    - **Property 13: Batch Processes Each Proposal Correctly**
    - Generate batch of 1–50 unique pending proposal Ids; batch accept/reject; assert each processed following single-proposal logic in request order
    - **Validates: Requirements 5.1, 5.2, 5.6**

  - [x] 12.15 Write property test: Batch Partial Failure Reports Correct Partitioning
    - **Property 14: Batch Partial Failure Reports Correct Partitioning**
    - Generate batch with mix of valid, unauthorized, non-existent, wrong-status, and invisible proposals; assert succeeded/failed lists correctly partition with accurate error reasons
    - **Validates: Requirements 5.3, 5.5**

  - [x] 12.16 Write property test: First Review Transitions Batch to InReview
    - **Property 15: First Review Transitions Batch to InReview**
    - Generate ReviewBatch in Pending status; review first proposal (accept/reject/edit); assert batch Status transitions to InReview
    - **Validates: Requirements 8.1, 3.6**

  - [x] 12.17 Write property test: All Proposals Terminal Transitions Batch to Completed
    - **Property 16: All Proposals Terminal Transitions Batch to Completed**
    - Generate ReviewBatch in InReview with all-but-one proposals terminal; bring last proposal to terminal; assert batch Status=Completed and CompletedAt set
    - **Validates: Requirements 8.2**

  - [x] 12.18 Write property test: Batch Not Completed While Non-Terminal Proposals Remain
    - **Property 17: Batch Not Completed While Non-Terminal Proposals Remain**
    - Generate batch with some Pending or Edited proposals remaining; assert batch Status is NOT Completed
    - **Validates: Requirements 8.3**

  - [x] 12.19 Write property test: Idempotent Terminal State
    - **Property 18: Idempotent Terminal State**
    - Accept an already-Accepted proposal; assert success with original ReviewedAt/ReviewedByUserId, no new entities; reject an already-Rejected proposal; assert success without state changes
    - **Validates: Requirements 10.1, 10.2**

  - [x] 12.20 Write property test: Cross-State Terminal Transition Error
    - **Property 19: Cross-State Terminal Transition Error**
    - Accept a Rejected proposal; assert error; reject an Accepted proposal; assert error
    - **Validates: Requirements 10.3**

  - [x] 12.21 Write property test: Accepted Entity Visibility Defaults
    - **Property 20: Accepted Entity Visibility Defaults**
    - Generate proposals without visibility in ProposedValueJson; accept; assert entity inherits source VisibilityScope; generate proposals with explicit visibility; assert entity uses specified value
    - **Validates: Requirements 7.5**

  - [x] 12.22 Write property test: Review Queue Ordering
    - **Property 21: Review Queue Ordering**
    - Generate proposals with random timestamps across multiple batches; list queue; assert proposals ordered by CreatedAt ascending within each batch, batches ordered by CreatedAt ascending
    - **Validates: Requirements 1.8**

  - [x] 12.23 Write property test: Review Queue Pagination
    - **Property 22: Review Queue Pagination**
    - Generate >200 matching proposals; list queue; assert exactly 200 returned with HasMore=true; generate ≤200; assert HasMore=false
    - **Validates: Requirements 1.11**

- [x] 13. Checkpoint - Property-based test verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 14. Integration tests
  - [x] 14.1 Write integration tests for ReviewsController authorization and membership
    - Test CampaignMemberActionFilter applied to all review endpoints
    - Test non-member returns 403
    - Test invalid campaignId returns 404
    - Test missing JWT returns 401
    - Test Observer attempting accept/reject/edit returns 403
    - _Requirements: 6.5, 6.6, 12.1–12.6_

  - [x] 14.2 Write integration tests for full review workflow through HTTP
    - Test list queue → accept → verify artifact created with correct fields
    - Test list queue → reject → verify no knowledge graph changes
    - Test list queue → edit → accept edited → verify artifact uses edited JSON
    - Test batch accept returns correct succeeded/failed partition
    - Test batch reject returns correct succeeded/failed partition
    - Test visibility enforcement: invisible proposal returns 404
    - Test idempotent accept returns 200 with existing data
    - Test conflicting state returns 409
    - _Requirements: 1.1, 2.1, 3.1, 4.1, 5.1, 7.4, 10.1, 10.3, 11.1_

- [x] 15. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Existing domain entities (ReviewProposal, ReviewBatch, Source, Artifact, ArtifactFact, ArtifactRelationship, SourceReference), enums (ReviewChangeType, ReviewTargetType, ReviewProposalStatus, ReviewBatchStatus, VisibilityScope, CampaignRole, ArtifactType, ArtifactStatus, SourceReferenceTargetType), and base repository interfaces already exist in Nornis.Domain
- The existing CampaignMemberActionFilter, UserProvisioningMiddleware, and Auth0 JWT middleware are already in place
- The existing AppResult pattern is used for service return types throughout the application layer
- Property tests use FsCheck.NUnit (FsCheck 3.x) with minimum 100 iterations per property
- Property test tag format: `Feature: review-proposal-workflow, Property {number}: {property_text}`
- The ProposalApplicator handles SourceReference creation within the same transaction as entity mutation
- Batch lifecycle updates (Pending→InReview, InReview→Completed) execute outside the individual proposal transaction
- Use realistic test data: Campaign "Black Harbor Investigation", Users Kelda (GM), Tavrin (Player), Jorin (Observer), Artifacts Captain Voss, Black Harbor, Silver Key, Missing Caravan

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.4"] },
    { "id": 1, "tasks": ["2.1", "2.2"] },
    { "id": 2, "tasks": ["3.1"] },
    { "id": 3, "tasks": ["3.2", "4.1"] },
    { "id": 4, "tasks": ["4.2"] },
    { "id": 5, "tasks": ["6.1", "6.2", "6.3", "6.4"] },
    { "id": 6, "tasks": ["6.5"] },
    { "id": 7, "tasks": ["8.1", "8.2"] },
    { "id": 8, "tasks": ["8.3"] },
    { "id": 9, "tasks": ["10.1", "10.2", "10.3", "10.4", "10.5"] },
    { "id": 10, "tasks": ["12.1"] },
    { "id": 11, "tasks": ["12.2", "12.3", "12.4", "12.5", "12.6", "12.7", "12.8", "12.9", "12.10", "12.11", "12.12", "12.13", "12.14", "12.15", "12.16", "12.17", "12.18", "12.19", "12.20", "12.21", "12.22", "12.23"] },
    { "id": 12, "tasks": ["14.1", "14.2"] }
  ]
}
```
