# Requirements Document

## Introduction

This feature implements the async source extraction worker — the nornis-worker service that consumes messages from the `source-extraction` Azure Service Bus queue, calls Azure OpenAI with a structured output schema, and creates ReviewBatch and ReviewProposal records. The worker is the critical bridge between the source lifecycle (sources in Queued status) and the AI extraction pipeline that produces reviewable campaign knowledge proposals. It handles message reception, source retrieval, existing artifact context loading, AI invocation, structured output parsing, proposal persistence, AI usage tracking, idempotency, failure handling, and dead-letter behavior.

## Glossary

- **Worker**: The nornis-worker background job processor service that listens to Azure Service Bus and processes extraction jobs
- **Extraction_Queue**: The `source-extraction` Azure Service Bus queue where extraction messages are placed by the API when a source is marked ready
- **Extraction_Message**: A message on the Extraction_Queue containing a Source Id and Campaign Id
- **Extraction_Service**: The application-layer service responsible for orchestrating the extraction workflow including source retrieval, context assembly, AI invocation, and proposal creation
- **AI_Client**: The infrastructure-layer client that calls Azure OpenAI with a structured output schema and returns the parsed extraction result
- **Structured_Output_Schema**: The JSON schema defining the expected shape of the AI extraction response, including an array of proposals each with changeType, targetType, targetId, proposedValue, rationale, and confidence
- **ReviewBatch**: A record grouping all proposals from a single extraction run for one source
- **ReviewProposal**: An individual reviewable change proposal extracted from a source (one proposal per discrete change)
- **AiUsageRecord**: A record tracking token consumption, estimated cost, model, duration, and success/failure for every AI call
- **SourceReference**: A citation linking a ReviewProposal back to the source it was extracted from, including an optional quote
- **Source**: The raw campaign information being processed, containing body text, visibility, type, and processing status
- **Campaign_Artifact_Context**: The set of existing artifacts, facts, and relationships in the campaign that are provided to the AI to reduce duplicate proposals
- **Dead_Letter_Queue**: The Azure Service Bus dead-letter queue where messages are moved after repeated processing failures
- **VisibilityScope**: One of Private, GMOnly, or PartyVisible — determines access control for proposals derived from the source
- **TruthState**: One of Confirmed, Likely, Rumor, Disputed, False, or Hidden — the default confidence assessment applied to extracted facts and relationships

## Requirements

### Requirement 1: Message Reception and Processing Lifecycle

**User Story:** As the nornis-worker service, I want to receive extraction messages from the Azure Service Bus queue and manage processing state transitions, so that sources move through the extraction pipeline reliably.

#### Acceptance Criteria

1. WHEN the Worker receives an Extraction_Message from the Extraction_Queue, THE Extraction_Service SHALL transition the Source ProcessingStatus from Queued to Processing
2. WHEN extraction completes and the Extraction_Service has persisted a ReviewBatch with one or more ReviewProposal records, THE Extraction_Service SHALL transition the Source ProcessingStatus from Processing to Processed and complete the message on the Extraction_Queue
3. IF the source referenced by the Extraction_Message does not exist, THEN THE Extraction_Service SHALL complete the message without creating any records and log a warning
4. IF the Source ProcessingStatus is not Queued when the Worker receives the message, THEN THE Extraction_Service SHALL complete the message without reprocessing and log an informational message indicating the source was already processed or is in an unexpected state
5. THE Worker SHALL use Azure Service Bus peek-lock mode with a message lock duration of 5 minutes to receive messages, completing the message only after the source transitions to Processed, or after an explicit decision to discard the message (source not found, source not in Queued status)
6. WHEN the Worker starts processing a message, THE Extraction_Service SHALL log the Source Id, Campaign Id, and a correlation identifier for traceability
7. IF extraction fails due to an unrecoverable error during processing (AI call failure, validation failure, or unexpected exception), THEN THE Extraction_Service SHALL transition the Source ProcessingStatus from Processing to Failed, complete the message on the Extraction_Queue, and log an error including the Source Id, Campaign Id, and failure reason
8. IF the Worker fails to process a message and does not explicitly complete it (process crash, lock expiration), THEN the message SHALL become available for redelivery up to a maximum delivery count of 5 attempts, after which Azure Service Bus SHALL move the message to the dead-letter queue

### Requirement 2: Idempotent Processing

**User Story:** As a system operator, I want extraction processing to be idempotent, so that redelivered messages do not produce duplicate ReviewBatch or ReviewProposal records.

#### Acceptance Criteria

1. WHEN the Extraction_Service receives a message for a Source that already has a ReviewBatch in Pending, InReview, or Completed status, THE Extraction_Service SHALL complete the message without creating additional records
2. WHEN the Extraction_Service receives a message for a Source with ProcessingStatus of Processed, THE Extraction_Service SHALL complete the message without reprocessing
3. THE Extraction_Service SHALL use the Source Id as the natural idempotency key when determining whether an extraction has already been performed

### Requirement 3: Source Retrieval and Validation

**User Story:** As the extraction pipeline, I want to retrieve the full source record by Id and Campaign Id, so that the AI has the source text and metadata needed for extraction.

#### Acceptance Criteria

1. WHEN the Extraction_Service begins processing, THE Extraction_Service SHALL retrieve the Source record by Id and Campaign Id from the database
2. IF the Source Body is null, empty, or contains only whitespace characters, THEN THE Extraction_Service SHALL skip AI invocation, create a ReviewBatch with zero proposals in Completed status, transition the Source to Processed, and log that the source had no extractable content
3. THE Extraction_Service SHALL pass the Source Body, Title, Type, Visibility, and OccurredAt to the AI context assembly step; IF OccurredAt is null, THEN THE Extraction_Service SHALL omit temporal context from the AI input rather than passing a null value

### Requirement 4: Campaign Artifact Context Assembly

**User Story:** As the extraction pipeline, I want to provide existing campaign artifacts as context to the AI, so that the AI can propose updates to existing artifacts rather than creating duplicates.

#### Acceptance Criteria

1. WHEN assembling context for AI invocation, THE Extraction_Service SHALL retrieve artifacts in the campaign ordered by UpdatedAt descending, limited to a configurable maximum count with a default of 50
2. WHEN assembling context for AI invocation and the Source Body is not null, THE Extraction_Service SHALL retrieve artifacts whose names appear in the Source Body text using case-insensitive whole-word matching
3. WHEN both retrieval strategies (recent and name-matched) return results, THE Extraction_Service SHALL merge the results into a single deduplicated list, prioritizing name-matched artifacts first followed by recently updated artifacts, up to the configurable maximum count
4. THE Extraction_Service SHALL include each context artifact's name, type, summary, and up to 20 facts per artifact (ordered by UpdatedAt descending) in the context payload provided to the AI
5. THE Extraction_Service SHALL respect VisibilityScope when assembling context: for a Private source, include only artifacts visible to the source creator; for a GMOnly source, include artifacts visible to GMs; for a PartyVisible source, include only PartyVisible artifacts
6. IF no existing artifacts are found for the campaign, THEN THE Extraction_Service SHALL proceed with extraction using only the source content as input
7. IF the Source Body is null or empty, THEN THE Extraction_Service SHALL skip name-based matching and retrieve only recently updated artifacts for context

### Requirement 5: AI Invocation with Structured Output

**User Story:** As the extraction pipeline, I want to call Azure OpenAI with a structured output schema, so that the AI returns proposals in a predictable, parseable format without fragile natural-language parsing.

#### Acceptance Criteria

1. WHEN invoking the AI, THE AI_Client SHALL send the source content, source metadata, and campaign artifact context to Azure OpenAI with a structured output schema that defines the expected response shape
2. THE AI_Client SHALL request a response conforming to a JSON schema containing an array of proposals (minimum 0, maximum 50 entries), where each proposal includes changeType (one of the defined ReviewChangeType values), targetType (one of the defined ReviewTargetType values), targetId (nullable GUID), proposedValue (object matching the target type's schema), rationale (string, 1–500 characters), and confidence (decimal between 0.0 and 1.0 inclusive)
3. THE AI_Client SHALL include a system prompt instructing the AI to: match proposal visibility to the source's VisibilityScope (Private source produces Private proposals, GMOnly produces GMOnly, PartyVisible produces PartyVisible); apply conservative truth state defaults (direct observations default to Likely, character claims default to Rumor, GM notes default to Hidden); and provide rationale for each proposal
4. THE AI_Client SHALL enforce a configurable timeout for the AI call with a default of 60 seconds; IF the AI call exceeds the configured timeout, THEN THE AI_Client SHALL abort the call, treat the invocation as a failure, and return an error indicating timeout
5. IF the AI response does not conform to the Structured_Output_Schema, THEN THE AI_Client SHALL treat the response as a failure and return an error indicating structured output parse failure
6. IF the AI call fails due to a non-timeout error (network failure, rate limiting, or service unavailability), THEN THE AI_Client SHALL treat the invocation as a failure and return an error indicating the failure category
7. WHEN the AI returns a valid response containing an empty proposals array, THE AI_Client SHALL treat the response as successful and return the empty array without error

### Requirement 6: AI Usage Tracking

**User Story:** As a system operator and campaign manager, I want every AI call to produce an AiUsageRecord, so that token consumption and estimated costs are tracked for observability and billing.

#### Acceptance Criteria

1. WHEN the AI_Client completes an AI call (success or failure), THE Extraction_Service SHALL create an AiUsageRecord with the CampaignId, SourceId, OperationType set to SourceExtraction, Model name, InputTokens, OutputTokens, TotalTokens, EstimatedCostUsd, DurationMs, and Succeeded flag
2. THE Extraction_Service SHALL calculate EstimatedCostUsd using configured per-model token pricing rates (InputPerMillionTokensUsd and OutputPerMillionTokensUsd)
3. IF the AI call fails, THEN THE Extraction_Service SHALL create an AiUsageRecord with Succeeded set to false, an ErrorCode describing the failure category, and token counts set to zero if unavailable
4. THE AiUsageRecord SHALL include the ReviewBatchId once the ReviewBatch is created; if creation fails before batch creation, ReviewBatchId SHALL be null

### Requirement 7: ReviewBatch and ReviewProposal Creation

**User Story:** As the extraction pipeline, I want to create a ReviewBatch and individual ReviewProposal records from the AI output, so that users can review each proposed change individually.

#### Acceptance Criteria

1. WHEN the AI returns a response that conforms to the expected extraction schema and contains a parseable proposals array, THE Extraction_Service SHALL create a ReviewBatch with CampaignId, SourceId, Status set to Pending, and CreatedAt set to the current UTC timestamp, and SHALL transition the Source ProcessingStatus to Processed
2. WHEN a ReviewBatch is created from an AI response containing one or more proposals, THE Extraction_Service SHALL create one ReviewProposal per proposal entry with ReviewBatchId, ChangeType (one of the defined ReviewChangeType values), TargetType (one of the defined ReviewTargetType values), TargetId (null for new artifacts), ProposedValueJson (serialized from the AI proposedValue object, maximum 50,000 characters), Rationale, Confidence (a decimal value between 0.00 and 1.00 inclusive, or null), Status set to Pending, and CreatedAt set to the current UTC timestamp
3. THE Extraction_Service SHALL create exactly one ReviewProposal per discrete change proposed by the AI — one proposal per artifact creation, artifact update, fact addition, fact update, relationship addition, or relationship update
4. WHEN a ReviewProposal is created, THE Extraction_Service SHALL create a SourceReference with TargetType set to ReviewProposal, TargetId set to the proposal Id, SourceId set to the extraction source Id, and CreatedAt set to the current UTC timestamp
5. IF the AI response contains zero proposals, THEN THE Extraction_Service SHALL create a ReviewBatch with Status set to Completed and zero ReviewProposal records, and transition the Source ProcessingStatus to Processed
6. IF the AI response does not conform to the expected extraction schema or cannot be parsed, THEN THE Extraction_Service SHALL NOT create a ReviewBatch, SHALL transition the Source ProcessingStatus to Failed, and SHALL log an error indicating the parse failure reason
7. THE Extraction_Service SHALL create the ReviewBatch, all ReviewProposals, and all SourceReferences within a single atomic operation — if any record fails to persist, none of the records from that extraction SHALL be committed

### Requirement 8: Visibility Propagation to Proposals

**User Story:** As a system operator, I want proposals to inherit the visibility scope of their source, so that private and GM-only content is never exposed to unauthorized members through the review queue.

#### Acceptance Criteria

1. WHEN creating ReviewProposal records, THE Extraction_Service SHALL set the proposed visibility in ProposedValueJson to match the Source VisibilityScope: Private sources produce Private proposals, GMOnly sources produce GMOnly proposals, and PartyVisible sources produce PartyVisible proposals
2. THE Extraction_Service SHALL NOT create proposals with a visibility scope broader than the source visibility (a Private source SHALL NOT produce PartyVisible or GMOnly proposals; a GMOnly source SHALL NOT produce PartyVisible proposals)

### Requirement 9: Truth State Defaults

**User Story:** As the extraction pipeline, I want proposals to include conservative truth state defaults, so that extracted facts and relationships reflect appropriate confidence levels based on source type and content.

#### Acceptance Criteria

1. WHEN the AI proposes facts or relationships from PartyVisible session notes or journal entries describing direct observations, THE AI_Client system prompt SHALL instruct the AI to assign TruthState of Likely or Confirmed
2. WHEN the AI proposes facts based on character claims or dialogue within the source, THE AI_Client system prompt SHALL instruct the AI to assign TruthState of Rumor or Disputed
3. WHEN the AI proposes facts from GMOnly sources, THE AI_Client system prompt SHALL instruct the AI to assign TruthState of Hidden or Confirmed depending on the phrasing
4. WHEN the AI proposes facts from Private sources containing player theories or speculation, THE AI_Client system prompt SHALL instruct the AI to assign TruthState of Rumor

### Requirement 10: Failure Handling and Dead-Letter Behavior

**User Story:** As a system operator, I want extraction failures to be handled gracefully with appropriate state transitions and dead-letter behavior, so that transient failures are retried and persistent failures are surfaced for investigation.

#### Acceptance Criteria

1. IF the AI call throws an exception or returns a non-successful response, or structured output parsing fails after the Worker has exhausted its application-level parse retries (maximum 2 retry attempts), THEN THE Extraction_Service SHALL transition the Source ProcessingStatus to Failed and set the ReviewBatch Status to Failed if a ReviewBatch was already created during this processing attempt
2. IF a transient failure occurs (timeout, network error, service unavailability), THEN THE Worker SHALL abandon the message without modifying Source or ReviewBatch status, allowing Azure Service Bus to redeliver it according to the queue retry policy
3. IF a non-transient failure occurs (source not found in database, source Body is null or empty, validation failure on source metadata, or malformed AI structured output after exhausting 2 parse retry attempts), THEN THE Worker SHALL transition the Source ProcessingStatus to Failed, set the ReviewBatch Status to Failed if one was created, and then complete the message to remove it from the queue
4. WHEN a message exceeds the maximum delivery count configured on the Azure Service Bus queue, THE message SHALL be moved to the Dead_Letter_Queue by Azure Service Bus without Worker intervention
5. THE Worker SHALL log all failures with Source Id, Campaign Id, error category (one of: TransientError, SourceNotFound, EmptySourceBody, ValidationFailure, AiCallFailure, ParseFailure), attempt number, and correlation identifier
6. WHEN a Source transitions to Failed status via the Worker, THE Source_Service SHALL permit a subsequent transition from Failed to Ready status so the source can be re-queued for extraction via the existing Source API

### Requirement 11: Observability and Metrics

**User Story:** As a system operator, I want the worker to emit structured logs and metrics for every extraction job, so that I can monitor pipeline health, latency, and failure rates.

#### Acceptance Criteria

1. THE Worker SHALL emit metrics for: extraction jobs started, extraction jobs completed, extraction jobs failed, extraction duration in milliseconds, and dead-letter count
2. THE Worker SHALL emit metrics for AI operations: input tokens, output tokens, total tokens, estimated cost, model used, and structured output parse failure count
3. THE Worker SHALL emit metrics for review output: review batches created and proposals created (count per batch)
4. THE Worker SHALL use structured logging with fields: CorrelationId, SourceId, CampaignId, OperationType, ReviewBatchId (when available), and DurationMs
5. THE Worker SHALL tag all metrics with service:nornis-worker, environment (dev or prod), and ai_model

### Requirement 12: Configuration and Operational Guardrails

**User Story:** As a system operator, I want the worker to use externalized configuration for AI model, timeouts, context limits, and pricing, so that operational parameters can be adjusted without redeployment.

#### Acceptance Criteria

1. THE Worker SHALL read AI model name, API endpoint, and timeout from configuration (not hardcoded)
2. THE Worker SHALL read the maximum artifact context count (number of existing artifacts to include) from configuration with a sensible default
3. THE Worker SHALL read per-model token pricing (InputPerMillionTokensUsd and OutputPerMillionTokensUsd) from configuration for cost estimation
4. THE Worker SHALL read Azure Service Bus connection configuration (connection string or managed identity settings) from configuration or Key Vault
5. IF required configuration values are missing at startup, THEN THE Worker SHALL fail fast with a clear error message indicating which configuration is missing

