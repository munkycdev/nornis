# Implementation Plan: Domain Data Layer

## Overview

This plan implements the core domain model, repository interfaces, EF Core persistence layer, and initial database migration for Nornis. Work proceeds from the innermost layer (Domain enums and entities) outward through repository interfaces, then into Infrastructure (DbContext, configurations, repositories, migration), and finally integration tests to validate correctness.

## Tasks

- [x] 1. Define domain enums
  - [x] 1.1 Create all domain enum files in Nornis.Domain/Enums
    - Create `CampaignRole`, `SourceType`, `SourceProcessingStatus`, `ArtifactType`, `ArtifactStatus`, `TruthState`, `VisibilityScope`, `ReviewBatchStatus`, `ReviewProposalStatus`, `ReviewChangeType`, `ReviewTargetType`, `SourceExtractionType`, `SourceReferenceTargetType`, `AiOperationType`, and `ConversationRole` enums
    - Each enum in its own file under `src/Nornis.Domain/Enums/`
    - Values exactly as specified in the design document
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 1.10, 1.11, 1.12, 1.13, 1.14, 1.15_

  - [x] 1.2 Write unit tests verifying enum definitions
    - Create `tests/Nornis.Domain.Tests/Enums/` test classes
    - Verify each enum has the expected set of values using reflection
    - Verify no unexpected values have been added
    - _Requirements: 1.1–1.15_

- [x] 2. Define domain entities
  - [x] 2.1 Create User and Campaign entities
    - Create `src/Nornis.Domain/Entities/User.cs` with Id, Auth0SubjectId, Username, Email, CreatedAt, UpdatedAt, RowVersion
    - Create `src/Nornis.Domain/Entities/Campaign.cs` with Id, Name, Description, GameSystem, CreatedAt, UpdatedAt, CreatedByUserId, RowVersion
    - Include navigation properties: Campaign → CampaignMembers collection, Campaign → CreatedByUser
    - Enable nullable reference types
    - Use DateTimeOffset for all timestamps
    - _Requirements: 2.1, 2.2, 2.13, 2.14, 3.1, 3.2_

  - [x] 2.2 Create CampaignMember and Source entities
    - Create `src/Nornis.Domain/Entities/CampaignMember.cs` with Id, CampaignId, UserId, Role, DisplayName, CharacterName, JoinedAt
    - Create `src/Nornis.Domain/Entities/Source.cs` with Id, CampaignId, Type, Title, Body, Uri, OccurredAt, CreatedAt, CreatedByUserId, Visibility, ProcessingStatus
    - Include navigation properties: CampaignMember → Campaign, User; Source → Campaign, CreatedByUser, SourceExtractions collection
    - _Requirements: 2.3, 2.4, 2.13, 2.14, 3.3, 3.4, 3.5_

  - [x] 2.3 Create SourceExtraction and Artifact entities
    - Create `src/Nornis.Domain/Entities/SourceExtraction.cs` with Id, SourceId, ExtractionType, Text, Confidence, CreatedAt
    - Create `src/Nornis.Domain/Entities/Artifact.cs` with Id, CampaignId, Type, Name, Summary, Visibility, Confidence, Status, CreatedAt, UpdatedAt, RowVersion
    - Include navigation properties: Artifact → Campaign, ArtifactFacts collection
    - _Requirements: 2.5, 2.6, 2.13, 2.14, 3.6, 3.7_

  - [x] 2.4 Create ArtifactFact and ArtifactRelationship entities
    - Create `src/Nornis.Domain/Entities/ArtifactFact.cs` with Id, ArtifactId, Predicate, Value, Confidence, TruthState, Visibility, CreatedAt, UpdatedAt, RowVersion
    - Create `src/Nornis.Domain/Entities/ArtifactRelationship.cs` with Id, CampaignId, ArtifactAId, ArtifactBId, Type, Description, Confidence, TruthState, Visibility, CreatedAt, UpdatedAt, RowVersion
    - Include navigation properties: ArtifactFact → Artifact; ArtifactRelationship → ArtifactA, ArtifactB
    - _Requirements: 2.7, 2.8, 2.13, 2.14, 3.8, 3.9_

  - [x] 2.5 Create SourceReference, ReviewBatch, and ReviewProposal entities
    - Create `src/Nornis.Domain/Entities/SourceReference.cs` with Id, SourceId, TargetType, TargetId, Quote, Notes, CreatedAt
    - Create `src/Nornis.Domain/Entities/ReviewBatch.cs` with Id, CampaignId, SourceId, Status, CreatedAt, CompletedAt
    - Create `src/Nornis.Domain/Entities/ReviewProposal.cs` with Id, ReviewBatchId, ChangeType, TargetType, TargetId, ProposedValueJson, Rationale, Confidence, Status, CreatedAt, ReviewedAt, ReviewedByUserId, RowVersion
    - Include navigation properties: ReviewBatch → Campaign, Source, ReviewProposals collection; ReviewProposal → ReviewBatch
    - _Requirements: 2.9, 2.10, 2.11, 2.13, 2.14, 3.10, 3.11_

  - [x] 2.6 Create AiUsageRecord entity
    - Create `src/Nornis.Domain/Entities/AiUsageRecord.cs` with Id, CampaignId, UserId, OperationType, Model, InputTokens, OutputTokens, TotalTokens, EstimatedCostUsd, SourceId, ReviewBatchId, DurationMs, Succeeded, ErrorCode, CreatedAt
    - _Requirements: 2.12, 2.13, 2.14_

  - [x] 2.7 Write unit tests verifying entity structure
    - Create test classes in `tests/Nornis.Domain.Tests/Entities/`
    - Verify all entity classes have expected properties with correct types using reflection
    - Verify all entities use DateTimeOffset for timestamps
    - Verify nullable reference types are enabled
    - _Requirements: 2.1–2.14_

- [x] 3. Define repository interfaces
  - [x] 3.1 Create campaign and membership repository interfaces
    - Create `src/Nornis.Domain/Repositories/ICampaignRepository.cs` with CreateAsync, GetByIdAsync, UpdateAsync, ListByUserAsync
    - Create `src/Nornis.Domain/Repositories/ICampaignMemberRepository.cs` with CreateAsync, GetByCampaignAndUserAsync, ListByCampaignAsync, RemoveAsync
    - Create `src/Nornis.Domain/Repositories/IUserRepository.cs` with CreateAsync, GetByIdAsync, GetByAuth0SubjectIdAsync, UpdateAsync
    - All methods accept CancellationToken and return Task/Task<T>
    - _Requirements: 4.1, 4.2, 4.11, 4.12, 4.13_

  - [x] 3.2 Create source and artifact repository interfaces
    - Create `src/Nornis.Domain/Repositories/ISourceRepository.cs` with CreateAsync, GetByIdAsync, ListByCampaignAsync (with visibility filtering), UpdateProcessingStatusAsync
    - Create `src/Nornis.Domain/Repositories/IArtifactRepository.cs` with CreateAsync, GetByIdAsync, ListByCampaignAsync (with type and visibility filtering), UpdateAsync, SearchByNameAsync
    - Create `src/Nornis.Domain/Repositories/IArtifactFactRepository.cs` with CreateAsync, GetByIdAsync, ListByArtifactAsync, UpdateAsync
    - Create `src/Nornis.Domain/Repositories/IArtifactRelationshipRepository.cs` with CreateAsync, GetByIdAsync, ListByArtifactAsync (bidirectional), UpdateAsync
    - _Requirements: 4.3, 4.4, 4.5, 4.6, 4.12, 4.13_

  - [x] 3.3 Create review and utility repository interfaces
    - Create `src/Nornis.Domain/Repositories/IReviewBatchRepository.cs` with CreateAsync, GetByIdAsync, ListByCampaignAsync, UpdateStatusAsync
    - Create `src/Nornis.Domain/Repositories/IReviewProposalRepository.cs` with CreateAsync, GetByIdAsync, ListByReviewBatchAsync, ListPendingByCampaignAsync, UpdateAsync
    - Create `src/Nornis.Domain/Repositories/ISourceReferenceRepository.cs` with CreateAsync, ListByTargetAsync
    - Create `src/Nornis.Domain/Repositories/IAiUsageRecordRepository.cs` with CreateAsync, QueryAsync (by campaign, user, date range, operation type)
    - _Requirements: 4.7, 4.8, 4.9, 4.10, 4.12, 4.13_

  - [x] 3.4 Write unit tests verifying repository interface contracts
    - Verify all repository interface methods accept CancellationToken
    - Verify all repository interface methods return Task or Task<T>
    - Verify interfaces define expected method signatures via reflection
    - _Requirements: 4.12, 4.13_

- [x] 4. Checkpoint - Verify domain layer compiles and tests pass
  - Ensure all tests pass, ask the user if questions arise.
  - Verify Nornis.Domain has no PackageReference for EF Core or infrastructure packages
  - _Requirements: 9.1, 9.2, 9.3, 9.4_

- [x] 5. Implement EF Core DbContext and entity configurations
  - [x] 5.1 Set up Nornis.Infrastructure project with EF Core packages and create NornisDbContext
    - Add Microsoft.EntityFrameworkCore.SqlServer package reference to Nornis.Infrastructure.csproj
    - Add Microsoft.EntityFrameworkCore.Design package reference
    - Create `src/Nornis.Infrastructure/Persistence/NornisDbContext.cs`
    - Expose DbSet properties for all 12 entities
    - Override OnModelCreating to apply configurations from assembly
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 5.2 Create entity configurations for User, Campaign, and CampaignMember
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/UserConfiguration.cs` — table name, string max lengths, unique index on Auth0SubjectId, RowVersion concurrency token, datetimeoffset columns
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/CampaignConfiguration.cs` — table name, string max lengths, FK to User with Restrict, RowVersion, datetimeoffset
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/CampaignMemberConfiguration.cs` — table name, unique composite index (CampaignId, UserId), enum stored as string, FK to Campaign with Cascade, FK to User with Restrict
    - _Requirements: 5.4, 5.5, 6.1, 6.2, 6.3, 6.4, 6.5, 6.9, 6.10_

  - [x] 5.3 Create entity configurations for Source, SourceExtraction, and Artifact
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/SourceConfiguration.cs` — table name, string lengths, Body as nvarchar(max), index on (CampaignId, ProcessingStatus), enums as strings, FK with Cascade to Campaign, Restrict to User
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/SourceExtractionConfiguration.cs` — table name, Text as nvarchar(max), enum as string, FK to Source with Cascade
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/ArtifactConfiguration.cs` — table name, string lengths, enums as strings, RowVersion, FK to Campaign with Cascade
    - _Requirements: 5.4, 5.5, 6.1, 6.2, 6.3, 6.7, 6.9, 6.10_

  - [x] 5.4 Create entity configurations for ArtifactFact, ArtifactRelationship, and SourceReference
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/ArtifactFactConfiguration.cs` — table name, string lengths, enums as strings, RowVersion, FK to Artifact with Cascade
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/ArtifactRelationshipConfiguration.cs` — table name, string lengths, indexes on ArtifactAId and ArtifactBId, enums as strings, RowVersion, FK to Campaign with Cascade, FK to Artifacts with Restrict
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/SourceReferenceConfiguration.cs` — table name, string lengths, enum as string, FK to Source with Cascade
    - _Requirements: 5.4, 5.5, 6.1, 6.2, 6.3, 6.6, 6.9, 6.10_

  - [x] 5.5 Create entity configurations for ReviewBatch, ReviewProposal, and AiUsageRecord
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/ReviewBatchConfiguration.cs` — table name, enum as string, FK to Campaign with Cascade, FK to Source with Restrict
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/ReviewProposalConfiguration.cs` — table name, ProposedValueJson as nvarchar(max), string lengths, index on (ReviewBatchId, Status), enums as strings, RowVersion, FK to ReviewBatch with Cascade, FK to User with Restrict
    - Create `src/Nornis.Infrastructure/Persistence/Configurations/AiUsageRecordConfiguration.cs` — table name, string lengths, enum as string, FKs with SetNull for nullable references
    - _Requirements: 5.4, 5.5, 6.1, 6.2, 6.3, 6.8, 6.9, 6.10_

- [x] 6. Implement repository classes
  - [x] 6.1 Implement UserRepository, CampaignRepository, and CampaignMemberRepository
    - Create `src/Nornis.Infrastructure/Persistence/Repositories/UserRepository.cs` implementing IUserRepository
    - Create `src/Nornis.Infrastructure/Persistence/Repositories/CampaignRepository.cs` implementing ICampaignRepository
    - Create `src/Nornis.Infrastructure/Persistence/Repositories/CampaignMemberRepository.cs` implementing ICampaignMemberRepository
    - Accept NornisDbContext via constructor injection
    - Use AsNoTracking for read-only queries
    - Propagate CancellationToken to all EF Core operations
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.7_

  - [x] 6.2 Implement SourceRepository, ArtifactRepository, and ArtifactFactRepository
    - Create `src/Nornis.Infrastructure/Persistence/Repositories/SourceRepository.cs` implementing ISourceRepository
    - Create `src/Nornis.Infrastructure/Persistence/Repositories/ArtifactRepository.cs` implementing IArtifactRepository
    - Create `src/Nornis.Infrastructure/Persistence/Repositories/ArtifactFactRepository.cs` implementing IArtifactFactRepository
    - Implement visibility filtering and type filtering for list queries
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.7_

  - [x] 6.3 Implement ArtifactRelationshipRepository, ReviewBatchRepository, and ReviewProposalRepository
    - Create `src/Nornis.Infrastructure/Persistence/Repositories/ArtifactRelationshipRepository.cs` implementing IArtifactRelationshipRepository
    - Implement bidirectional query: where ArtifactAId == id OR ArtifactBId == id
    - Create `src/Nornis.Infrastructure/Persistence/Repositories/ReviewBatchRepository.cs` implementing IReviewBatchRepository
    - Create `src/Nornis.Infrastructure/Persistence/Repositories/ReviewProposalRepository.cs` implementing IReviewProposalRepository
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7_

  - [x] 6.4 Implement SourceReferenceRepository and AiUsageRecordRepository
    - Create `src/Nornis.Infrastructure/Persistence/Repositories/SourceReferenceRepository.cs` implementing ISourceReferenceRepository
    - Create `src/Nornis.Infrastructure/Persistence/Repositories/AiUsageRecordRepository.cs` implementing IAiUsageRecordRepository
    - Implement date range and multi-field filtering for AiUsageRecord queries
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.7_

- [x] 7. Checkpoint - Verify infrastructure layer compiles
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Create initial EF Core migration
  - [x] 8.1 Generate the initial migration
    - Add EF Core tools reference if needed
    - Generate migration using `dotnet ef migrations add InitialCreate`
    - Verify the migration creates all tables, indexes, foreign keys, and concurrency tokens
    - Ensure datetimeoffset and nvarchar types are used correctly for Azure SQL compatibility
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

- [x] 9. Write integration tests
  - [x] 9.1 Set up integration test infrastructure
    - Add Microsoft.EntityFrameworkCore.Sqlite package to Nornis.Infrastructure.Tests
    - Add FsCheck and FsCheck.NUnit packages to Nornis.Infrastructure.Tests
    - Create a test fixture base class that provisions an in-memory SQLite database with NornisDbContext
    - Create custom FsCheck Arbitrary generators for valid entity instances
    - _Requirements: 5.4, 7.4_

  - [x] 9.2 Write property test for entity persistence round-trip
    - **Property 1: Entity Persistence Round-Trip**
    - Generate random valid entities, persist via repository, retrieve by ID, assert all properties match including enum values
    - Minimum 100 iterations
    - **Validates: Requirements 5.4, 7.4**

  - [x] 9.3 Write property test for update persistence
    - **Property 2: Update Persistence**
    - Generate random valid entities, persist, mutate a mutable property, update via repository, retrieve, assert mutation reflected
    - Minimum 100 iterations
    - **Validates: Requirements 7.5**

  - [x] 9.4 Write property test for optimistic concurrency detection
    - **Property 3: Optimistic Concurrency Detection**
    - Generate a mutable entity, persist, load two copies via separate DbContext instances, modify both, save one, assert second save throws DbUpdateConcurrencyException
    - Minimum 100 iterations
    - **Validates: Requirements 6.9**

  - [x] 9.5 Write property test for bidirectional relationship query
    - **Property 4: Bidirectional Relationship Query**
    - Generate an artifact with relationships on both sides, persist all, query relationships for the artifact, assert all relationships returned
    - Minimum 100 iterations
    - **Validates: Requirements 7.6**

  - [x] 9.6 Write integration tests for EF Core model validation
    - Verify table names, column types, max lengths, and indexes match design
    - Verify migration applies cleanly to a fresh database
    - _Requirements: 6.1–6.10, 8.1–8.5_

- [x] 10. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.
  - Remove `Placeholder.cs` from Nornis.Domain if present

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate structural contracts via reflection
- The Domain layer must remain free of EF Core or infrastructure package references (Requirement 9)
- Use SQLite in-memory provider for integration tests to avoid requiring a live database
- Use FsCheck.NUnit as the property-based testing library per the design document

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "2.1", "2.6"] },
    { "id": 2, "tasks": ["2.2", "2.3", "2.4", "2.5"] },
    { "id": 3, "tasks": ["2.7", "3.1", "3.2", "3.3"] },
    { "id": 4, "tasks": ["3.4", "5.1"] },
    { "id": 5, "tasks": ["5.2", "5.3", "5.4", "5.5"] },
    { "id": 6, "tasks": ["6.1", "6.2", "6.3", "6.4"] },
    { "id": 7, "tasks": ["8.1"] },
    { "id": 8, "tasks": ["9.1"] },
    { "id": 9, "tasks": ["9.2", "9.3", "9.4", "9.5", "9.6"] }
  ]
}
```
