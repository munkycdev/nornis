# Implementation Plan: Cost Dashboard

## Overview

This plan implements the Cost Dashboard API — a set of endpoints that aggregate and serve AI token usage and estimated cost data from `AiUsageRecords`. The implementation follows clean architecture: `CostsController` in the API layer delegates to `CostService` in the Application layer, which orchestrates role-based filtering, date range validation, time period calculation, and delegation to aggregation methods on `IAiUsageRecordRepository`. The repository uses EF Core LINQ projections to perform SQL-side aggregation.

Key components:
- **API**: `CostsController`, response DTOs (`CostSummaryResponse`, `TimePeriodSummaryResponse`, etc.)
- **Application**: `ICostService`/`CostService`, aggregation result models (`CostSummary`, `TimePeriodCostResult`, etc.), `TimePeriodCalculator`
- **Domain**: Extended `IAiUsageRecordRepository` with aggregation query methods
- **Infrastructure**: Extended `AiUsageRecordRepository` with EF Core LINQ aggregation implementations

Existing domain entities (`AiUsageRecord`, `CampaignMember`, `Campaign`), enums (`CampaignRole`, `AiOperationType`, `VisibilityScope`), repository interfaces (`ICampaignMemberRepository`, `ICampaignRepository`), `CampaignMemberActionFilter`, `UserProvisioningMiddleware`, and the `AppResult` pattern are already in place.

## Tasks

- [x] 1. Define application models and interfaces
  - [x] 1.1 Create aggregation result models in `Nornis.Application/Models/`
    - Create `CostSummary` record with TotalInputTokens (long), TotalOutputTokens (long), TotalTokens (long), TotalEstimatedCostUsd (decimal), OperationCount (int), and a static `Empty` property
    - Create `GroupedCostSummary<TKey>` record with Key and Summary properties
    - Create `TimePeriodCostResult` record with Today, ThisWeek, ThisMonth, AllTime (all CostSummary)
    - Create `CampaignCostResult` record with CampaignId, CampaignName, Summary
    - Create `UserCostResult` record with UserId, Username, Summary
    - Create `OperationTypeCostResult` record with OperationType, Summary
    - Create `ModelCostResult` record with Model, Summary
    - _Requirements: 3.6, 4.3, 5.2, 6.3, 7.3, 9.1, 9.2, 9.3, 10.1_

  - [x] 1.2 Create `ICostService` interface in `Nornis.Application/Services/`
    - Define `GetSummaryAsync(Guid campaignId, Guid userId, CampaignRole role, CancellationToken ct)` returning `AppResult<TimePeriodCostResult>`
    - Define `GetByCampaignAsync(Guid userId, CancellationToken ct)` returning `AppResult<IReadOnlyList<CampaignCostResult>>`
    - Define `GetByUserAsync(Guid campaignId, Guid userId, CampaignRole role, DateTimeOffset? startDate, DateTimeOffset? endDate, CancellationToken ct)` returning `AppResult<IReadOnlyList<UserCostResult>>`
    - Define `GetByOperationTypeAsync(Guid campaignId, Guid userId, CampaignRole role, DateTimeOffset? startDate, DateTimeOffset? endDate, CancellationToken ct)` returning `AppResult<IReadOnlyList<OperationTypeCostResult>>`
    - Define `GetByModelAsync(Guid campaignId, Guid userId, CampaignRole role, DateTimeOffset? startDate, DateTimeOffset? endDate, CancellationToken ct)` returning `AppResult<IReadOnlyList<ModelCostResult>>`
    - _Requirements: 2.1, 2.2, 3.1, 4.1, 5.1, 6.1, 7.1, 8.1_

  - [x] 1.3 Create `TimePeriodCalculator` static helper in `Nornis.Application/Services/`
    - Implement `GetTodayRange()` returning (DateTimeOffset Start, DateTimeOffset End) for current UTC date start to now
    - Implement `GetThisWeekRange()` returning start from most recent Monday UTC to now
    - Implement `GetThisMonthRange()` returning start from first day of current UTC month to now
    - _Requirements: 3.2, 3.3, 3.4_

- [x] 2. Extend repository interface and implementation
  - [x] 2.1 Add aggregation methods to `IAiUsageRecordRepository` in `Nornis.Domain/Repositories/`
    - Add `AggregateAsync(Guid campaignId, Guid? userId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken ct)` returning `CostSummary`
    - Add `AggregateByOperationTypeAsync(Guid campaignId, Guid? userId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken ct)` returning `IReadOnlyList<GroupedCostSummary<string>>`
    - Add `AggregateByModelAsync(Guid campaignId, Guid? userId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken ct)` returning `IReadOnlyList<GroupedCostSummary<string>>`
    - Add `AggregateByUserAsync(Guid campaignId, Guid? userId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken ct)` returning `IReadOnlyList<GroupedCostSummary<Guid>>`
    - Add `AggregateByCampaignAsync(IReadOnlyList<Guid> campaignIds, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken ct)` returning `IReadOnlyList<GroupedCostSummary<Guid>>`
    - _Requirements: 3.1, 4.1, 5.1, 6.1, 7.1, 9.2, 9.3, 10.2_

  - [x] 2.2 Implement aggregation methods in `AiUsageRecordRepository` in `Nornis.Infrastructure/`
    - Implement `AggregateAsync` using EF Core LINQ: filter by campaignId, optional userId, optional date range, then `GroupBy(_ => 1).Select(g => new CostSummary { ... })` with `Sum` and `Count`, return `CostSummary.Empty` when no records match
    - Implement `AggregateByOperationTypeAsync` using `GroupBy(r => r.OperationType.ToString())` with same filtering
    - Implement `AggregateByModelAsync` using `GroupBy(r => r.Model)` with same filtering
    - Implement `AggregateByUserAsync` using `GroupBy(r => r.UserId)` with same filtering
    - Implement `AggregateByCampaignAsync` using `Where(r => campaignIds.Contains(r.CampaignId)).GroupBy(r => r.CampaignId)` with optional date filtering
    - All aggregations must push SUM/GROUP BY to SQL via EF Core projections, not load records into memory
    - _Requirements: 3.1, 4.1, 5.1, 6.1, 7.1, 9.2, 9.3, 10.2_

- [x] 3. Implement CostService
  - [x] 3.1 Implement `CostService` in `Nornis.Application/Services/`
    - Inject `IAiUsageRecordRepository`, `ICampaignMemberRepository`, `ICampaignRepository`, `ILogger<CostService>`
    - Implement `GetSummaryAsync`: determine userId filter (GM → null, else → userId), compute time period boundaries via `TimePeriodCalculator`, call `AggregateAsync` four times (today, this week, this month, all-time), assemble `TimePeriodCostResult`, log aggregation duration
    - Implement `GetByCampaignAsync`: query `ICampaignMemberRepository` for all campaigns where user has GM role, get campaign IDs, call `AggregateByCampaignAsync`, resolve campaign names via `ICampaignRepository`, assemble `CampaignCostResult` list
    - Implement `GetByUserAsync`: validate date range (start <= end or return 400 error), determine userId filter, call `AggregateByUserAsync`, resolve usernames via `ICampaignMemberRepository`, assemble `UserCostResult` list
    - Implement `GetByOperationTypeAsync`: validate date range, determine userId filter, call `AggregateByOperationTypeAsync`, assemble `OperationTypeCostResult` list
    - Implement `GetByModelAsync`: validate date range, determine userId filter, call `AggregateByModelAsync`, assemble `ModelCostResult` list
    - For non-GM roles in `GetByUserAsync`, return only the requesting user's summary
    - Log aggregation duration for all methods using `Stopwatch` and structured logging
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 4.1, 4.2, 5.1, 5.2, 5.3, 6.1, 6.2, 6.3, 6.5, 7.1, 7.2, 7.3, 7.5, 8.2, 8.3, 8.4, 8.5, 8.6, 9.2, 9.3, 10.2, 12.2_

  - [x] 3.2 Write property test for role-based record filtering
    - **Property 1: Role-Based Record Filtering**
    - Generate a campaign with AiUsageRecords from multiple users; invoke CostService methods as GM vs Player vs Observer; assert GM results include all users' records while Player/Observer results include only the requesting user's records
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 5.3, 6.5, 7.5**

  - [x] 3.3 Write property test for aggregation sum correctness
    - **Property 2: Aggregation Sum Correctness**
    - Generate a non-empty set of AiUsageRecords matching filters; call AggregateAsync through CostService; assert TotalInputTokens = sum of InputTokens, TotalOutputTokens = sum of OutputTokens, TotalTokens = sum of TotalTokens, TotalEstimatedCostUsd = sum of EstimatedCostUsd, OperationCount = count of records
    - **Validates: Requirements 3.6, 9.2, 9.3, 10.2**

  - [x] 3.4 Write property test for date range filtering correctness
    - **Property 3: Date Range Filtering Correctness**
    - Generate AiUsageRecords with random CreatedAt values and a date range [start, end]; call CostService with date filters; assert aggregation includes exactly those records where CreatedAt >= start AND CreatedAt <= end
    - **Validates: Requirements 3.2, 3.3, 3.4, 3.5, 5.4, 6.4, 7.4, 8.2, 8.3**

  - [x] 3.5 Write property test for grouping produces correct partitions
    - **Property 4: Grouping Produces Correct Partitions**
    - Generate AiUsageRecords with multiple distinct operation types, models, and users; call GetByOperationType/GetByModel/GetByUser; assert exactly one entry per distinct key, sum of OperationCount across groups equals total record count, no group has zero OperationCount
    - **Validates: Requirements 4.1, 5.1, 6.1, 6.2, 7.1, 7.2**

  - [x] 3.6 Write property test for date range validation
    - **Property 5: Date Range Validation**
    - Generate pairs of DateTimeOffset where startDate > endDate; call CostService breakdown methods; assert validation error (400) returned. Generate pairs where startDate <= endDate; assert aggregation proceeds successfully
    - **Validates: Requirements 8.4, 8.5**

  - [x] 3.7 Write property test for cross-campaign GM filtering
    - **Property 6: Cross-Campaign View Shows Only GM Campaigns**
    - Generate a user with memberships in multiple campaigns with mixed roles (GM, Player, Observer); call GetByCampaignAsync; assert only campaigns where user is GM appear in results
    - **Validates: Requirements 4.2**

- [x] 4. Checkpoint - CostService and property tests
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement API layer
  - [x] 5.1 Create response DTOs in `Nornis.Api/Contracts/Responses/`
    - Create `CostSummaryResponse` record with TotalInputTokens, TotalOutputTokens, TotalTokens, TotalEstimatedCostUsd, OperationCount
    - Create `TimePeriodSummaryResponse` record with Today, ThisWeek, ThisMonth, AllTime (all CostSummaryResponse)
    - Create `CampaignCostResponse` record with CampaignId, CampaignName, Summary (CostSummaryResponse)
    - Create `UserCostResponse` record with UserId, Username, Summary (CostSummaryResponse)
    - Create `OperationTypeCostResponse` record with OperationType, Summary (CostSummaryResponse)
    - Create `ModelCostResponse` record with Model, Summary (CostSummaryResponse)
    - _Requirements: 3.6, 4.3, 5.2, 6.3, 7.3, 9.1_

  - [x] 5.2 Create `CostsController` in `Nornis.Api/Controllers/`
    - Apply `[ApiController]`, `[Route("api/campaigns/{campaignId:guid}/costs")]`, `[ServiceFilter(typeof(CampaignMemberActionFilter))]`
    - Implement `GET summary` endpoint: extract user/member from HttpContext, call `GetSummaryAsync`, map to `TimePeriodSummaryResponse`
    - Implement `GET by-user` endpoint with optional `startDate`/`endDate` query params: extract user/member, call `GetByUserAsync`, map to list of `UserCostResponse`
    - Implement `GET by-operation` endpoint with optional date params: call `GetByOperationTypeAsync`, map to list of `OperationTypeCostResponse`
    - Implement `GET by-model` endpoint with optional date params: call `GetByModelAsync`, map to list of `ModelCostResponse`
    - Implement `GET ~/api/costs/by-campaign` cross-campaign endpoint (not scoped to single campaign, requires only authenticated user): call `GetByCampaignAsync`, map to list of `CampaignCostResponse`
    - Implement `MapError` helper: 400 → BadRequest with error code/message, default → 500 with generic message
    - Emit structured logs: correlation ID, user ID, campaign ID, endpoint accessed
    - Never expose stack traces or internal error details in responses
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 3.1, 4.1, 5.1, 5.4, 6.1, 6.4, 7.1, 7.4, 8.1, 8.5, 11.1, 11.2, 11.3, 12.1, 12.3_

  - [x] 5.3 Register `ICostService` → `CostService` in DI container
    - Register as scoped in `Program.cs` or service registration extension
    - _Requirements: 3.1_

- [x] 6. Checkpoint - API layer verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Unit tests
  - [x] 7.1 Write unit tests for `TimePeriodCalculator`
    - Test `GetTodayRange`: start is midnight UTC of current date, end is at or after start
    - Test `GetThisWeekRange`: start is most recent Monday UTC (when today is Monday → start is today; when today is Sunday → start is previous Monday)
    - Test `GetThisMonthRange`: start is first of current UTC month
    - Test boundary: year boundary (January 1st)
    - _Requirements: 3.2, 3.3, 3.4_

  - [x] 7.2 Write unit tests for `CostService` — role-based filtering
    - Test GM request passes null userId to repository (sees all users)
    - Test Player request passes userId to repository (sees only own data)
    - Test Observer request passes userId to repository (sees only own data)
    - Test GetByUserAsync as Player returns only one entry (own summary)
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 7.3 Write unit tests for `CostService` — date range validation and aggregation
    - Test startDate after endDate → 400 validation error with `invalid_date_range` code
    - Test startDate equal to endDate → proceeds normally
    - Test both null → no date restriction
    - Test only startDate provided → filters from startDate
    - Test only endDate provided → filters to endDate
    - Test empty campaign → all CostSummary fields are zero
    - _Requirements: 8.2, 8.3, 8.4, 8.5, 8.6_

  - [x] 7.4 Write unit tests for `CostService` — cross-campaign
    - Test GetByCampaignAsync queries only campaigns where user is GM
    - Test user with no GM campaigns returns empty list
    - Test campaign names resolved correctly
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 7.5 Write unit tests for `CostsController`
    - Test valid summary request → 200 with `TimePeriodSummaryResponse` structure
    - Test CampaignMemberActionFilter applied (non-member → 403)
    - Test invalid date range → 400 with descriptive message
    - Test invalid campaignId format → 404 (route constraint)
    - Test internal error → 500 with generic message, no stack traces
    - Test cross-campaign endpoint accessible without campaign-scoped filter
    - _Requirements: 1.1, 1.2, 1.3, 8.5, 11.1, 11.2, 11.3, 12.1_

- [x] 8. Checkpoint - Unit tests verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Integration tests
  - [x] 9.1 Write integration tests for authorization and visibility
    - Test non-member request → 403 without revealing campaign existence
    - Test missing JWT → 401
    - Test GM sees aggregated data for all users in campaign
    - Test Player sees only their own usage data
    - Test Observer sees only their own usage data
    - Test cross-campaign endpoint returns only GM-role campaigns
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 2.4, 4.2_

  - [x] 9.2 Write integration tests for full request pipeline
    - Test GET summary with seeded AiUsageRecords → correct aggregation values
    - Test GET by-user → correct per-user grouping
    - Test GET by-operation → correct per-operation-type grouping
    - Test GET by-model → correct per-model grouping
    - Test date range filtering produces correct subset
    - Test empty campaign → all zeros (not error)
    - Test SQL-side aggregation matches manual sum for representative data
    - _Requirements: 3.1, 3.6, 5.1, 6.1, 7.1, 8.2, 8.3, 9.2, 9.3, 10.2_

- [x] 10. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Existing domain entities (`AiUsageRecord`, `CampaignMember`, `Campaign`), enums (`CampaignRole`, `AiOperationType`), and repository interfaces (`IAiUsageRecordRepository`, `ICampaignMemberRepository`, `ICampaignRepository`) already exist
- The existing `CampaignMemberActionFilter`, `UserProvisioningMiddleware`, and Auth0 JWT middleware are already in place
- The existing `AppResult` pattern is used for service return types throughout the application layer
- `IAiUsageRecordRepository` already has `CreateAsync` and `QueryAsync` methods — the new aggregation methods extend the existing interface
- Property tests use FsCheck with NUnit (minimum 100 iterations per property)
- Property test tag format: `Feature: cost-dashboard, Property {N}: {title}`
- Use realistic test data: campaigns "Black Harbor Investigation" and "Silver Key Mystery", users Kelda (GM), Tavrin (Player), Jorin (Observer)
- The cross-campaign endpoint (`GET /api/costs/by-campaign`) requires only authenticated user, not campaign-scoped membership — it internally queries all campaigns where user holds GM role
- All aggregation happens SQL-side via EF Core LINQ projections — no in-memory collection of all records
- `EstimatedCostUsd` is pre-calculated on each `AiUsageRecord` — the service sums existing values rather than recalculating from model pricing

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3"] },
    { "id": 1, "tasks": ["2.1"] },
    { "id": 2, "tasks": ["2.2"] },
    { "id": 3, "tasks": ["3.1"] },
    { "id": 4, "tasks": ["3.2", "3.3", "3.4", "3.5", "3.6", "3.7"] },
    { "id": 5, "tasks": ["5.1", "5.2"] },
    { "id": 6, "tasks": ["5.3"] },
    { "id": 7, "tasks": ["7.1", "7.2", "7.3", "7.4", "7.5"] },
    { "id": 8, "tasks": ["9.1", "9.2"] }
  ]
}
```
