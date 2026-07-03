# Implementation Plan: Auth and Campaigns

## Overview

This plan implements the authentication pipeline (Auth0 JWT validation, user provisioning middleware), campaign CRUD operations, campaign member management, and campaign-scoped authorization for the Nornis API. The domain entities, enums, repository interfaces, and EF Core implementations already exist. This plan focuses on the Application services, API layer (middleware, filters, controllers, DTOs), and tests.

## Tasks

- [x] 1. Set up Application layer foundation
  - [x] 1.1 Create AppResult and AppError types in `Nornis.Application/Errors/`
    - Implement `AppError` record with StatusCode, Code, and Message
    - Implement `AppResult<T>` and `AppResult` classes with Success/Fail factory methods
    - _Requirements: All (used across all operations for consistent error handling)_

  - [x] 1.2 Create CampaignRoleExtensions in `Nornis.Application/Authorization/`
    - Implement `Rank()` extension method: GM=3, Player=2, Observer=1
    - Implement `IsAtLeast(CampaignRole required)` comparison method
    - _Requirements: 8.3, 8.4_

  - [x] 1.3 Create Application layer command and DTO models in `Nornis.Application/Models/`
    - Implement `CreateCampaignCommand`, `UpdateCampaignCommand`, `AddMemberCommand`, `UpdateMemberRoleCommand`, and `CampaignWithRoleDto` records
    - _Requirements: 3.1, 4.1, 5.1, 5.6, 7.1_

  - [x] 1.4 Add `UpdateAsync` method to `ICampaignMemberRepository` interface and implementation
    - Add `Task<CampaignMember> UpdateAsync(CampaignMember member, CancellationToken ct)` to the domain interface
    - Implement in `CampaignMemberRepository`
    - Add `CountByRoleAsync(Guid campaignId, CampaignRole role, CancellationToken ct)` for last-GM protection
    - _Requirements: 5.6, 5.7_

- [x] 2. Implement Campaign Application Service
  - [x] 2.1 Create `ICampaignService` interface and `CampaignService` implementation in `Nornis.Application/Services/`
    - Implement `CreateAsync` — validate name (1–100 chars, non-blank), create Campaign, auto-add creator as GM member, return Campaign
    - Implement `GetByIdAsync` — verify membership, return campaign with requesting member's role
    - Implement `UpdateAsync` — verify GM role, validate name if provided, update specified fields and UpdatedAt
    - Implement `ListForUserAsync` — return campaigns with user's role for each
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 7.1, 7.2, 7.3, 9.1, 9.2, 9.3, 9.4_

  - [x] 2.2 Write property test: Campaign Creation Field Mapping
    - **Property 3: Campaign Creation Field Mapping**
    - **Validates: Requirements 3.1, 3.4, 3.5, 3.6**

  - [x] 2.3 Write property test: Campaign Creator Becomes GM
    - **Property 4: Campaign Creator Becomes GM**
    - **Validates: Requirements 3.2**

  - [x] 2.4 Write property test: Campaign Name Validation Rejects Invalid Names
    - **Property 5: Campaign Name Validation Rejects Invalid Names**
    - **Validates: Requirements 3.3, 4.6**

  - [x] 2.5 Write property test: Campaign Update Modifies Only Specified Fields
    - **Property 6: Campaign Update Modifies Only Specified Fields**
    - **Validates: Requirements 4.1, 4.5**

- [x] 3. Implement Campaign Member Application Service
  - [x] 3.1 Create `ICampaignMemberService` interface and `CampaignMemberService` implementation in `Nornis.Application/Services/`
    - Implement `AddMemberAsync` — verify acting user is GM, check target user exists, check not already a member, validate role value, create CampaignMember
    - Implement `RemoveMemberAsync` — verify GM role, check last-GM protection, remove member
    - Implement `UpdateRoleAsync` — verify GM role, check last-GM protection when downgrading a GM, update role
    - Implement `ListMembersAsync` — verify requesting user is a member, return members ordered by JoinedAt ascending
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 6.1, 6.2, 6.3, 6.4_

  - [x] 3.2 Write property test: Non-GM Operations Are Denied
    - **Property 7: Non-GM Operations Are Denied**
    - **Validates: Requirements 4.2, 5.2**

  - [x] 3.3 Write property test: Role Hierarchy Enforcement
    - **Property 8: Role Hierarchy Enforcement**
    - **Validates: Requirements 8.3, 8.4**

  - [x] 3.4 Write property test: Member List Ordering
    - **Property 10: Member List Ordering**
    - **Validates: Requirements 6.1, 6.4**

  - [x] 3.5 Write property test: Campaign Listing Completeness and Exclusivity
    - **Property 11: Campaign Listing Completeness and Exclusivity**
    - **Validates: Requirements 7.1, 7.2, 7.3**

- [x] 4. Checkpoint - Application layer verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement Auth0 JWT Authentication in API layer
  - [x] 5.1 Create `Auth0Extensions.cs` in `Nornis.Api/Authentication/`
    - Add NuGet package `Microsoft.AspNetCore.Authentication.JwtBearer`
    - Implement `AddAuth0Authentication` extension method on `IServiceCollection`
    - Configure JWT bearer with Auth0 domain, audience, issuer validation, and token validation parameters
    - Set `FallbackPolicy = RequireAuthenticatedUser` so all endpoints require auth by default
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 5.2 Create `UserProvisioningMiddleware` in `Nornis.Api/Middleware/`
    - Implement middleware that resolves or creates Nornis User from JWT claims (sub, email, nickname)
    - Skip unauthenticated requests (pass through to next middleware)
    - Return 401 if sub claim or email claim is missing
    - Handle concurrent creation race (catch DbUpdateException, retry lookup)
    - Return 503 on infrastructure failures
    - Store resolved User in `HttpContext.Items["NornisUser"]`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8_

  - [x] 5.3 Create `HttpContextExtensions` in `Nornis.Api/Extensions/`
    - Implement `GetNornisUser(this HttpContext)` to retrieve user from Items
    - Implement `GetCampaignMember(this HttpContext)` to retrieve campaign member from Items
    - _Requirements: 2.5, 8.1_

  - [x] 5.4 Write property test: User Provisioning Idempotence
    - **Property 1: User Provisioning Idempotence**
    - **Validates: Requirements 2.1, 2.3**

  - [x] 5.5 Write property test: User Provisioning Creates Correct User from Claims
    - **Property 2: User Provisioning Creates Correct User from Claims**
    - **Validates: Requirements 2.2**

- [x] 6. Implement Campaign-Scoped Authorization Filter
  - [x] 6.1 Create `CampaignMemberFilter` in `Nornis.Api/Filters/`
    - Implement `IEndpointFilter` that extracts `{campaignId}` from route
    - Look up CampaignMember for the resolved user and campaign
    - Return 403 Forbidden if user is not a member (regardless of campaign existence per Req 8.5)
    - Store resolved CampaignMember in `HttpContext.Items["CampaignMember"]`
    - _Requirements: 8.1, 8.2, 8.5_

  - [x] 6.2 Write property test: Non-Member Access Denied
    - **Property 9: Non-Member Access Denied**
    - **Validates: Requirements 8.1, 8.2, 8.5**

- [x] 7. Implement API Request/Response Contracts
  - [x] 7.1 Create request DTOs in `Nornis.Api/Contracts/Requests/`
    - Implement `CreateCampaignRequest`, `UpdateCampaignRequest`, `AddCampaignMemberRequest`, `UpdateCampaignMemberRoleRequest` as records
    - _Requirements: 3.1, 4.1, 5.1, 5.6_

  - [x] 7.2 Create response DTOs in `Nornis.Api/Contracts/Responses/`
    - Implement `CampaignResponse`, `CampaignListItemResponse`, `CampaignMemberResponse`, `ErrorResponse` as records
    - _Requirements: 3.1, 5.1, 6.1, 7.1, 9.1_

- [x] 8. Implement Campaign Endpoints
  - [x] 8.1 Create `CampaignsController` in `Nornis.Api/Controllers/`
    - `POST /api/campaigns` — Authenticated, validates request, calls CampaignService.CreateAsync, returns 201 with CampaignResponse
    - `GET /api/campaigns` — Authenticated, calls CampaignService.ListForUserAsync, returns list of CampaignListItemResponse
    - `GET /api/campaigns/{campaignId}` — Campaign member filter, calls CampaignService.GetByIdAsync, returns CampaignResponse with MyRole
    - `PUT /api/campaigns/{campaignId}` — Campaign member filter + GM check, calls CampaignService.UpdateAsync, returns CampaignResponse
    - Map AppResult errors to appropriate HTTP status codes (400, 403, 404, 409)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 7.1, 7.2, 7.3, 9.1, 9.2, 9.3, 9.4_

  - [x] 8.2 Create `CampaignMembersController` in `Nornis.Api/Controllers/`
    - `GET /api/campaigns/{campaignId}/members` — Campaign member filter, calls CampaignMemberService.ListMembersAsync, returns list of CampaignMemberResponse
    - `POST /api/campaigns/{campaignId}/members` — Campaign member filter + GM check, validates request, calls CampaignMemberService.AddMemberAsync, returns 201 with CampaignMemberResponse
    - `PUT /api/campaigns/{campaignId}/members/{userId}` — Campaign member filter + GM check, calls CampaignMemberService.UpdateRoleAsync, returns CampaignMemberResponse
    - `DELETE /api/campaigns/{campaignId}/members/{userId}` — Campaign member filter + GM check, calls CampaignMemberService.RemoveMemberAsync, returns 204
    - Map AppResult errors to appropriate HTTP status codes (400, 403, 404, 409)
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 6.1, 6.2, 6.3, 6.4_

- [x] 9. Wire up Program.cs and DI registration
  - [x] 9.1 Update `Program.cs` to register authentication, middleware, services, and map endpoints
    - Add Auth0 JWT authentication and authorization
    - Register `IUserRepository`, `ICampaignRepository`, `ICampaignMemberRepository` with DI
    - Register `ICampaignService`, `ICampaignMemberService` with DI
    - Add UserProvisioningMiddleware to the pipeline (after auth, before endpoints)
    - Mark `/health` as `[AllowAnonymous]`
    - Apply CampaignMemberFilter to campaign-scoped endpoint groups
    - _Requirements: 1.1, 1.5, 2.5, 8.1_

- [x] 10. Checkpoint - Full API layer verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Integration tests for authentication and authorization
  - [x] 11.1 Set up integration test infrastructure in `Nornis.Api.Tests`
    - Configure `WebApplicationFactory<Program>` with in-memory database provider
    - Create test JWT issuer helper to generate valid/invalid/expired tokens
    - Create helper methods for authenticated HTTP requests
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 11.2 Write integration tests for JWT authentication
    - Test valid token grants access
    - Test missing token returns 401
    - Test expired token returns 401
    - Test wrong issuer returns 401
    - Test anonymous access to `/health` succeeds
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6_

  - [x] 11.3 Write integration tests for user provisioning
    - Test new user is created from JWT claims (sub, nickname, email)
    - Test existing user is resolved without modification
    - Test missing email claim returns 401
    - Test nickname fallback to sub when nickname absent
    - _Requirements: 2.1, 2.2, 2.3, 2.5, 2.6_

  - [x] 11.4 Write integration tests for campaign CRUD endpoints
    - Test campaign creation returns 201 with correct fields
    - Test campaign creation with invalid name returns 400
    - Test campaign list returns only user's campaigns
    - Test campaign detail returns campaign with member's role
    - Test campaign update by GM succeeds
    - Test campaign update by non-GM returns 403
    - Test campaign update by non-member returns 403
    - Test campaign update for non-existent campaign returns 404
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 7.1, 7.2, 7.3, 9.1, 9.2, 9.3, 9.4_

  - [x] 11.5 Write integration tests for campaign member management endpoints
    - Test add member returns 201 with correct details
    - Test add member by non-GM returns 403
    - Test add duplicate member returns 409
    - Test add non-existent user returns 404
    - Test remove member returns 204
    - Test remove last GM returns 409
    - Test update member role returns updated details
    - Test list members returns ordered by JoinedAt ascending
    - Test list members by non-member returns 403
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 6.1, 6.2, 6.3, 6.4_

  - [x] 11.6 Write integration tests for campaign-scoped authorization
    - Test non-member accessing campaign endpoint returns 403
    - Test non-member accessing non-existent campaign returns 403 (not 404)
    - Test member accessing non-existent campaign returns 404
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

- [x] 12. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Domain entities, enums, repository interfaces, and EF Core implementations already exist — this plan builds the Application and API layers on top
- Property tests use FsCheck.NUnit with in-memory repository fakes for fast, deterministic execution
- Integration tests use `WebApplicationFactory<Program>` with an in-memory database and a test JWT issuer
- Checkpoints ensure incremental validation before progressing to the next layer
- The CampaignMemberFilter handles the non-member vs non-existent campaign distinction per Requirement 8.5/8.6

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.4"] },
    { "id": 1, "tasks": ["2.1", "3.1", "7.1", "7.2"] },
    { "id": 2, "tasks": ["2.2", "2.3", "2.4", "2.5", "3.2", "3.3", "3.4", "3.5"] },
    { "id": 3, "tasks": ["5.1", "5.2", "5.3"] },
    { "id": 4, "tasks": ["5.4", "5.5", "6.1"] },
    { "id": 5, "tasks": ["6.2", "8.1", "8.2"] },
    { "id": 6, "tasks": ["9.1"] },
    { "id": 7, "tasks": ["11.1"] },
    { "id": 8, "tasks": ["11.2", "11.3"] },
    { "id": 9, "tasks": ["11.4", "11.5", "11.6"] }
  ]
}
```
