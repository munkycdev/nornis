# Requirements Document

## Introduction

This document specifies the "Ask the Loremaster" feature — a conversational AI interface that allows campaign members to ask questions about their campaign and receive AI-generated answers grounded in accepted campaign knowledge. The Loremaster retrieves relevant Artifacts, ArtifactFacts, ArtifactRelationships, and SourceReferences, respects visibility and campaign membership, cites sources in answers, and admits when information is unknown or unsupported.

## Glossary

- **Loremaster_Service**: The application-layer service that orchestrates question answering by retrieving campaign knowledge, constructing AI prompts, calling Azure OpenAI, and returning structured answers.
- **Knowledge_Retriever**: The component responsible for loading relevant Artifacts, ArtifactFacts, ArtifactRelationships, and SourceReferences from the database based on a question. For MVP, uses keyword/name matching against the question text.
- **Ask_Endpoint**: The API endpoint that accepts a campaign member's question and returns a structured Loremaster answer.
- **Campaign_Member**: A user who has an active CampaignMember record for the specified campaign (role: GM, Player, or Observer).
- **Visibility_Filter**: Logic that restricts which knowledge items are included in retrieval based on the requesting user's campaign role and visibility rules.
- **Answer_Response**: The structured response returned by the Ask endpoint, containing answer text, citations, confidence indicator, and caveats.
- **Citation**: A reference to a specific SourceReference, Artifact, ArtifactFact, or ArtifactRelationship that supports a claim in the answer.
- **AiUsageRecord**: A database record tracking token counts, estimated cost, duration, and success/failure for each AI call.
- **Confidence_Indicator**: A qualitative signal (e.g., High, Medium, Low) indicating how well the answer is supported by campaign knowledge.

## Requirements

### Requirement 1: Campaign Membership Authorization

**User Story:** As a campaign member, I want the Ask endpoint to verify my campaign membership before answering, so that non-members cannot access campaign knowledge.

#### Acceptance Criteria

1. WHEN a request is received at the Ask endpoint, THE Ask_Endpoint SHALL require a valid Auth0 JWT and a resolved Nornis user.
2. WHEN a request is received from a user who is not a Campaign_Member of the specified campaign, THE Ask_Endpoint SHALL return HTTP 403 without revealing whether the campaign exists.
3. THE Ask_Endpoint SHALL derive the user identity from validated JWT claims and never trust client-provided user IDs.

### Requirement 2: Accept and Validate Question

**User Story:** As a campaign member, I want to submit a question in natural language, so that the Loremaster can answer based on campaign knowledge.

#### Acceptance Criteria

1. WHEN a Campaign_Member submits a question, THE Ask_Endpoint SHALL accept a request body containing a question string.
2. WHEN the question string is empty or exceeds 2000 characters, THE Ask_Endpoint SHALL return HTTP 400 with a descriptive validation error.
3. THE Ask_Endpoint SHALL accept an optional conversation context parameter for future multi-turn support.

### Requirement 3: Role-Based Visibility Filtering

**User Story:** As a GM, I want the Loremaster to ground answers in all visibility levels I have access to, and as a Player, I want answers grounded only in PartyVisible content, so that sensitive information is never leaked.

#### Acceptance Criteria

1. WHEN the requesting user has the GM role, THE Visibility_Filter SHALL include knowledge items with visibility Private (owned by the user), GMOnly, and PartyVisible.
2. WHEN the requesting user has the Player role, THE Visibility_Filter SHALL include only knowledge items with visibility PartyVisible and Private (owned by the user).
3. WHEN the requesting user has the Observer role, THE Visibility_Filter SHALL include only knowledge items with visibility PartyVisible.
4. THE Visibility_Filter SHALL never include Private knowledge owned by a different user regardless of role.
5. THE Loremaster_Service SHALL apply the Visibility_Filter before retrieving any Artifacts, ArtifactFacts, ArtifactRelationships, or SourceReferences.

### Requirement 4: Knowledge Retrieval

**User Story:** As a campaign member, I want the Loremaster to find relevant campaign knowledge based on my question, so that answers are grounded in accepted facts.

#### Acceptance Criteria

1. WHEN a question is received, THE Knowledge_Retriever SHALL identify relevant Artifacts by matching artifact names against keywords in the question text.
2. WHEN relevant Artifacts are identified, THE Knowledge_Retriever SHALL load ArtifactFacts associated with those Artifacts, filtered by the Visibility_Filter.
3. WHEN relevant Artifacts are identified, THE Knowledge_Retriever SHALL load ArtifactRelationships involving those Artifacts, filtered by the Visibility_Filter.
4. WHEN relevant Artifacts are identified, THE Knowledge_Retriever SHALL load SourceReferences associated with retrieved facts and relationships.
5. THE Knowledge_Retriever SHALL also load recently updated Artifacts in the campaign (limited to a configurable maximum count) to provide broader context.
6. THE Knowledge_Retriever SHALL be implemented behind an abstraction so the retrieval mechanism is swappable without changing the Loremaster_Service.

### Requirement 5: AI-Generated Answer

**User Story:** As a campaign member, I want to receive an AI-generated answer that synthesizes campaign knowledge, so that I can understand what Nornis knows about my question.

#### Acceptance Criteria

1. WHEN the Knowledge_Retriever has assembled relevant knowledge, THE Loremaster_Service SHALL construct a prompt containing the question and retrieved knowledge context.
2. THE Loremaster_Service SHALL call Azure OpenAI with the constructed prompt and receive a response.
3. THE Loremaster_Service SHALL instruct the AI model to ground its answer only in the provided campaign knowledge context.
4. THE Loremaster_Service SHALL instruct the AI model to cite specific sources when making claims.
5. THE Loremaster_Service SHALL use a cancellation token to allow request cancellation.

### Requirement 6: Hallucination Guardrails

**User Story:** As a campaign member, I want the Loremaster to admit when it does not have enough information rather than inventing an answer, so that I can trust the knowledge record.

#### Acceptance Criteria

1. WHEN the retrieved campaign knowledge does not contain information relevant to the question, THE Loremaster_Service SHALL instruct the AI to respond with an acknowledgment that the information is not available (e.g., "I don't have a confirmed source for that yet").
2. WHEN the retrieved knowledge contains items marked with TruthState Rumor or Disputed, THE Loremaster_Service SHALL instruct the AI to qualify those claims with their truth state in the answer.
3. THE Loremaster_Service SHALL include explicit instructions in the prompt that the AI must not invent campaign facts beyond what is provided in the context.

### Requirement 7: Source Citations in Answer

**User Story:** As a campaign member, I want the Loremaster's answer to cite specific sources, so that I can trace claims back to their origin.

#### Acceptance Criteria

1. WHEN the AI generates an answer, THE Loremaster_Service SHALL parse citations from the AI response.
2. THE Answer_Response SHALL include a list of Citation objects, each referencing the source artifact, fact, or relationship that supports a claim.
3. WHEN no SourceReferences support a particular claim, THE Answer_Response SHALL omit a citation for that claim rather than fabricating one.

### Requirement 8: Structured Answer Response

**User Story:** As a frontend developer, I want the Ask endpoint to return a structured response, so that I can render the answer with citations, confidence, and caveats in the UI.

#### Acceptance Criteria

1. THE Ask_Endpoint SHALL return an Answer_Response containing: answer text, a list of citations, a Confidence_Indicator, and a list of caveats.
2. WHEN the AI call succeeds, THE Ask_Endpoint SHALL return HTTP 200 with the Answer_Response.
3. WHEN no relevant campaign knowledge is found, THE Ask_Endpoint SHALL still return HTTP 200 with an answer acknowledging the lack of information and a Low Confidence_Indicator.
4. THE Confidence_Indicator SHALL be one of: High, Medium, or Low, determined by the quantity and quality of retrieved knowledge supporting the answer.

### Requirement 9: AI Usage Tracking

**User Story:** As a system operator, I want every Ask call to create an AiUsageRecord, so that token consumption and costs are tracked.

#### Acceptance Criteria

1. WHEN an AI call is made by the Loremaster_Service, THE Loremaster_Service SHALL create an AiUsageRecord with OperationType set to AskLoremaster.
2. THE AiUsageRecord SHALL capture: CampaignId, UserId, Model, InputTokens, OutputTokens, TotalTokens, EstimatedCostUsd, DurationMs, Succeeded, and CreatedAt.
3. WHEN the AI call fails, THE Loremaster_Service SHALL still create an AiUsageRecord with Succeeded set to false and an appropriate ErrorCode.
4. THE Loremaster_Service SHALL calculate EstimatedCostUsd using configured model pricing rates.

### Requirement 10: Error Handling

**User Story:** As a campaign member, I want clear error responses when the Loremaster cannot answer, so that I understand what went wrong.

#### Acceptance Criteria

1. IF the AI call fails due to a timeout or service error, THEN THE Ask_Endpoint SHALL return HTTP 503 with a user-friendly error message indicating the service is temporarily unavailable.
2. IF the AI call fails due to rate limiting, THEN THE Ask_Endpoint SHALL return HTTP 429 with a message indicating the user should try again later.
3. IF an unexpected error occurs during knowledge retrieval, THEN THE Ask_Endpoint SHALL return HTTP 500 with a generic error message and log the full error context.
4. THE Ask_Endpoint SHALL never expose stack traces, internal error details, or AI prompt content in error responses.

### Requirement 11: Observability

**User Story:** As a system operator, I want Ask operations to be logged and metered, so that I can monitor usage, performance, and failures.

#### Acceptance Criteria

1. THE Loremaster_Service SHALL emit structured logs including: correlation ID, user ID, campaign ID, operation type (AskLoremaster), duration, and success/failure status.
2. THE Loremaster_Service SHALL emit metrics for: ask operation count, ask operation duration, input tokens, output tokens, and estimated cost.
3. THE Loremaster_Service SHALL never log the full AI prompt content or sensitive GMOnly source material in default log configuration.
