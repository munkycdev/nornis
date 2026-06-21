# Requirements Document

## Introduction

This specification defines the core domain model entities, repository interfaces, EF Core persistence layer, and initial database migration for the Nornis application. The domain data layer establishes the foundational data structures and access patterns used by all higher layers of the application.

## Glossary

- **Domain_Layer**: The Nornis.Domain project containing entity classes, enums, and repository interfaces with no external infrastructure dependencies.
- **Infrastructure_Layer**: The Nornis.Infrastructure project containing EF Core DbContext, entity configurations, and repository implementations.
- **Entity**: A C# class representing a persistent domain object with a unique identifier.
- **Enum**: A C# enumeration representing a finite set of domain states or categories.
- **Repository_Interface**: A C# interface defining data access operations for a specific entity or aggregate, defined in the Domain or Application layer.
- **Repository_Implementation**: A concrete C# class in the Infrastructure layer that implements a Repository_Interface using EF Core.
- **DbContext**: The EF Core NornisDbContext class that manages entity-to-database mapping and database connections.
- **Entity_Configuration**: An EF Core IEntityTypeConfiguration class that defines table mapping, column types, indexes, and constraints for a specific entity.
- **Concurrency_Token**: A row version column used by EF Core for optimistic concurrency control on mutable records.
- **Migration**: An EF Core migration that creates or modifies the database schema.

## Requirements

### Requirement 1: Domain Enums

**User Story:** As a developer, I want all domain state enumerations defined in Nornis.Domain, so that domain logic can reference explicit typed values instead of magic strings.

#### Acceptance Criteria

1. THE Domain_Layer SHALL define a CampaignRole enum with values GM, Player, and Observer.
2. THE Domain_Layer SHALL define a SourceType enum with values SessionNote, JournalEntry, Transcript, Upload, Image, HandwrittenNotes, WebLink, GMNote, and ImportedNote.
3. THE Domain_Layer SHALL define a SourceProcessingStatus enum with values Draft, Ready, Queued, Processing, Processed, and Failed.
4. THE Domain_Layer SHALL define an ArtifactType enum with values Character, Location, Item, Faction, Event, Thread, Concept, and Document.
5. THE Domain_Layer SHALL define an ArtifactStatus enum with values Active, Dormant, Resolved, and Archived.
6. THE Domain_Layer SHALL define a TruthState enum with values Confirmed, Likely, Rumor, Disputed, False, and Hidden.
7. THE Domain_Layer SHALL define a VisibilityScope enum with values Private, GMOnly, and PartyVisible.
8. THE Domain_Layer SHALL define a ReviewBatchStatus enum with values Pending, InReview, Completed, Canceled, and Failed.
9. THE Domain_Layer SHALL define a ReviewProposalStatus enum with values Pending, Accepted, Rejected, and Edited.
10. THE Domain_Layer SHALL define a ReviewChangeType enum with values CreateArtifact, UpdateArtifact, MergeArtifact, AddFact, UpdateFact, AddRelationship, and UpdateRelationship.
11. THE Domain_Layer SHALL define a ReviewTargetType enum with values Artifact, ArtifactFact, and ArtifactRelationship.
12. THE Domain_Layer SHALL define a SourceExtractionType enum with values Manual, OCR, VisionSummary, Transcription, and WebPageText.
13. THE Domain_Layer SHALL define a SourceReferenceTargetType enum with values Artifact, ArtifactFact, ArtifactRelationship, and ReviewProposal.
14. THE Domain_Layer SHALL define an AiOperationType enum with values SourceExtraction, ArtifactSummary, AskLoremaster, and SourceExtractionRepair.
15. THE Domain_Layer SHALL define a ConversationRole enum with values User and Assistant.

### Requirement 2: Core Domain Entities

**User Story:** As a developer, I want all domain entities defined as C# classes in Nornis.Domain, so that the application has a strongly-typed representation of campaign knowledge.

#### Acceptance Criteria

1. THE Domain_Layer SHALL define a User entity with properties Id (Guid), Auth0SubjectId (string), Username (string), Email (string), CreatedAt (DateTimeOffset), and UpdatedAt (DateTimeOffset).
2. THE Domain_Layer SHALL define a Campaign entity with properties Id (Guid), Name (string), Description (string, nullable), GameSystem (string, nullable), CreatedAt (DateTimeOffset), UpdatedAt (DateTimeOffset), and CreatedByUserId (Guid).
3. THE Domain_Layer SHALL define a CampaignMember entity with properties Id (Guid), CampaignId (Guid), UserId (Guid), Role (CampaignRole), DisplayName (string, nullable), CharacterName (string, nullable), and JoinedAt (DateTimeOffset).
4. THE Domain_Layer SHALL define a Source entity with properties Id (Guid), CampaignId (Guid), Type (SourceType), Title (string), Body (string, nullable), Uri (string, nullable), OccurredAt (DateTimeOffset, nullable), CreatedAt (DateTimeOffset), CreatedByUserId (Guid), Visibility (VisibilityScope), and ProcessingStatus (SourceProcessingStatus).
5. THE Domain_Layer SHALL define a SourceExtraction entity with properties Id (Guid), SourceId (Guid), ExtractionType (SourceExtractionType), Text (string), Confidence (decimal, nullable), and CreatedAt (DateTimeOffset).
6. THE Domain_Layer SHALL define an Artifact entity with properties Id (Guid), CampaignId (Guid), Type (ArtifactType), Name (string), Summary (string, nullable), Visibility (VisibilityScope), Confidence (decimal, nullable), Status (ArtifactStatus), CreatedAt (DateTimeOffset), and UpdatedAt (DateTimeOffset).
7. THE Domain_Layer SHALL define an ArtifactFact entity with properties Id (Guid), ArtifactId (Guid), Predicate (string), Value (string), Confidence (decimal, nullable), TruthState (TruthState), Visibility (VisibilityScope), CreatedAt (DateTimeOffset), and UpdatedAt (DateTimeOffset).
8. THE Domain_Layer SHALL define an ArtifactRelationship entity with properties Id (Guid), CampaignId (Guid), ArtifactAId (Guid), ArtifactBId (Guid), Type (string), Description (string, nullable), Confidence (decimal, nullable), TruthState (TruthState), Visibility (VisibilityScope), CreatedAt (DateTimeOffset), and UpdatedAt (DateTimeOffset).
9. THE Domain_Layer SHALL define a SourceReference entity with properties Id (Guid), SourceId (Guid), TargetType (SourceReferenceTargetType), TargetId (Guid), Quote (string, nullable), Notes (string, nullable), and CreatedAt (DateTimeOffset).
10. THE Domain_Layer SHALL define a ReviewBatch entity with properties Id (Guid), CampaignId (Guid), SourceId (Guid), Status (ReviewBatchStatus), CreatedAt (DateTimeOffset), and CompletedAt (DateTimeOffset, nullable).
11. THE Domain_Layer SHALL define a ReviewProposal entity with properties Id (Guid), ReviewBatchId (Guid), ChangeType (ReviewChangeType), TargetType (ReviewTargetType), TargetId (Guid, nullable), ProposedValueJson (string), Rationale (string, nullable), Confidence (decimal, nullable), Status (ReviewProposalStatus), CreatedAt (DateTimeOffset), ReviewedAt (DateTimeOffset, nullable), and ReviewedByUserId (Guid, nullable).
12. THE Domain_Layer SHALL define an AiUsageRecord entity with properties Id (Guid), CampaignId (Guid, nullable), UserId (Guid, nullable), OperationType (AiOperationType), Model (string), InputTokens (int), OutputTokens (int), TotalTokens (int), EstimatedCostUsd (decimal), SourceId (Guid, nullable), ReviewBatchId (Guid, nullable), DurationMs (int), Succeeded (bool), ErrorCode (string, nullable), and CreatedAt (DateTimeOffset).
13. THE Domain_Layer SHALL use DateTimeOffset for all timestamp properties across all entities.
14. THE Domain_Layer SHALL enable nullable reference types for all entity classes.

### Requirement 3: Entity Navigation Properties

**User Story:** As a developer, I want entities to include navigation properties for related entities, so that EF Core can establish relationships and enable efficient querying.

#### Acceptance Criteria

1. THE Campaign entity SHALL include a navigation property to a collection of CampaignMember entities.
2. THE Campaign entity SHALL include a navigation property to the User entity referenced by CreatedByUserId.
3. THE CampaignMember entity SHALL include navigation properties to the Campaign entity and the User entity.
4. THE Source entity SHALL include navigation properties to the Campaign entity and the User entity referenced by CreatedByUserId.
5. THE Source entity SHALL include a navigation property to a collection of SourceExtraction entities.
6. THE Artifact entity SHALL include a navigation property to the Campaign entity.
7. THE Artifact entity SHALL include a navigation property to a collection of ArtifactFact entities.
8. THE ArtifactFact entity SHALL include a navigation property to the Artifact entity.
9. THE ArtifactRelationship entity SHALL include navigation properties to the Artifact entities referenced by ArtifactAId and ArtifactBId.
10. THE ReviewBatch entity SHALL include navigation properties to the Campaign entity, the Source entity, and a collection of ReviewProposal entities.
11. THE ReviewProposal entity SHALL include a navigation property to the ReviewBatch entity.

### Requirement 4: Repository Interfaces

**User Story:** As a developer, I want repository interfaces defined in the Domain or Application layer, so that application services can access data without depending on EF Core directly.

#### Acceptance Criteria

1. THE Domain_Layer SHALL define an ICampaignRepository interface with methods to create, retrieve by id, update, and list campaigns for a user.
2. THE Domain_Layer SHALL define an ICampaignMemberRepository interface with methods to create, retrieve by campaign and user, list members by campaign, and remove a member.
3. THE Domain_Layer SHALL define an ISourceRepository interface with methods to create, retrieve by id, list by campaign with visibility filtering, and update processing status.
4. THE Domain_Layer SHALL define an IArtifactRepository interface with methods to create, retrieve by id, list by campaign with filtering by type and visibility, update, and search by name within a campaign.
5. THE Domain_Layer SHALL define an IArtifactFactRepository interface with methods to create, retrieve by id, list by artifact, and update.
6. THE Domain_Layer SHALL define an IArtifactRelationshipRepository interface with methods to create, retrieve by id, list relationships for a given artifact (matching either ArtifactAId or ArtifactBId), and update.
7. THE Domain_Layer SHALL define an IReviewBatchRepository interface with methods to create, retrieve by id, list by campaign, and update status.
8. THE Domain_Layer SHALL define an IReviewProposalRepository interface with methods to create, retrieve by id, list by review batch, list pending proposals by campaign, and update.
9. THE Domain_Layer SHALL define an ISourceReferenceRepository interface with methods to create and list references by target type and target id.
10. THE Domain_Layer SHALL define an IAiUsageRecordRepository interface with methods to create and query usage records by campaign, user, date range, and operation type.
11. THE Domain_Layer SHALL define an IUserRepository interface with methods to create, retrieve by id, retrieve by Auth0SubjectId, and update.
12. ALL repository interface methods that perform I/O SHALL accept a CancellationToken parameter.
13. ALL repository interface methods that perform I/O SHALL return Task or Task of T for asynchronous execution.

### Requirement 5: EF Core DbContext

**User Story:** As a developer, I want a properly configured EF Core DbContext in Nornis.Infrastructure, so that entities can be persisted to Azure SQL with correct schema mapping.

#### Acceptance Criteria

1. THE Infrastructure_Layer SHALL define a NornisDbContext class that extends DbContext.
2. THE NornisDbContext SHALL expose a DbSet property for each domain entity: User, Campaign, CampaignMember, Source, SourceExtraction, Artifact, ArtifactFact, ArtifactRelationship, SourceReference, ReviewBatch, ReviewProposal, and AiUsageRecord.
3. THE NornisDbContext SHALL apply entity configurations from IEntityTypeConfiguration classes using the ApplyConfigurationsFromAssembly method.
4. THE NornisDbContext SHALL store all enum properties as strings in the database using EF Core value conversions.
5. THE NornisDbContext SHALL configure DateTimeOffset columns to use the datetimeoffset SQL Server column type.

### Requirement 6: Entity Configurations

**User Story:** As a developer, I want each entity to have an explicit EF Core configuration class, so that database schema details are defined clearly and consistently.

#### Acceptance Criteria

1. THE Infrastructure_Layer SHALL define a separate IEntityTypeConfiguration class for each domain entity.
2. WHEN configuring an entity, THE Entity_Configuration SHALL specify the table name explicitly.
3. WHEN configuring string properties, THE Entity_Configuration SHALL specify maximum length constraints appropriate for the data (Name fields limited to 200 characters, Description and Summary fields limited to 2000 characters, Body and ProposedValueJson fields configured as nvarchar max).
4. WHEN configuring the User entity, THE Entity_Configuration SHALL create a unique index on the Auth0SubjectId column.
5. WHEN configuring the CampaignMember entity, THE Entity_Configuration SHALL create a unique composite index on CampaignId and UserId to prevent duplicate membership.
6. WHEN configuring the ArtifactRelationship entity, THE Entity_Configuration SHALL create indexes on both ArtifactAId and ArtifactBId for efficient bidirectional lookups.
7. WHEN configuring the Source entity, THE Entity_Configuration SHALL create an index on CampaignId and ProcessingStatus for efficient queue queries.
8. WHEN configuring the ReviewProposal entity, THE Entity_Configuration SHALL create an index on ReviewBatchId and Status for efficient review queue queries.
9. WHEN configuring mutable entities (Campaign, User, Artifact, ArtifactFact, ArtifactRelationship, ReviewProposal), THE Entity_Configuration SHALL configure a Concurrency_Token using a row version column.
10. WHEN configuring foreign key relationships, THE Entity_Configuration SHALL specify OnDelete behavior to prevent cascading deletes across campaign boundaries (use Restrict or NoAction for cross-aggregate references).

### Requirement 7: Repository Implementations

**User Story:** As a developer, I want concrete repository implementations in Nornis.Infrastructure, so that data access operations are executed against the database using EF Core.

#### Acceptance Criteria

1. THE Infrastructure_Layer SHALL provide a concrete implementation class for each repository interface defined in the Domain_Layer.
2. WHEN implementing a repository, THE Repository_Implementation SHALL accept NornisDbContext as a constructor dependency.
3. WHEN implementing query methods, THE Repository_Implementation SHALL use AsNoTracking for read-only queries to improve performance.
4. WHEN implementing create methods, THE Repository_Implementation SHALL add the entity to the DbContext and call SaveChangesAsync.
5. WHEN implementing update methods, THE Repository_Implementation SHALL call SaveChangesAsync to persist changes.
6. WHEN the IArtifactRelationshipRepository lists relationships for an artifact, THE Repository_Implementation SHALL query where ArtifactAId equals the given id OR ArtifactBId equals the given id.
7. ALL Repository_Implementation methods SHALL propagate CancellationToken to EF Core async operations.

### Requirement 8: Initial Database Migration

**User Story:** As a developer, I want an initial EF Core migration that creates the complete database schema, so that the application can be deployed against a fresh Azure SQL database.

#### Acceptance Criteria

1. THE Infrastructure_Layer SHALL include an initial EF Core migration that creates all tables defined by the entity configurations.
2. THE Migration SHALL create all indexes defined in the entity configurations.
3. THE Migration SHALL create all foreign key constraints defined in the entity configurations.
4. THE Migration SHALL create concurrency token columns (RowVersion) for all mutable entities.
5. THE Migration SHALL produce a schema compatible with Azure SQL (using datetimeoffset for timestamps and nvarchar for string columns).

### Requirement 9: Domain Layer Independence

**User Story:** As a developer, I want the Domain layer to remain free of infrastructure dependencies, so that domain logic is testable in isolation and the architecture remains clean.

#### Acceptance Criteria

1. THE Domain_Layer SHALL NOT reference EF Core packages.
2. THE Domain_Layer SHALL NOT reference Azure SDK packages.
3. THE Domain_Layer SHALL NOT reference any infrastructure or presentation layer packages.
4. THE Nornis.Domain project file SHALL contain no PackageReference elements for Microsoft.EntityFrameworkCore or related packages.
