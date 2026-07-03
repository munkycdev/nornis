# Requirements Document

## Introduction

This document specifies the Cost Dashboard feature — the API endpoints that aggregate and serve AI token usage and estimated cost data from AiUsageRecords. The dashboard enables campaign owners and system operators to understand how much AI usage is occurring, broken down by time period, campaign, user, operation type, and model. The feature provides campaign-scoped cost visibility with role-based access control, ensuring GMs see full campaign costs while Players see only their own usage.

## Glossary

- **Cost_Service**: The application-layer service that orchestrates cost data aggregation by querying AiUsageRecords, grouping results, and calculating summaries.
- **Cost_Endpoint**: The API controller that exposes cost dashboard data via HTTP endpoints, scoped to a campaign.
- **Campaign_Member**: A user who has an active CampaignMember record for the specified campaign (role: GM, Player, or Observer).
- **AiUsageRecord**: A database record tracking CampaignId, UserId, OperationType, Model, InputTokens, OutputTokens, TotalTokens, EstimatedCostUsd, DurationMs, Succeeded, ErrorCode, and CreatedAt for each AI call.
- **AiOperationType**: The enum of AI operations: SourceExtraction, ArtifactSummary, AskLoremaster, SourceExtractionRepair.
- **Cost_Summary**: An aggregated view of total input tokens, output tokens, total tokens, estimated cost in USD, and operation count for a given grouping.
- **Time_Period**: A predefined time window for aggregation: today, this week (Monday to now), this month (first of month to now), or all-time.
- **Model_Pricing**: Configuration that maps AI model names to per-million-token rates for input and output, used to calculate EstimatedCostUsd.
- **Date_Range_Filter**: An optional pair of start and end DateTimeOffset values used to restrict which AiUsageRecords are included in aggregation.

## Requirements

### Requirement 1: Campaign Membership Authorization

**User Story:** As a campaign member, I want the Cost endpoint to verify my campaign membership before returning cost data, so that non-members cannot access campaign usage information.

#### Acceptance Criteria

1. WHEN a request is received at the Cost_Endpoint, THE Cost_Endpoint SHALL require a valid Auth0 JWT and a resolved Nornis user.
2. WHEN a request is received from a user who is not a Campaign_Member of the specified campaign, THE Cost_Endpoint SHALL return HTTP 403 without revealing whether the campaign exists.
3. THE Cost_Endpoint SHALL derive the user identity from validated JWT claims and never trust client-provided user IDs.

### Requirement 2: Role-Based Cost Visibility

**User Story:** As a GM, I want to see full campaign cost data for all users, and as a Player, I want to see only my own usage, so that cost visibility aligns with campaign authority.

#### Acceptance Criteria

1. WHEN the requesting user has the GM role, THE Cost_Service SHALL return aggregated cost data across all users in the campaign.
2. WHEN the requesting user has the Player role, THE Cost_Service SHALL return aggregated cost data filtered to only the requesting user's AiUsageRecords within the campaign.
3. WHEN the requesting user has the Observer role, THE Cost_Service SHALL return aggregated cost data filtered to only the requesting user's AiUsageRecords within the campaign.
4. THE Cost_Service SHALL never return usage data for users other than the requester unless the requester has the GM role.

### Requirement 3: Cost Summary by Time Period

**User Story:** As a campaign member, I want to see cost summaries for today, this week, this month, and all-time, so that I can understand usage trends at a glance.

#### Acceptance Criteria

1. THE Cost_Endpoint SHALL provide a summary endpoint that returns Cost_Summary values for each predefined Time_Period: today, this week, this month, and all-time.
2. THE Cost_Summary for "today" SHALL aggregate AiUsageRecords with CreatedAt on the current UTC date.
3. THE Cost_Summary for "this week" SHALL aggregate AiUsageRecords with CreatedAt from the most recent Monday (UTC) to now.
4. THE Cost_Summary for "this month" SHALL aggregate AiUsageRecords with CreatedAt from the first day of the current UTC month to now.
5. THE Cost_Summary for "all-time" SHALL aggregate all AiUsageRecords for the campaign (subject to role-based filtering).
6. EACH Cost_Summary SHALL include: total input tokens, total output tokens, total tokens, total estimated cost in USD, and total operation count.

### Requirement 4: Usage Breakdown by Campaign

**User Story:** As a system operator with multiple campaigns, I want to see how costs are distributed across campaigns, so that I can identify which campaigns consume the most AI resources.

#### Acceptance Criteria

1. WHEN a GM requests cost data, THE Cost_Endpoint SHALL provide a breakdown of Cost_Summary values grouped by campaign.
2. THE Cost_Service SHALL only include campaigns where the requesting user is a member with GM role.
3. EACH campaign grouping SHALL include the campaign ID, campaign name, and its associated Cost_Summary.

### Requirement 5: Usage Breakdown by User

**User Story:** As a GM, I want to see cost breakdowns per user within my campaign, so that I can understand individual usage patterns.

#### Acceptance Criteria

1. WHEN a GM requests usage by user, THE Cost_Endpoint SHALL return a list of Cost_Summary values grouped by user within the specified campaign.
2. EACH user grouping SHALL include the user ID, username, and the associated Cost_Summary.
3. WHEN a Player or Observer requests usage by user, THE Cost_Endpoint SHALL return only the requesting user's Cost_Summary.
4. THE Cost_Endpoint SHALL support an optional Date_Range_Filter to restrict the aggregation window.

### Requirement 6: Usage Breakdown by Operation Type

**User Story:** As a campaign member, I want to see costs broken down by AI operation type, so that I can understand which operations consume the most tokens.

#### Acceptance Criteria

1. THE Cost_Endpoint SHALL provide a breakdown of Cost_Summary values grouped by AiOperationType.
2. THE breakdown SHALL include entries for each AiOperationType that has at least one AiUsageRecord matching the filters.
3. EACH operation type grouping SHALL include the operation type name and its associated Cost_Summary.
4. THE Cost_Endpoint SHALL support an optional Date_Range_Filter to restrict the aggregation window.
5. THE breakdown SHALL respect role-based visibility (GMs see all campaign usage; Players and Observers see only their own).

### Requirement 7: Usage Breakdown by Model

**User Story:** As a campaign member, I want to see costs broken down by AI model, so that I can understand which models are being used and their relative costs.

#### Acceptance Criteria

1. THE Cost_Endpoint SHALL provide a breakdown of Cost_Summary values grouped by model name.
2. THE breakdown SHALL include entries for each distinct model that has at least one AiUsageRecord matching the filters.
3. EACH model grouping SHALL include the model name and its associated Cost_Summary.
4. THE Cost_Endpoint SHALL support an optional Date_Range_Filter to restrict the aggregation window.
5. THE breakdown SHALL respect role-based visibility (GMs see all campaign usage; Players and Observers see only their own).

### Requirement 8: Date Range Filtering

**User Story:** As a campaign member, I want to filter cost data by arbitrary date ranges, so that I can examine usage during specific time periods.

#### Acceptance Criteria

1. THE Cost_Endpoint SHALL accept optional startDate and endDate query parameters as DateTimeOffset values on breakdown endpoints.
2. WHEN startDate is provided, THE Cost_Service SHALL include only AiUsageRecords with CreatedAt at or after the startDate.
3. WHEN endDate is provided, THE Cost_Service SHALL include only AiUsageRecords with CreatedAt at or before the endDate.
4. WHEN both startDate and endDate are provided, THE Cost_Service SHALL validate that startDate is before or equal to endDate.
5. IF startDate is after endDate, THEN THE Cost_Endpoint SHALL return HTTP 400 with a descriptive validation error.
6. WHEN neither startDate nor endDate is provided, THE Cost_Service SHALL include all matching AiUsageRecords without date restriction.

### Requirement 9: Token Breakdown in Aggregations

**User Story:** As a campaign member, I want to see input tokens and output tokens separately in every cost aggregation, so that I can understand the token distribution of AI operations.

#### Acceptance Criteria

1. EVERY Cost_Summary returned by the Cost_Endpoint SHALL include separate fields for total input tokens and total output tokens.
2. EVERY Cost_Summary returned by the Cost_Endpoint SHALL include a total tokens field representing the sum of input and output tokens.
3. THE Cost_Service SHALL compute token totals by summing InputTokens and OutputTokens from matching AiUsageRecords.

### Requirement 10: Estimated Dollar Totals

**User Story:** As a campaign member, I want to see estimated dollar costs for AI usage, so that I can understand the financial impact of campaign AI operations.

#### Acceptance Criteria

1. EVERY Cost_Summary returned by the Cost_Endpoint SHALL include a total estimated cost in USD.
2. THE Cost_Service SHALL compute estimated cost by summing EstimatedCostUsd from matching AiUsageRecords.
3. THE Cost_Service SHALL use pre-calculated EstimatedCostUsd values stored on each AiUsageRecord rather than recalculating from Model_Pricing at query time.

### Requirement 11: Error Handling

**User Story:** As a campaign member, I want clear error responses when the cost dashboard encounters problems, so that I understand what went wrong.

#### Acceptance Criteria

1. IF an unexpected error occurs during cost data aggregation, THEN THE Cost_Endpoint SHALL return HTTP 500 with a generic error message and log the full error context.
2. IF the campaignId route parameter is not a valid GUID, THEN THE Cost_Endpoint SHALL return HTTP 404.
3. THE Cost_Endpoint SHALL never expose stack traces or internal error details in error responses.

### Requirement 12: Observability

**User Story:** As a system operator, I want cost dashboard requests to be logged and metered, so that I can monitor usage and performance of the cost feature itself.

#### Acceptance Criteria

1. THE Cost_Endpoint SHALL emit structured logs including: correlation ID, user ID, campaign ID, and endpoint accessed.
2. THE Cost_Service SHALL log the aggregation duration for monitoring query performance.
3. THE Cost_Endpoint SHALL never log sensitive user data beyond user ID and campaign membership context.
