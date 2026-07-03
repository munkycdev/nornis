# Requirements Document

## Introduction

This feature establishes the authentication and authorization pipeline for the Nornis API, along with Campaign CRUD operations and campaign membership management. It covers Auth0 JWT validation, automatic user provisioning from JWT claims, campaign creation and settings management, member management with role assignment (GM, Player, Observer), and campaign-scoped authorization middleware that enforces membership and role checks on all campaign-scoped endpoints.

## Glossary

- **API**: The Nornis ASP.NET Core backend service (nornis-api) that handles all authenticated requests
- **Auth0_JWT**: A JSON Web Token issued by Auth0 after successful authentication via Discord
- **User**: A Nornis domain entity representing an authenticated individual, linked to Auth0 via Auth0SubjectId
- **Campaign**: The root collaboration and authorization boundary in Nornis, owned by a creating user
- **CampaignMember**: A record associating a User with a Campaign and assigning a CampaignRole
- **CampaignRole**: One of GM, Player, or Observer — determines what actions a member may perform within a campaign
- **GM**: Game Master role — full campaign management, member management, and content visibility
- **Player**: Player role — can create sources, view PartyVisible content, manage own Private content
- **Observer**: Observer role — read-only access to PartyVisible content
- **User_Provisioning**: The process of resolving an existing Nornis User or creating a new one from Auth0 JWT claims
- **Campaign_Scoped_Endpoint**: An API endpoint that operates on a resource belonging to a specific campaign
- **Authorization_Policy**: An ASP.NET Core policy that enforces access rules based on authentication state, campaign membership, and role

## Requirements

### Requirement 1: Auth0 JWT Authentication Pipeline

**User Story:** As an API consumer, I want all requests to be authenticated via Auth0 JWT, so that only verified users can access protected resources.

#### Acceptance Criteria

1. THE API SHALL validate Auth0 JWT bearer tokens on all incoming requests by default, verifying the token signature, issuer, audience, expiration, and structural integrity
2. WHEN a request arrives without an Auth0 JWT, or with a malformed token that cannot be parsed as a valid JWT, THE API SHALL return HTTP 401 Unauthorized
3. WHEN a request arrives with an expired Auth0 JWT, THE API SHALL return HTTP 401 Unauthorized
4. WHEN a request arrives with an Auth0 JWT issued by an untrusted authority, THE API SHALL return HTTP 401 Unauthorized
5. WHERE the endpoint is GET /health, THE API SHALL allow anonymous access without a JWT
6. WHEN a request arrives with a valid Auth0 JWT, THE API SHALL extract the subject identifier from the token sub claim and make it available to downstream request handlers for the duration of the request

### Requirement 2: User Provisioning from JWT Claims

**User Story:** As a new user signing in for the first time, I want Nornis to automatically create my user record from my Auth0 claims, so that I can immediately use the application without manual registration.

#### Acceptance Criteria

1. WHEN a request arrives with a valid Auth0 JWT, THE API SHALL resolve the Nornis User matching the Auth0 subject identifier (sub claim) from the token claims
2. WHEN no Nornis User exists for the Auth0 subject identifier, THE API SHALL create a new User with the Auth0SubjectId set to the sub claim value, Username set to the nickname claim value (falling back to the sub claim value if nickname is absent), and Email set to the email claim value
3. WHEN a Nornis User already exists for the Auth0 subject identifier, THE API SHALL use the existing User record for the request context without modifying it
4. THE API SHALL derive the user identity exclusively from validated JWT claims and SHALL NOT accept client-provided user identifiers for authorization purposes
5. THE API SHALL make the resolved Nornis User available to downstream request handlers for the duration of the request
6. IF the JWT does not contain a valid email claim, THEN THE API SHALL reject the request with HTTP 401 Unauthorized
7. IF user provisioning fails due to a concurrent request for the same Auth0 subject identifier, THEN THE API SHALL retrieve the existing User record created by the concurrent request and use it for the request context
8. IF user provisioning fails due to a database or infrastructure error, THEN THE API SHALL return HTTP 503 Service Unavailable

### Requirement 3: Campaign Creation

**User Story:** As an authenticated user, I want to create a new campaign, so that I can organize a collaborative space for my tabletop game.

#### Acceptance Criteria

1. WHEN an authenticated user submits a campaign creation request with a Name that is between 1 and 100 characters and is not blank or whitespace-only, THE API SHALL create a new Campaign and return the created Campaign details including Id, Name, Description, GameSystem, CreatedByUserId, CreatedAt, and UpdatedAt
2. WHEN an authenticated user creates a Campaign, THE API SHALL automatically add the creating user as a CampaignMember with the GM role
3. IF a campaign creation request has a missing, empty, or whitespace-only Name field, or a Name exceeding 100 characters, THEN THE API SHALL return HTTP 400 Bad Request with a validation error indicating the Name constraint that was violated
4. THE API SHALL set the CreatedByUserId to the resolved Nornis User identifier from the JWT claims
5. THE API SHALL set CreatedAt and UpdatedAt timestamps at the time of Campaign creation
6. WHEN a campaign creation request includes optional Description or GameSystem fields, THE API SHALL store the provided values on the created Campaign

### Requirement 4: Campaign Settings Update

**User Story:** As a GM, I want to update campaign settings like name, description, and game system, so that I can keep campaign information current.

#### Acceptance Criteria

1. WHEN a GM submits a campaign update request with one or more updatable fields (Name, Description, GameSystem), THE API SHALL update only the specified fields and return the updated Campaign details including Id, Name, Description, GameSystem, CreatedAt, and UpdatedAt
2. WHEN a non-GM member submits a campaign update request, THE API SHALL return HTTP 403 Forbidden
3. WHEN a non-member submits a campaign update request, THE API SHALL return HTTP 403 Forbidden
4. WHEN a campaign update request references a Campaign that does not exist, THE API SHALL return HTTP 404 Not Found
5. THE API SHALL update the UpdatedAt timestamp when Campaign settings are modified
6. IF a campaign update request provides a Name that is empty, whitespace-only, or exceeds 100 characters, THEN THE API SHALL return HTTP 400 Bad Request with a validation error

### Requirement 5: Campaign Member Management

**User Story:** As a GM, I want to add members to my campaign and assign roles, so that players and observers can participate according to their permissions.

#### Acceptance Criteria

1. WHEN a GM submits a request to add a user to a campaign with a specified CampaignRole (GM, Player, or Observer), THE API SHALL create a CampaignMember record and return the membership details including the member's Id, CampaignId, UserId, Role, DisplayName, CharacterName, and JoinedAt
2. WHEN a non-GM member submits a request to add, remove, or change the role of a member, THE API SHALL return HTTP 403 Forbidden
3. WHEN a GM submits a request to add a user who is already a member of the campaign, THE API SHALL return HTTP 409 Conflict
4. WHEN a GM submits a request to add a user that does not exist in the Nornis User table, THE API SHALL return HTTP 404 Not Found
5. WHEN a GM submits a request to remove a member from a campaign, THE API SHALL delete the CampaignMember record and return HTTP 204 No Content
6. WHEN a GM submits a request to change a member's role, THE API SHALL update the CampaignMember role and return the updated membership details including the member's Id, CampaignId, UserId, Role, DisplayName, CharacterName, and JoinedAt
7. IF a GM submits a request to remove or change the role of the last GM in a campaign such that no GM would remain, THEN THE API SHALL return HTTP 409 Conflict with an error message indicating the campaign must retain at least one GM
8. IF a GM submits a request to add a member with a CampaignRole value that is not GM, Player, or Observer, THEN THE API SHALL return HTTP 400 Bad Request with a validation error indicating the allowed role values

### Requirement 6: Campaign Membership Listing

**User Story:** As a campaign member, I want to see who else is in my campaign, so that I know my fellow players and their roles.

#### Acceptance Criteria

1. WHEN an authenticated campaign member requests the member list for a campaign, THE API SHALL return the list of all CampaignMembers for that campaign including the requesting user, with each entry containing the member's UserId, CampaignRole, DisplayName, CharacterName, and JoinedAt
2. IF a non-member requests the member list for a campaign, THEN THE API SHALL return HTTP 403 Forbidden
3. IF a member requests the member list for a campaign that does not exist, THEN THE API SHALL return HTTP 404 Not Found
4. WHEN an authenticated campaign member requests the member list, THE API SHALL return members ordered by JoinedAt ascending

### Requirement 7: Campaign Listing for Current User

**User Story:** As an authenticated user, I want to see all campaigns I belong to, so that I can navigate to the appropriate campaign.

#### Acceptance Criteria

1. WHEN an authenticated user requests their campaign list, THE API SHALL return all Campaigns where the user is a CampaignMember, including for each campaign the Campaign Id, Name, Description, GameSystem, and the user's CampaignRole
2. IF the authenticated user is not a member of any campaign, THEN THE API SHALL return an empty list
3. THE API SHALL NOT include campaigns where the user is not a member

### Requirement 8: Campaign-Scoped Authorization

**User Story:** As a system operator, I want all campaign-scoped endpoints to enforce membership and role checks, so that unauthorized users cannot access or modify campaign resources.

#### Acceptance Criteria

1. WHEN a request targets a Campaign_Scoped_Endpoint, THE API SHALL verify that the resolved Nornis User is a CampaignMember of the target campaign before executing any operation logic
2. IF the resolved User is not a CampaignMember of the target campaign, THEN THE API SHALL return HTTP 403 Forbidden
3. WHEN a Campaign_Scoped_Endpoint requires a minimum CampaignRole, THE API SHALL verify the member's role is equal to or higher than the required role using the hierarchy GM > Player > Observer before executing the operation
4. IF the member's CampaignRole is lower than the minimum required role for the operation, THEN THE API SHALL return HTTP 403 Forbidden
5. IF a non-member targets a Campaign_Scoped_Endpoint for a campaign that does not exist, THEN THE API SHALL return HTTP 403 Forbidden rather than HTTP 404 Not Found
6. IF a campaign member targets a Campaign_Scoped_Endpoint for a campaign that does not exist, THEN THE API SHALL return HTTP 404 Not Found

### Requirement 9: Campaign Detail Retrieval

**User Story:** As a campaign member, I want to retrieve details about my campaign, so that I can see the campaign name, description, and game system.

#### Acceptance Criteria

1. WHEN a campaign member requests campaign details by ID, THE API SHALL return the Campaign record including Name, Description, GameSystem, CreatedAt, and UpdatedAt
2. WHEN a non-member requests campaign details, THE API SHALL return HTTP 403 Forbidden regardless of whether the campaign exists
3. WHEN an authenticated campaign member requests details for a Campaign that does not exist, THE API SHALL return HTTP 404 Not Found
4. WHEN a campaign member requests campaign details, THE API SHALL include the requesting member's CampaignRole in the response
