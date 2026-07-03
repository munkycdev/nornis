# Implementation Plan: Async Source Extraction

## Overview

This plan implements the nornis-worker async source extraction service — the background job processor that consumes messages from the `source-extraction` Azure Service Bus queue, calls Azure OpenAI with structured output, and creates ReviewBatch/ReviewProposal records. The implementation follows clean architecture: a thin Worker layer (BackgroundService) delegates to a rich Application layer (`ExtractionService`) which orchestrates idempotency, context assembly, AI invocation, proposal creation, usage tracking, and failure classification. The Infrastructure layer provides the `AzureOpenAiExtractionClient` and `ServiceBusExtractionProcessor`.

Existing domain entities (`Source`, `ReviewBatch`, `ReviewProposal`, `SourceReference`, `AiUsageRecord`, `Artifact`, `ArtifactFact`), enums, and repository interfaces are already in place. This plan adds the extraction-specific application service, AI client interface and implementation, worker host configuration, extended repository methods, and comprehensive tests.

## Tasks

- [x] 1. Define configuration models and AI contracts
  - [x] 1.1 Create `ExtractionOptions` and `ModelPricing` in `Nornis.Application/Configuration/`
    - Define `ExtractionOptions` class with AiModel, AiEndpoint, AiTimeoutSeconds (default 60), MaxArtifactContextCount (default 50), MaxFactsPerArtifact (default 20), MaxParseRetryAttempts (default 2), and `Dictionary<string, ModelPricing>` for ModelPricing
    - Define `ModelPricing` class with InputPerMillionTokensUsd and OutputPerMillionTokensUsd as decimal
    - _Requirements: 12.1, 12.2, 12.3_

  - [x] 1.2 Create AI request/response models in `Nornis.Application/Ai/`
    - Create `ExtractionRequest` with SourceBody, SourceTitle, SourceType, SourceVisibility, OccurredAt (nullable), ExistingArtifacts list
    - Create `ArtifactContext` with Id, Name, Type, Summary (nullable), Facts list
    - Create `FactContext` with Predicate, Value
    - Create `AiExtractionResponse` with Proposals list, InputTokens, OutputTokens, TotalTokens, DurationMs, Model
    - Create `ExtractionProposal` with ChangeType, TargetType, TargetId (nullable Guid), ProposedValue (object), Rationale, Confidence (nullable decimal)
    - _Requirements: 3.3, 5.1, 5.2_

  - [x] 1.3 Create `IAiExtractionClient` interface in `Nornis.Application/Ai/`
    - Define `Task<AiExtractionResponse> ExtractAsync(ExtractionRequest request, CancellationToken ct)`
    - _Requirements: 5.1_

  - [x] 1.4 Create `ExtractionOutcome` model in `Nornis.Application/Models/`
    - Define `OutcomeType` enum: Success, Skipped, TransientFailure, NonTransientFailure
    - Define `ExtractionOutcome` class with Type, ErrorCategory (nullable), ErrorMessage (nullable), ReviewBatchId (nullable), ProposalCount
    - Add static factory methods: `Succeeded`, `SkippedIdempotent`, `Transient`, `NonTransient`
    - _Requirements: 1.2, 1.4, 1.7, 10.1, 10.2_

  - [x] 1.5 Create `ErrorCategories` static class in `Nornis.Application/Ai/`
    - Define constants: TransientError, SourceNotFound, EmptySourceBody, ValidationFailure, AiCallFailure, ParseFailure, Timeout
    - _Requirements: 10.5_

- [x] 2. Extend repository interfaces and implementations
  - [x] 2.1 Add `GetBySourceIdAsync` to `IReviewBatchRepository` and implement in `ReviewBatchRepository`
    - Add `Task<ReviewBatch?> GetBySourceIdAsync(Guid sourceId, CancellationToken cancellationToken = default)` to interface
    - Implement query filtering by SourceId, returning first match in Pending/InReview/Completed status
    - _Requirements: 2.1_

  - [x] 2.2 Add context query methods to `IArtifactRepository` and implement in `ArtifactRepository`
    - Add `Task<IReadOnlyList<Artifact>> ListRecentByCampaignAsync(Guid campaignId, IReadOnlyList<VisibilityScope> allowedVisibilities, int maxCount, CancellationToken ct)` — return artifacts ordered by UpdatedAt descending, filtered by visibility
    - Add `Task<IReadOnlyList<Artifact>> ListByNamesInTextAsync(Guid campaignId, string text, IReadOnlyList<VisibilityScope> allowedVisibilities, CancellationToken ct)` — return artifacts whose Name appears in the text (case-insensitive whole-word match)
    - _Requirements: 4.1, 4.2, 4.5_

  - [x] 2.3 Add `ListByArtifactIdsAsync` to `IArtifactFactRepository` and implement in `ArtifactFactRepository`
    - Add `Task<IReadOnlyList<ArtifactFact>> ListByArtifactIdsAsync(IReadOnlyList<Guid> artifactIds, int maxPerArtifact, CancellationToken ct)` — return up to maxPerArtifact facts per artifact, ordered by UpdatedAt descending
    - _Requirements: 4.4_

- [x] 3. Implement ExtractionService (core orchestration)
  - [x] 3.1 Create `IExtractionService` interface in `Nornis.Application/Services/`
    - Define `Task<ExtractionOutcome> ProcessExtractionAsync(Guid sourceId, Guid campaignId, CancellationToken ct)`
    - _Requirements: 1.1, 1.2_

  - [x] 3.2 Implement `ExtractionService` in `Nornis.Application/Services/`
    - Inject ISourceRepository, IReviewBatchRepository, IReviewProposalRepository, ISourceReferenceRepository, IAiUsageRecordRepository, IArtifactRepository, IArtifactFactRepository, IAiExtractionClient, IOptions\<ExtractionOptions\>, ILogger
    - Implement idempotency check: verify source exists, is in Queued status, and has no existing ReviewBatch in Pending/InReview/Completed
    - Implement status transition: Queued → Processing
    - Implement empty body short-circuit: null/empty/whitespace body → create Completed ReviewBatch with zero proposals, mark source Processed
    - Implement context assembly: get allowed visibility scopes, load recent artifacts, load name-matched artifacts, merge/dedup (name-matched first), limit to MaxArtifactContextCount, load facts per artifact (limited to MaxFactsPerArtifact)
    - Implement AI invocation: build ExtractionRequest from source fields and context, call IAiExtractionClient.ExtractAsync
    - Implement parse retry: retry AI call up to MaxParseRetryAttempts on validation failures
    - Implement response validation: verify proposals conform to schema constraints (max 50, field lengths, enum values, confidence range)
    - Implement atomic proposal creation: create ReviewBatch + ReviewProposals + SourceReferences in single transaction
    - Implement visibility enforcement: override ProposedValueJson visibility to match source VisibilityScope
    - Implement usage tracking: create AiUsageRecord with correct fields regardless of success/failure
    - Implement cost calculation: (InputTokens × InputRate / 1,000,000) + (OutputTokens × OutputRate / 1,000,000)
    - Implement failure classification: timeout/network → TransientFailure; parse/validation/not-found → NonTransientFailure
    - Return appropriate ExtractionOutcome
    - _Requirements: 1.1–1.7, 2.1–2.3, 3.1–3.3, 4.1–4.7, 5.5, 6.1–6.4, 7.1–7.7, 8.1–8.2, 10.1–10.3_

- [x] 4. Checkpoint - Application layer core verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement Infrastructure AI client
  - [x] 5.1 Create `AzureOpenAiExtractionClient` in `Nornis.Infrastructure/Ai/`
    - Implement `IAiExtractionClient` using Azure OpenAI SDK (ChatClient)
    - Build system prompt with visibility instructions, truth state defaults, and structured output schema reference
    - Build user message from ExtractionRequest (source content + artifact context)
    - Send chat completion with structured output JSON schema (proposals array with changeType, targetType, targetId, proposedValue, rationale, confidence)
    - Enforce configurable timeout via CancellationTokenSource
    - Parse structured output response into AiExtractionResponse with token usage from API response
    - Handle timeout (OperationCanceledException) → return error indicating Timeout category
    - Handle HttpRequestException with 429/503 status → return error indicating TransientError
    - Handle other HttpRequestException → return error indicating AiCallFailure
    - Handle JSON parse failures → return error indicating ParseFailure
    - Handle empty proposals array as successful response
    - _Requirements: 5.1–5.7, 9.1–9.4_

  - [x] 5.2 Write unit tests for `AzureOpenAiExtractionClient`
    - Test valid JSON response → correct ExtractionProposal objects with all fields mapped
    - Test missing required fields in response → ParseFailure error
    - Test wrong enum values in response → ParseFailure error
    - Test rationale exceeding 500 chars → ParseFailure error
    - Test confidence outside 0.0–1.0 → ParseFailure error
    - Test >50 proposals → ParseFailure error
    - Test empty proposals array → success with empty list
    - Test timeout → Timeout error category
    - Test 429 response → TransientError category
    - Test 503 response → TransientError category
    - Test network exception → appropriate error
    - Test system prompt includes visibility and truth state instructions
    - _Requirements: 5.2, 5.4, 5.5, 5.6, 5.7_

- [x] 6. Implement Worker layer
  - [x] 6.1 Create `WorkerOptions` in `Nornis.Worker/Configuration/`
    - Define strongly-typed configuration for ServiceBus: ConnectionString, QueueName (default "source-extraction"), MaxConcurrentCalls (default 1), PrefetchCount (default 0), MaxAutoLockRenewalDuration (default 5 minutes)
    - _Requirements: 12.4_

  - [x] 6.2 Create `ServiceBusExtractionProcessor` in `Nornis.Infrastructure/Messaging/`
    - Wrap Azure Service Bus `ServiceBusProcessor` for message reception
    - Configure peek-lock mode, max concurrent calls, prefetch, and lock renewal duration from WorkerOptions
    - Expose event handlers for ProcessMessageAsync and ProcessErrorAsync
    - Implement StartProcessingAsync and StopProcessingAsync for BackgroundService lifecycle
    - _Requirements: 1.5, 1.8_

  - [x] 6.3 Create `ExtractionWorker` BackgroundService in `Nornis.Worker/`
    - Replace existing `Worker.cs` with `ExtractionWorker.cs`
    - On message received: deserialize ExtractionMessage (SourceId, CampaignId)
    - Call `IExtractionService.ProcessExtractionAsync`
    - On Success/Skipped/NonTransientFailure → complete message
    - On TransientFailure → abandon message (redelivery)
    - Emit structured logs with CorrelationId, SourceId, CampaignId, outcome, duration
    - Handle deserialization failures as non-transient (complete message, log error)
    - _Requirements: 1.1–1.8, 10.1–10.5_

  - [x] 6.4 Update `Program.cs` in `Nornis.Worker/` to wire DI and configuration
    - Register IExtractionService → ExtractionService
    - Register IAiExtractionClient → AzureOpenAiExtractionClient
    - Register ServiceBusExtractionProcessor
    - Bind ExtractionOptions from configuration section "Extraction"
    - Bind WorkerOptions from configuration section "ServiceBus"
    - Configure Azure OpenAI client (endpoint + key or managed identity)
    - Configure Azure Service Bus client (connection string or managed identity)
    - Register repository implementations (ISourceRepository, IReviewBatchRepository, IReviewProposalRepository, ISourceReferenceRepository, IAiUsageRecordRepository, IArtifactRepository, IArtifactFactRepository)
    - Register ExtractionWorker as hosted service
    - Add configuration validation: fail fast if required settings missing
    - _Requirements: 12.1–12.5, 11.4_

- [x] 7. Checkpoint - Worker layer verification
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 8. Property-based tests for ExtractionService
  - [x] 8.1 Set up test infrastructure: in-memory repository fakes and FakeAiExtractionClient
    - Create InMemorySourceRepository (extended with ProcessingStatus transitions)
    - Create InMemoryReviewBatchRepository with GetBySourceIdAsync
    - Create InMemoryReviewProposalRepository
    - Create InMemorySourceReferenceRepository
    - Create InMemoryAiUsageRecordRepository
    - Create InMemoryArtifactRepository with ListRecentByCampaignAsync and ListByNamesInTextAsync
    - Create InMemoryArtifactFactRepository with ListByArtifactIdsAsync
    - Create FakeAiExtractionClient: configurable to return success, throw transient errors, or return invalid responses; records all requests
    - Create FsCheck generators: ValidSourceBody, EmptySourceBody, ExtractionProposalGen, InvalidExtractionResponse, ArtifactWithFacts, TokenCounts, ModelPricingGen, SourceVisibilityScenario
    - _Requirements: All (test infrastructure)_

  - [-] 8.2 Write property test: Successful Extraction State Transitions
    - **Property 1: Successful Extraction State Transitions**
    - Generate random sources in Queued status with non-empty bodies; configure fake AI client to return valid 1–50 proposals; assert source ends at Processed status
    - **Validates: Requirements 1.1, 1.2**

  - [-] 8.3 Write property test: Non-Queued Sources and Existing Batches Are Skipped
    - **Property 2: Non-Queued Sources and Existing Batches Are Skipped**
    - Generate sources in all non-Queued statuses OR with existing ReviewBatches in Pending/InReview/Completed; assert Skipped outcome with no new records
    - **Validates: Requirements 1.4, 2.1, 2.2**

  - [-] 8.4 Write property test: Non-Transient Failures Transition Source to Failed
    - **Property 3: Non-Transient Failures Transition Source to Failed**
    - Generate sources in Queued status; configure fake AI client to return parse failures after retries exhausted; assert source transitions to Failed
    - **Validates: Requirements 1.7, 10.1, 10.3**

  - [-] 8.5 Write property test: Transient Failures Leave Source and Batch Unchanged
    - **Property 4: Transient Failures Leave Source and Batch Unchanged**
    - Generate sources in Queued status; configure fake AI client to throw timeout/network exceptions; assert source status is NOT Failed, outcome is TransientFailure
    - **Validates: Requirements 10.2**

  - [-] 8.6 Write property test: Empty Body Short-Circuits to Completed Batch
    - **Property 5: Empty Body Short-Circuits to Completed Batch**
    - Generate sources with null, empty, or whitespace-only bodies; assert no AI call, ReviewBatch Status=Completed, zero proposals, source=Processed
    - **Validates: Requirements 3.2**

  - [x] 8.7 Write property test: Source Fields Correctly Mapped to AI Request
    - **Property 6: Source Fields Correctly Mapped to AI Request**
    - Generate random sources with various field combinations; assert ExtractionRequest passed to fake AI client contains matching Body, Title, Type, Visibility, OccurredAt
    - **Validates: Requirements 3.3**

  - [x] 8.8 Write property test: Context Assembly Merge, Dedup, Ordering, and Limit
    - **Property 7: Context Assembly Merge, Dedup, Ordering, and Limit**
    - Generate campaigns with N > MaxArtifactContextCount artifacts (some name-matched, some recent, some overlapping); assert context ≤ MaxCount, name-matched first, no duplicates
    - **Validates: Requirements 4.1, 4.2, 4.3**

  - [x] 8.9 Write property test: Context Payload Respects Facts Limit
    - **Property 8: Context Payload Respects Facts Limit**
    - Generate artifacts with >20 facts; assert context includes exactly MaxFactsPerArtifact facts per artifact ordered by UpdatedAt descending
    - **Validates: Requirements 4.4**

  - [x] 8.10 Write property test: Context Assembly Respects Visibility Scope
    - **Property 9: Context Assembly Respects Visibility Scope**
    - Generate campaigns with mixed-visibility artifacts and sources of each visibility; assert only permitted artifacts appear in context
    - **Validates: Requirements 4.5**

  - [x] 8.11 Write property test: Invalid AI Responses Are Treated as Failures
    - **Property 10: Invalid AI Responses Are Treated as Failures**
    - Generate random invalid JSON structures (missing fields, wrong enum values, >500 char rationale, confidence out of range, >50 proposals); assert non-transient failure, no batch created
    - **Validates: Requirements 5.5, 7.6**

  - [x] 8.12 Write property test: AiUsageRecord Always Created
    - **Property 11: AiUsageRecord Always Created**
    - Generate mixed success/failure scenarios for sources that pass idempotency and have non-empty body; assert AiUsageRecord exists with correct CampaignId, SourceId, OperationType, Model, DurationMs ≥ 0
    - **Validates: Requirements 6.1, 6.3**

  - [x] 8.13 Write property test: Cost Calculation Correctness
    - **Property 12: Cost Calculation Correctness**
    - Generate random (inputTokens, outputTokens, inputRate, outputRate) tuples; assert EstimatedCostUsd = (input × inputRate / 1,000,000) + (output × outputRate / 1,000,000)
    - **Validates: Requirements 6.2**

  - [x] 8.14 Write property test: Extraction Output Record Creation
    - **Property 13: Extraction Output Record Creation**
    - Generate valid AI responses with 1–50 proposals; assert exactly 1 ReviewBatch + N ReviewProposals + N SourceReferences with correct field mapping
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4**

  - [x] 8.15 Write property test: Proposal Visibility Always Matches Source Visibility
    - **Property 14: Proposal Visibility Always Matches Source Visibility**
    - Generate sources of each VisibilityScope and AI responses with mismatched visibilities; assert all persisted proposal visibilities match source
    - **Validates: Requirements 8.1, 8.2**

- [x] 9. Unit tests for ExtractionService
  - [x] 9.1 Write unit tests for ExtractionService idempotency and state transitions
    - Test source not found → NonTransientFailure with SourceNotFound
    - Test source in Processing/Processed/Failed status → Skipped outcome
    - Test source with existing ReviewBatch in Pending/InReview/Completed → Skipped outcome
    - Test successful flow: Queued → Processing → Processed
    - Test non-transient failure: Queued → Processing → Failed
    - Test transient failure: source stays at Processing
    - _Requirements: 1.1–1.7, 2.1–2.3_

  - [x] 9.2 Write unit tests for ExtractionService context assembly
    - Test recent artifacts limited to MaxArtifactContextCount
    - Test name-matched artifacts prioritized first in merged list
    - Test deduplication of overlapping artifacts
    - Test visibility filtering for Private/GMOnly/PartyVisible sources
    - Test facts limited to MaxFactsPerArtifact per artifact
    - Test empty campaign (no artifacts) proceeds without error
    - Test null source body skips name-matching
    - _Requirements: 4.1–4.7_

  - [x] 9.3 Write unit tests for ExtractionService proposal creation and visibility enforcement
    - Test ReviewBatch created with correct CampaignId, SourceId, Status=Pending, CreatedAt
    - Test one ReviewProposal per AI proposal with correct field mapping
    - Test one SourceReference per ReviewProposal
    - Test visibility enforcement overwrites ProposedValueJson visibility
    - Test zero proposals → ReviewBatch with Status=Completed
    - Test atomic rollback on DB failure
    - _Requirements: 7.1–7.7, 8.1–8.2_

  - [x] 9.4 Write unit tests for ExtractionService usage tracking and cost calculation
    - Test AiUsageRecord created on success with Succeeded=true
    - Test AiUsageRecord created on failure with Succeeded=false and ErrorCode
    - Test cost calculation: (InputTokens × InputRate / 1,000,000) + (OutputTokens × OutputRate / 1,000,000)
    - Test token counts zero when unavailable on failure
    - Test ReviewBatchId set on AiUsageRecord after batch creation
    - _Requirements: 6.1–6.4_

- [x] 10. Worker and integration tests
  - [x] 10.1 Write unit tests for ExtractionWorker message handling
    - Test Success outcome → message completed
    - Test Skipped outcome → message completed
    - Test NonTransientFailure → message completed
    - Test TransientFailure → message abandoned
    - Test deserialization failure → message completed, error logged
    - Test structured logging includes CorrelationId, SourceId, CampaignId
    - _Requirements: 1.1–1.8, 10.1–10.5_

  - [x] 10.2 Write unit tests for ServiceBusExtractionProcessor configuration
    - Test peek-lock mode configured
    - Test MaxConcurrentCalls from options
    - Test MaxAutoLockRenewalDuration from options
    - Test StartProcessingAsync and StopProcessingAsync lifecycle
    - _Requirements: 1.5, 12.4_

  - [x] 10.3 Write integration test for configuration validation
    - Test missing AiModel → fail fast with clear error
    - Test missing AiEndpoint → fail fast with clear error
    - Test missing ServiceBus ConnectionString → fail fast with clear error
    - _Requirements: 12.5_

- [x] 11. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Existing domain entities (Source, ReviewBatch, ReviewProposal, SourceReference, AiUsageRecord, Artifact, ArtifactFact), enums (SourceProcessingStatus, ReviewBatchStatus, ReviewChangeType, ReviewTargetType, VisibilityScope, AiOperationType), and base repository interfaces already exist in Nornis.Domain
- The existing `IExtractionQueueClient` and `ServiceBusExtractionQueueClient` handle message sending (API side); this plan implements the message reception side
- Property tests use FsCheck.NUnit (FsCheck 3.x) with minimum 100 iterations per property
- The `Worker.cs` placeholder will be replaced by `ExtractionWorker.cs`
- AiUsageRecord is created outside the proposal transaction so it persists even on rollback
- The worker does NOT implement dead-letter processing — that is an operational concern handled by monitoring
- Checkpoints ensure incremental validation before progressing to the next layer

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.4", "1.5"] },
    { "id": 1, "tasks": ["2.1", "2.2", "2.3"] },
    { "id": 2, "tasks": ["3.1"] },
    { "id": 3, "tasks": ["3.2"] },
    { "id": 4, "tasks": ["5.1", "6.1"] },
    { "id": 5, "tasks": ["5.2", "6.2", "6.3"] },
    { "id": 6, "tasks": ["6.4"] },
    { "id": 7, "tasks": ["8.1"] },
    { "id": 8, "tasks": ["8.2", "8.3", "8.4", "8.5", "8.6", "8.7", "8.8", "8.9", "8.10", "8.11", "8.12", "8.13", "8.14", "8.15"] },
    { "id": 9, "tasks": ["9.1", "9.2", "9.3", "9.4"] },
    { "id": 10, "tasks": ["10.1", "10.2", "10.3"] }
  ]
}
```
