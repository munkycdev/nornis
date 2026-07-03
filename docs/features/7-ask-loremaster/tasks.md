# Implementation Plan: Ask the Loremaster

## Overview

This plan implements the "Ask the Loremaster" conversational AI endpoint — a feature that allows campaign members to ask questions about their campaign and receive AI-generated answers grounded in accepted campaign knowledge. The implementation follows clean architecture: `LoremasterController` in the API layer delegates to `LoremasterService` in the Application layer, which orchestrates knowledge retrieval via `IKnowledgeRetriever`, prompt construction, AI invocation via `ILoremasterAiClient`, citation parsing, confidence calculation, and usage tracking.

Key components:
- **API**: `LoremasterController`, request/response DTOs
- **Application**: `ILoremasterService`/`LoremasterService`, `IKnowledgeRetriever`, `ILoremasterAiClient`, knowledge models, prompt builder, citation parser, confidence calculator
- **Infrastructure**: `KeywordKnowledgeRetriever` (MVP SQL-based retrieval), `AzureOpenAiLoremasterClient`, `FakeLoremasterAiClient`

Existing domain entities (Artifact, ArtifactFact, ArtifactRelationship, SourceReference, AiUsageRecord), enums (VisibilityScope, CampaignRole, TruthState, AiOperationType), repository interfaces, and the CampaignMemberActionFilter are already in place.

## Tasks

- [x] 1. Define application models, interfaces, and configuration
  - [x] 1.1 Create knowledge retrieval models in `Nornis.Application/Knowledge/`
    - Create `KnowledgeContext` class with Artifacts, Facts, Relationships, SourceReferences lists
    - Create `KnowledgeArtifact` class with Id, Name, Type, Summary, ReferenceId
    - Create `KnowledgeFact` class with Id, ArtifactId, Predicate, Value, TruthState, ReferenceId
    - Create `KnowledgeRelationship` class with Id, ArtifactAId, ArtifactBId, Type, Description, TruthState, ReferenceId
    - Create `KnowledgeSourceReference` class with Id, SourceId, TargetId, Quote, ReferenceId
    - Create `IKnowledgeRetriever` interface with `RetrieveAsync(question, campaignId, userId, role, ct)`
    - Create `Citation` class with ReferenceId, Type, DisplayName, ArtifactId, FactId, RelationshipId, SourceId
    - Create `CitationType` enum (Artifact, Fact, Relationship, Source)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.6, 7.1, 7.2_

  - [x] 1.2 Create AI client models and interface in `Nornis.Application/Ai/`
    - Create `ILoremasterAiClient` interface with `AskAsync(LoremasterAiRequest, CancellationToken)`
    - Create `LoremasterAiRequest` class with SystemPrompt, UserMessage, Model, TimeoutSeconds
    - Create `LoremasterAiResponse` class with AnswerText, InputTokens, OutputTokens, TotalTokens, DurationMs, Model
    - _Requirements: 5.2, 5.5, 9.1, 9.2_

  - [x] 1.3 Create service models and interface in `Nornis.Application/Services/` and `Nornis.Application/Models/`
    - Create `ILoremasterService` interface with `AskAsync(AskLoremasterCommand, CancellationToken)`
    - Create `AskLoremasterCommand` record with CampaignId, Question, UserId, UserRole, ConversationContext
    - Create `LoremasterAnswer` class with AnswerText, Citations, Confidence, Caveats
    - Create `ConfidenceLevel` enum (High, Medium, Low)
    - _Requirements: 2.1, 8.1, 8.4_

  - [x] 1.4 Create `LoremasterOptions` configuration class in `Nornis.Application/Configuration/`
    - Define AiModel, AiEndpoint, AiTimeoutSeconds, MaxRetrievalCount, MaxFactsPerArtifact, MaxContextTokens, MaxQuestionLength, ModelPricing dictionary
    - _Requirements: 2.2, 4.5, 9.4_

- [x] 2. Extend repository interfaces and implementations
  - [x] 2.1 Add `ListByArtifactIdsAsync` to `IArtifactRelationshipRepository` and implement
    - Add method signature: `Task<IReadOnlyList<ArtifactRelationship>> ListByArtifactIdsAsync(IReadOnlyList<Guid> artifactIds, IReadOnlyList<VisibilityScope> allowedVisibilities, CancellationToken ct)`
    - Implement in `ArtifactRelationshipRepository` with EF Core query filtering by ArtifactAId or ArtifactBId and allowed visibilities
    - _Requirements: 4.3_

  - [x] 2.2 Add `ListByTargetIdsAsync` to `ISourceReferenceRepository` and implement
    - Add method signature: `Task<IReadOnlyList<SourceReference>> ListByTargetIdsAsync(IReadOnlyList<Guid> targetIds, CancellationToken ct)`
    - Implement in `SourceReferenceRepository` with EF Core query filtering by TargetId
    - _Requirements: 4.4_

- [x] 3. Implement KeywordKnowledgeRetriever
  - [x] 3.1 Implement `KeywordKnowledgeRetriever` in `Nornis.Infrastructure/Knowledge/`
    - Inject IArtifactRepository, IArtifactFactRepository, IArtifactRelationshipRepository, ISourceReferenceRepository, IOptions<LoremasterOptions>
    - Implement visibility scope resolution: GM → PartyVisible + GMOnly + own Private; Player → PartyVisible + own Private; Observer → PartyVisible only
    - Implement name-matched artifact retrieval using `ListByNamesInTextAsync`
    - Implement recent artifact retrieval using `ListRecentByCampaignAsync`
    - Merge and deduplicate artifacts (name-matched first, then recent), cap at MaxRetrievalCount
    - Load facts via `ListByArtifactIdsAsync` filtered by visibility
    - Load relationships via `ListByArtifactIdsAsync` filtered by visibility
    - Load source references via `ListByTargetIdsAsync` for fact and relationship IDs
    - Map domain entities to Knowledge models with stable ReferenceId generation
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 3.2 Write property test: Visibility Filter Correctness
    - **Property 2: Visibility Filter Correctness**
    - Generate campaigns with mixed-visibility artifacts, facts, and relationships owned by different users; retrieve as GM/Player/Observer; assert correct visibility filtering per role and ownership
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4**

  - [x] 3.3 Write property test: Keyword-Based Artifact Retrieval
    - **Property 3: Keyword-Based Artifact Retrieval**
    - Generate artifacts with various names and questions containing those names; assert matching artifacts appear in retrieved context when visibility permits
    - **Validates: Requirements 4.1**

  - [x] 3.4 Write property test: Retrieved Knowledge Respects Visibility
    - **Property 4: Retrieved Knowledge Respects Visibility**
    - Generate artifacts with associated facts and relationships at various visibilities; retrieve for each role; assert no facts or relationships with disallowed visibility appear
    - **Validates: Requirements 4.2, 4.3**

  - [x] 3.5 Write property test: Retrieval Count Cap
    - **Property 5: Retrieval Count Cap**
    - Generate campaigns with artifact count exceeding MaxRetrievalCount; retrieve; assert at most MaxRetrievalCount artifacts returned with no duplicates
    - **Validates: Requirements 4.5**

- [x] 4. Checkpoint - Knowledge retrieval verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement LoremasterService core logic
  - [x] 5.1 Implement input validation and confidence calculation in `LoremasterService`
    - Validate question: reject empty/whitespace or over MaxQuestionLength characters with validation error
    - Implement `DetermineConfidence(KnowledgeContext)`: Low when no artifacts; Medium when ≥1 confirmed/likely fact or ≥2 total facts; High when ≥3 confirmed/likely facts + relationships + source references
    - _Requirements: 2.2, 8.4_

  - [x] 5.2 Implement prompt construction in `LoremasterService`
    - Build system prompt with Loremaster persona, grounding rules, citation format `[ref:ID]`, truth state handling, anti-hallucination instructions
    - Format knowledge context block with artifacts (name, type, summary), facts (with truth state labels for Rumor/Disputed), relationships, and source references — each with stable reference IDs
    - Combine system prompt + knowledge context + user question into `LoremasterAiRequest`
    - _Requirements: 5.1, 5.3, 5.4, 6.1, 6.2, 6.3_

  - [x] 5.3 Implement citation parsing in `LoremasterService`
    - Extract `[ref:ID]` markers via regex from AI response text
    - Map each ID to known artifacts, facts, relationships, or source references from the knowledge context
    - Create Citation objects for valid references
    - Silently drop markers referencing unknown IDs
    - _Requirements: 7.1, 7.2, 7.3_

  - [x] 5.4 Implement `AskAsync` orchestration in `LoremasterService`
    - Inject IKnowledgeRetriever, ILoremasterAiClient, IAiUsageRecordRepository, IOptions<LoremasterOptions>
    - Validate input → retrieve knowledge → build prompt → calculate confidence → call AI → parse citations → assemble caveats → create AiUsageRecord → return LoremasterAnswer
    - Handle empty knowledge context: still call AI but set Low confidence and add caveat
    - On AI success: create AiUsageRecord with Succeeded=true, calculate EstimatedCostUsd
    - On AI failure: create AiUsageRecord with Succeeded=false, propagate appropriate error
    - Pass cancellation token through to retriever and AI client
    - _Requirements: 2.1, 2.2, 2.3, 5.1, 5.2, 5.5, 8.1, 8.2, 8.3, 9.1, 9.2, 9.3, 9.4, 10.1, 10.2, 10.3, 10.4_

  - [x] 5.5 Write property test: Invalid Questions Are Rejected
    - **Property 1: Invalid Questions Are Rejected**
    - Generate empty, whitespace-only, and over-2000-character strings; call AskAsync; assert validation error returned without AI client or knowledge retriever invocation
    - **Validates: Requirements 2.2**

  - [x] 5.6 Write property test: Prompt Contains Question and Context
    - **Property 6: Prompt Contains Question and Context**
    - Generate valid questions and non-empty knowledge contexts; invoke prompt builder; assert prompt contains original question text and at least one artifact name
    - **Validates: Requirements 5.1**

  - [x] 5.7 Write property test: Truth State Qualification in Prompt
    - **Property 7: Truth State Qualification in Prompt**
    - Generate knowledge contexts with Rumor/Disputed facts; invoke prompt builder; assert truth state labels appear alongside those items in the context block
    - **Validates: Requirements 6.2**

  - [x] 5.8 Write property test: Citation Parsing Produces Only Valid References
    - **Property 8: Citation Parsing Produces Only Valid References**
    - Generate AI responses with mix of valid and invalid `[ref:ID]` markers; parse citations; assert only citations matching known context IDs are returned
    - **Validates: Requirements 7.1, 7.3**

  - [x] 5.9 Write property test: Confidence Indicator Determination
    - **Property 9: Confidence Indicator Determination**
    - Generate various KnowledgeContext combinations; compute confidence; assert Low when no artifacts, Medium when ≥1 confirmed/likely or ≥2 total facts, High when ≥3 confirmed/likely + relationships + source references
    - **Validates: Requirements 8.4**

  - [x] 5.10 Write property test: AiUsageRecord Always Created
    - **Property 10: AiUsageRecord Always Created**
    - Generate valid questions; mock AI client to succeed and fail; assert AiUsageRecord created with correct OperationType, CampaignId, UserId, token counts, duration, and Succeeded flag in both cases
    - **Validates: Requirements 9.1, 9.2, 9.3**

  - [x] 5.11 Write property test: Cost Calculation Correctness
    - **Property 11: Cost Calculation Correctness**
    - Generate AI responses with various InputTokens/OutputTokens and configured ModelPricing; calculate cost; assert EstimatedCostUsd equals `(InputTokens × InputPerMillionTokensUsd / 1_000_000) + (OutputTokens × OutputPerMillionTokensUsd / 1_000_000)`
    - **Validates: Requirements 9.4**

- [x] 6. Checkpoint - LoremasterService core logic verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Implement Infrastructure AI client
  - [x] 7.1 Implement `AzureOpenAiLoremasterClient` in `Nornis.Infrastructure/Ai/`
    - Inject Azure OpenAI SDK client and IOptions<LoremasterOptions>
    - Implement `AskAsync`: send chat completion with system prompt and user message, respect timeout, return LoremasterAiResponse with token counts and duration
    - Handle timeout: throw appropriate exception for service to catch
    - Handle rate limiting (429): throw appropriate exception for service to catch
    - Handle service errors (5xx): throw appropriate exception for service to catch
    - _Requirements: 5.2, 5.5, 10.1, 10.2_

  - [x] 7.2 Implement `FakeLoremasterAiClient` in `Nornis.Infrastructure/Ai/`
    - Return configurable canned responses for testing
    - Support configurable failure modes (timeout, rate limit, service error)
    - Include realistic token count simulation
    - _Requirements: 5.2 (testing support)_

- [x] 8. Implement API layer
  - [x] 8.1 Create request and response DTOs in `Nornis.Api/Contracts/`
    - Create `AskLoremasterRequest` record with Question and optional ConversationContext
    - Create `AskAnswerResponse` record with Answer, Citations, Confidence, Caveats
    - Create `CitationResponse` record with ReferenceId, Type, DisplayName, ArtifactId, FactId, RelationshipId, SourceId
    - _Requirements: 2.1, 2.3, 8.1_

  - [x] 8.2 Create `LoremasterController` in `Nornis.Api/Controllers/`
    - Apply `[ApiController]`, `[Route("api/campaigns/{campaignId:guid}/ask")]`
    - Apply `[ServiceFilter(typeof(CampaignMemberActionFilter))]` for membership enforcement
    - Implement POST endpoint: extract user from HttpContext, extract CampaignMember, build AskLoremasterCommand, call service, map result to response
    - Map errors: validation → 400, rate limit → 429, service unavailable → 503, internal → 500
    - Never expose stack traces, prompt content, or retrieved knowledge in error responses
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 8.1, 8.2, 8.3, 10.1, 10.2, 10.3, 10.4_

  - [x] 8.3 Register services in DI container and add configuration
    - Register ILoremasterService → LoremasterService (scoped)
    - Register IKnowledgeRetriever → KeywordKnowledgeRetriever (scoped)
    - Register ILoremasterAiClient → AzureOpenAiLoremasterClient (scoped)
    - Bind `LoremasterOptions` from configuration section "Loremaster"
    - Add Loremaster configuration to appsettings.json
    - _Requirements: 4.6, 5.2, 9.4_

  - [x] 8.4 Write property test: Error Responses Never Expose Internals
    - **Property 12: Error Responses Never Expose Internals**
    - Generate various error scenarios (AI failure, retrieval failure, unexpected exceptions); map through controller error handling; assert responses never contain stack traces, internal exception messages, AI prompt content, or knowledge content
    - **Validates: Requirements 10.4**

- [x] 9. Checkpoint - API layer verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Unit tests for LoremasterService and controller
  - [x] 10.1 Write unit tests for LoremasterService — input validation
    - Test empty question → 400 validation error
    - Test whitespace-only question → 400 validation error
    - Test question over 2000 characters → 400 validation error
    - Test valid question proceeds to retrieval
    - Test AI client and knowledge retriever NOT called on validation failure
    - _Requirements: 2.2_

  - [x] 10.2 Write unit tests for LoremasterService — knowledge retrieval and prompt
    - Test valid question triggers knowledge retrieval with correct campaignId, userId, role
    - Test empty knowledge context → Low confidence answer with acknowledgment caveat
    - Test system prompt contains grounding instructions, citation format, anti-hallucination rules
    - Test prompt includes original question text
    - Test prompt includes artifact names from knowledge context
    - Test Rumor/Disputed facts have truth state labels in prompt
    - _Requirements: 4.1, 5.1, 5.3, 5.4, 6.1, 6.2, 6.3, 8.3_

  - [x] 10.3 Write unit tests for LoremasterService — citation parsing and answer assembly
    - Test valid `[ref:ID]` markers produce correct Citation objects
    - Test invalid reference IDs silently dropped
    - Test no citations when AI response has no markers
    - Test mixed valid/invalid markers produces only valid citations
    - Test answer text preserves citation markers for UI rendering
    - _Requirements: 7.1, 7.2, 7.3_

  - [x] 10.4 Write unit tests for LoremasterService — confidence calculation
    - Test no artifacts → Low
    - Test 1 confirmed fact, no relationships → Medium
    - Test 2 total facts → Medium
    - Test 3+ confirmed facts + relationships + source references → High
    - _Requirements: 8.4_

  - [x] 10.5 Write unit tests for LoremasterService — usage tracking and error handling
    - Test successful AI call creates AiUsageRecord with Succeeded=true and correct fields
    - Test failed AI call creates AiUsageRecord with Succeeded=false
    - Test AI timeout → 503 error propagated
    - Test AI rate limit → 429 error propagated
    - Test AI service error → 503 error propagated
    - Test cost calculation correctness with known pricing
    - Test cancellation token passed through to AI client and retriever
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 10.1, 10.2, 10.3_

  - [x] 10.6 Write unit tests for LoremasterController
    - Test valid request → 200 with AskAnswerResponse structure
    - Test membership enforced via CampaignMemberActionFilter
    - Test optional conversationContext accepted in request
    - Test validation error → 400 with user-friendly message
    - Test service unavailable → 503 with generic message
    - Test rate limited → 429 with retry message
    - Test internal error → 500 with generic message, no stack traces
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.3, 8.1, 8.2, 10.1, 10.2, 10.3, 10.4_

- [x] 11. Checkpoint - Unit test verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Integration tests
  - [x] 12.1 Write integration tests for LoremasterController authorization
    - Test CampaignMemberActionFilter applied to Ask endpoint
    - Test non-member request → 403 without revealing campaign existence
    - Test missing JWT → 401
    - Test GM receives answer grounded in GMOnly content
    - Test Player does NOT receive GMOnly content in answers
    - Test Observer receives only PartyVisible content
    - Test Private content of other users never appears in answers
    - _Requirements: 1.1, 1.2, 1.3, 3.1, 3.2, 3.3, 3.4_

  - [x] 12.2 Write integration tests for full Ask workflow through HTTP
    - Test POST with valid question → 200 with answer, citations, confidence, caveats
    - Test question with matching artifact names → relevant knowledge in answer
    - Test empty question → 400
    - Test oversized question → 400
    - Test AI timeout scenario → 503
    - Test AiUsageRecord created in database after successful call
    - _Requirements: 2.1, 2.2, 5.1, 8.1, 8.2, 9.1, 10.1_

- [x] 13. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Existing domain entities (Artifact, ArtifactFact, ArtifactRelationship, SourceReference, AiUsageRecord), enums (VisibilityScope, CampaignRole, TruthState, AiOperationType, ArtifactType, ArtifactStatus), and repository interfaces already exist in Nornis.Domain
- The existing CampaignMemberActionFilter, UserProvisioningMiddleware, and Auth0 JWT middleware are already in place
- The existing AppResult pattern is used for service return types throughout the application layer
- IArtifactRepository already has `ListByNamesInTextAsync` and `ListRecentByCampaignAsync` methods
- IArtifactFactRepository already has `ListByArtifactIdsAsync` method
- Property tests use FsCheck.NUnit (FsCheck 3.x) with minimum 100 iterations per property
- Property test tag format: `Feature: ask-loremaster, Property {number}: {property_text}`
- Use realistic test data: Campaign "Black Harbor Investigation", Users Kelda (GM), Tavrin (Player), Jorin (Observer), Artifacts Captain Voss, Black Harbor, Silver Key, Missing Caravan
- The FakeLoremasterAiClient enables testing without live Azure OpenAI calls in CI
- Conversation persistence is deferred for MVP — the endpoint accepts conversationContext but does not store it
- The system prompt template should be stored as a constant or embedded resource in the Application layer, not hardcoded in the service method

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.4"] },
    { "id": 1, "tasks": ["2.1", "2.2"] },
    { "id": 2, "tasks": ["3.1"] },
    { "id": 3, "tasks": ["3.2", "3.3", "3.4", "3.5"] },
    { "id": 4, "tasks": ["5.1", "5.2", "5.3"] },
    { "id": 5, "tasks": ["5.4", "7.1", "7.2"] },
    { "id": 6, "tasks": ["5.5", "5.6", "5.7", "5.8", "5.9", "5.10", "5.11"] },
    { "id": 7, "tasks": ["8.1", "8.2"] },
    { "id": 8, "tasks": ["8.3", "8.4"] },
    { "id": 9, "tasks": ["10.1", "10.2", "10.3", "10.4", "10.5", "10.6"] },
    { "id": 10, "tasks": ["12.1", "12.2"] }
  ]
}
```
