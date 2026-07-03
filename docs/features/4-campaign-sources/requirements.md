# Requirements Document

## Introduction

This feature implements Source CRUD operations for the Nornis API — the ability to create, store, manage, list, and delete campaign source material. Sources are the entry point to the MVP loop (Source → AI extraction → Review proposals → Artifacts → Ask). This feature covers source lifecycle management including visibility enforcement, processing status transitions, campaign-scoped authorization, and integration with an extraction queue for downstream AI processing.

## Glossary

- **API**: The Nornis ASP.NET Core backend service (nornis-api) that handles all authenticated requests
- **Source**: A raw information record entered or uploaded by a campaign member, containing title, body, type, visibility, and processing status
- **Source_Service**: The application-layer service responsible for orchestrating source operations including authorization, validation, and persistence
- **SourceType**: One of SessionNote, JournalEntry, GMNote, Transcript, Upload, Image, HandwrittenNotes, WebLink, or ImportedNote — classifies the origin of the source material
- **VisibilityScope**: One of Private, GMOnly, or PartyVisible — determines which campaign members can access the source
- **ProcessingStatus**: One of Draft, Ready, Queued, Processing, Processed, or Failed — tracks the source through the extraction lifecycle
- **Campaign_Member**: A user with an active membership in the target campaign, resolved via CampaignMemberActionFilter
- **GM**: Game Master role — can create, read, update, and delete any source in the campaign (subject to visibility rules for reading)
- **Player**: Player role — can create sources, manage own sources, and read sources according to visibility rules
- **Observer**: Observer role — read-only access to PartyVisible sources; cannot create, update, or delete sources in MVP
- **Extraction_Queue**: An Azure Service Bus queue (source-extraction) used to enqueue source extraction jobs for the downstream AI worker
- **Extraction_Message**: A message placed on the Extraction_Queue containing the Source Id and Campaign Id for worker processing

## Requirements

### Requirement 1: Create Source

**User Story:** As a campaign member (GM or Player), I want to create a source within my campaign, so that I can capture session notes, journal entries, and other campaign material for AI extraction.

#### Acceptance Criteria

1. WHEN a GM or Player submits a source creation request with a valid Title (1–200 characters, not blank), a valid SourceType, and a valid VisibilityScope, THE Source_Service SHALL create a new Source with ProcessingStatus set to Draft and return the created Source details including Id, CampaignId, Type, Title, Body, Uri, OccurredAt, CreatedAt, CreatedByUserId, Visibility, and ProcessingStatus
2. THE Source_Service SHALL set CreatedByUserId to the resolved Nornis User identifier from the authenticated request context
3. THE Source_Service SHALL set CreatedAt to the current UTC timestamp at the time of creation
4. WHEN a source creation request includes optional Body (maximum 100,000 characters), Uri (maximum 2,048 characters), or OccurredAt fields, THE Source_Service SHALL store the provided values on the created Source; omitted optional fields SHALL be stored as null
5. IF a source creation request has a missing, empty, or whitespace-only Title, or a Title exceeding 200 characters, THEN THE Source_Service SHALL return a validation error indicating the Title constraint that was violated
6. IF a source creation request specifies a SourceType value that is not one of the defined SourceType enum values, THEN THE Source_Service SHALL return a validation error indicating the allowed SourceType values
7. IF a source creation request specifies a VisibilityScope value that is not one of Private, GMOnly, or PartyVisible, THEN THE Source_Service SHALL return a validation error indicating the allowed VisibilityScope values
8. WHEN an Observer submits a source creation request, THE API SHALL return HTTP 403 Forbidden
9. WHEN a Player submits a source creation request with VisibilityScope set to GMOnly, THE Source_Service SHALL return a validation error indicating that Players cannot create GMOnly sources
10. IF a source creation request provides a Body exceeding 100,000 characters or a Uri exceeding 2,048 characters, THEN THE Source_Service SHALL return a validation error indicating the field length constraint that was violated

### Requirement 2: Get Source by Id

**User Story:** As a campaign member, I want to retrieve a specific source by its identifier, so that I can view its content and processing status.

#### Acceptance Criteria

1. WHEN a campaign member requests a source by Id, THE Source_Service SHALL return the Source details including Id, CampaignId, Type, Title, Body, Uri, OccurredAt, CreatedAt, CreatedByUserId, Visibility, and ProcessingStatus
2. IF a Player or Observer requests a source with VisibilityScope Private that was created by a different user, THEN THE Source_Service SHALL return a not-found error
3. IF a Player or Observer requests a source with VisibilityScope GMOnly, THEN THE Source_Service SHALL return a not-found error
4. WHEN any campaign member requests a source with VisibilityScope PartyVisible, THE Source_Service SHALL return the Source details
5. IF the requested Source Id does not exist within the campaign, THEN THE Source_Service SHALL return a not-found error
6. IF a non-member requests a source, THEN THE API SHALL return HTTP 403 Forbidden
7. WHEN a GM requests a source with VisibilityScope Private or GMOnly, THE Source_Service SHALL return the Source details regardless of which user created it

### Requirement 3: Update Source

**User Story:** As a source creator or GM, I want to update a source's title, body, URI, occurred-at date, type, or visibility, so that I can correct or enrich source material before extraction.

#### Acceptance Criteria

1. WHEN the source creator or a GM submits an update request with one or more updatable fields (Title, Body, Uri, OccurredAt, Type, Visibility), THE Source_Service SHALL update only the specified fields and return the updated Source details including Id, CampaignId, Type, Title, Body, Uri, OccurredAt, CreatedAt, CreatedByUserId, Visibility, and ProcessingStatus
2. WHEN a campaign member who is neither the source creator nor a GM submits an update request, THE Source_Service SHALL return a forbidden error
3. WHILE the Source ProcessingStatus is Queued, Processing, or Processed, THE Source_Service SHALL reject update requests with an error indicating the source cannot be modified in its current processing state
4. IF an update request provides a Title that is empty, whitespace-only, or exceeds 200 characters, THEN THE Source_Service SHALL return a validation error
5. IF the requested Source Id does not exist within the campaign, THEN THE Source_Service SHALL return a not-found error
6. WHEN a Player updates their own source and sets Visibility to GMOnly, THE Source_Service SHALL return a validation error indicating that Players cannot set GMOnly visibility
7. IF an update request provides a Body that exceeds 100,000 characters, THEN THE Source_Service SHALL return a validation error indicating the maximum body length
8. IF an update request specifies a Type value that is not one of the defined SourceType enum values, or a Visibility value that is not one of Private, GMOnly, or PartyVisible, THEN THE Source_Service SHALL return a validation error indicating the allowed values
9. IF an update request provides a Uri that exceeds 2,048 characters, THEN THE Source_Service SHALL return a validation error indicating the maximum URI length

### Requirement 4: Delete Source

**User Story:** As a source creator or GM, I want to delete a source, so that I can remove incorrect or unwanted material from the campaign.

#### Acceptance Criteria

1. WHEN the source creator or a GM submits a delete request for a source, THE Source_Service SHALL delete the Source record and return a success indicator
2. WHEN a campaign member who is neither the source creator nor a GM submits a delete request, THE Source_Service SHALL return a forbidden error
3. WHILE the Source ProcessingStatus is Queued or Processing, THE Source_Service SHALL reject delete requests with an error indicating the source cannot be deleted while being processed
4. IF the requested Source Id does not exist within the campaign, THEN THE Source_Service SHALL return a not-found error
5. WHEN a source in Draft, Ready, Processed, or Failed status is deleted, THE Source_Service SHALL allow the deletion regardless of whether associated SourceReference records exist

### Requirement 5: List Sources by Campaign

**User Story:** As a campaign member, I want to list all sources in my campaign that I am authorized to see, so that I can browse the source ledger and find material to review.

#### Acceptance Criteria

1. WHEN a GM requests the source list for a campaign, THE Source_Service SHALL return all sources in the campaign regardless of VisibilityScope
2. WHEN a Player requests the source list for a campaign, THE Source_Service SHALL return sources where VisibilityScope is PartyVisible, or where VisibilityScope is Private and CreatedByUserId matches the requesting user
3. WHEN an Observer requests the source list for a campaign, THE Source_Service SHALL return only sources where VisibilityScope is PartyVisible
4. THE Source_Service SHALL return sources ordered by CreatedAt descending
5. THE Source_Service SHALL return each source with its Id, CampaignId, Type, Title, OccurredAt, CreatedAt, CreatedByUserId, Visibility, and ProcessingStatus
6. IF the campaign has no sources visible to the requesting member, THEN THE Source_Service SHALL return an empty list

### Requirement 6: Mark Source as Ready

**User Story:** As a source creator or GM, I want to mark a source as ready for processing, so that it becomes eligible for AI extraction.

#### Acceptance Criteria

1. WHEN the source creator or a GM submits a mark-ready request for a source with ProcessingStatus Draft, THE Source_Service SHALL transition the ProcessingStatus to Ready and return the updated Source details
2. IF the Source ProcessingStatus is not Draft when a mark-ready request is received, THEN THE Source_Service SHALL return an error indicating the source can only be marked ready from Draft status
3. WHEN a campaign member who is neither the source creator nor a GM submits a mark-ready request, THE Source_Service SHALL return a forbidden error
4. IF the requested Source Id does not exist within the campaign, THEN THE Source_Service SHALL return a not-found error

### Requirement 7: Enqueue Source for Extraction

**User Story:** As a system component, I want sources marked as Ready to be enqueued for AI extraction, so that the downstream worker can process them asynchronously.

#### Acceptance Criteria

1. WHEN a Source transitions to Ready status, THE Source_Service SHALL place an Extraction_Message on the Extraction_Queue containing the Source Id and Campaign Id
2. WHEN the Extraction_Message is successfully placed on the Extraction_Queue, THE Source_Service SHALL transition the Source ProcessingStatus from Ready to Queued
3. IF placing the Extraction_Message on the Extraction_Queue fails, THEN THE Source_Service SHALL leave the Source ProcessingStatus as Ready and return an error indicating the enqueue operation failed
4. THE Extraction_Message SHALL contain only identifiers (Source Id, Campaign Id) and SHALL NOT contain the full source body

### Requirement 8: Processing Status Lifecycle

**User Story:** As a system operator, I want the source processing status to follow a defined lifecycle, so that the system maintains consistent state through the extraction pipeline.

#### Acceptance Criteria

1. THE Source_Service SHALL enforce the following valid ProcessingStatus transitions: Draft to Ready, Ready to Queued, Queued to Processing, Processing to Processed, and Processing to Failed
2. IF a status transition is requested that does not match the valid transitions, THEN THE Source_Service SHALL return an error indicating the transition is not allowed from the current status
3. WHEN a Source transitions to Failed status, THE Source_Service SHALL allow transition back to Ready so the source can be re-queued for extraction
4. THE Source_Service SHALL NOT allow direct transitions from Draft to Queued, Draft to Processing, Ready to Processing, or any skipped-state transition not explicitly defined

### Requirement 9: Source Visibility Enforcement

**User Story:** As a system operator, I want visibility rules consistently enforced across all source operations, so that private and GM-only content is never leaked to unauthorized members.

#### Acceptance Criteria

1. THE Source_Service SHALL enforce that Private sources are accessible only to the creating user and campaign GMs for all read operations (get by id, list)
2. THE Source_Service SHALL enforce that GMOnly sources are accessible only to campaign GMs for all read operations
3. THE Source_Service SHALL enforce that PartyVisible sources are accessible to all campaign members for all read operations
4. WHEN a non-GM member attempts any operation on a source they cannot see due to visibility rules, THE Source_Service SHALL respond as if the source does not exist (not-found error rather than forbidden)
5. THE Source_Service SHALL NOT allow Players to create or update sources with GMOnly visibility

### Requirement 10: Campaign-Scoped Source Authorization

**User Story:** As a system operator, I want all source endpoints to require campaign membership, so that only authorized campaign participants can interact with source material.

#### Acceptance Criteria

1. THE API SHALL require campaign membership for all source endpoints, enforced via the CampaignMemberActionFilter
2. IF the requesting user is not a member of the target campaign, THEN THE API SHALL return HTTP 403 Forbidden
3. THE API SHALL derive the acting user identity from the authenticated JWT claims and SHALL NOT accept client-provided user identifiers for source operations
4. WHEN a source endpoint references a campaign that does not exist, THE API SHALL return HTTP 403 Forbidden for non-members (consistent with campaign-scoped authorization behavior)
