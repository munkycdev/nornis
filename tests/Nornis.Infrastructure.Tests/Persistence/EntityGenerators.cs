using FsCheck;
using FsCheck.Fluent;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Custom FsCheck Arbitrary generators for all 12 domain entity types.
/// Generates valid instances respecting required field constraints, max lengths, and enum values.
/// </summary>
public static class EntityGenerators
{
    private static readonly string[] Names =
    [
        "Captain Voss", "Tavrin", "Black Harbor", "Silver Key",
        "Missing Caravan", "The Red Lodge", "Iron Gate", "Whisperwind"
    ];

    private static readonly string[] Predicates =
    [
        "location", "allegiance", "status", "title",
        "last_seen", "known_associates", "weakness", "motivation"
    ];

    private static readonly string[] RelationshipTypes =
    [
        "LocatedIn", "AlliedWith", "SuspectedIn", "Owns",
        "Opposes", "CreatedBy", "ConnectedTo", "ServantOf"
    ];

    private static readonly string[] Models =
    [
        "gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-3.5-turbo"
    ];

    /// <summary>
    /// Generates a non-null, non-empty string with a maximum length constraint.
    /// </summary>
    private static Gen<string> NonEmptyStringGen(int maxLength) =>
        Gen.Elements(Names)
            .Select(n => n.Length > maxLength ? n[..maxLength] : n);

    /// <summary>
    /// Generates a nullable string that is sometimes null, sometimes populated.
    /// </summary>
    private static Gen<string?> NullableStringGen(int maxLength) =>
        Gen.Frequency(
            (1, Gen.Constant<string?>(null)),
            (3, NonEmptyStringGen(maxLength).Select(s => (string?)s)));

    /// <summary>
    /// Generates a random enum value from all defined values.
    /// </summary>
    private static Gen<T> EnumGen<T>() where T : struct, Enum =>
        Gen.Elements(Enum.GetValues<T>());

    /// <summary>
    /// Generates a confidence decimal between 0.0 and 1.0, or null.
    /// </summary>
    private static Gen<decimal?> NullableConfidenceGen() =>
        Gen.Frequency(
            (1, Gen.Constant<decimal?>(null)),
            (3, Gen.Choose(0, 100).Select(i => (decimal?)((decimal)i / 100m))));

    /// <summary>
    /// Generates a confidence decimal between 0.0 and 1.0.
    /// </summary>
    private static Gen<decimal> ConfidenceGen() =>
        Gen.Choose(0, 100).Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a random DateTimeOffset within a reasonable range.
    /// </summary>
    private static Gen<DateTimeOffset> DateTimeOffsetGen() =>
        from year in Gen.Choose(2020, 2025)
        from month in Gen.Choose(1, 12)
        from day in Gen.Choose(1, 28)
        from hour in Gen.Choose(0, 23)
        select new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Generates a nullable DateTimeOffset.
    /// </summary>
    private static Gen<DateTimeOffset?> NullableDateTimeOffsetGen() =>
        Gen.Frequency(
            (1, Gen.Constant<DateTimeOffset?>(null)),
            (3, DateTimeOffsetGen().Select(d => (DateTimeOffset?)d)));

    /// <summary>
    /// Generates a random Guid.
    /// </summary>
    private static Gen<Guid> GuidGen() =>
        ArbMap.Default.GeneratorFor<Guid>();

    /// <summary>
    /// Generates a nullable Guid.
    /// </summary>
    private static Gen<Guid?> NullableGuidGen() =>
        Gen.Frequency(
            (1, Gen.Constant<Guid?>(null)),
            (3, GuidGen().Select(g => (Guid?)g)));

    public static Gen<User> UserGen() =>
        from id in GuidGen()
        from auth0Sub in NonEmptyStringGen(200)
        from username in NonEmptyStringGen(200)
        from email in NonEmptyStringGen(200)
        from createdAt in DateTimeOffsetGen()
        from updatedAt in DateTimeOffsetGen()
        select new User
        {
            Id = id,
            Auth0SubjectId = auth0Sub,
            Username = username,
            Email = email,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            RowVersion = []
        };

    public static Gen<World> WorldGen() =>
        from id in GuidGen()
        from name in NonEmptyStringGen(200)
        from description in NullableStringGen(2000)
        from gameSystem in NullableStringGen(200)
        from createdAt in DateTimeOffsetGen()
        from updatedAt in DateTimeOffsetGen()
        from createdByUserId in GuidGen()
        select new World
        {
            Id = id,
            Name = name,
            Description = description,
            GameSystem = gameSystem,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            CreatedByUserId = createdByUserId,
            RowVersion = []
        };

    public static Gen<WorldMember> WorldMemberGen() =>
        from id in GuidGen()
        from worldId in GuidGen()
        from userId in GuidGen()
        from role in EnumGen<WorldRole>()
        from displayName in NullableStringGen(200)
        from joinedAt in DateTimeOffsetGen()
        select new WorldMember
        {
            Id = id,
            WorldId = worldId,
            UserId = userId,
            Role = role,
            DisplayName = displayName,
            JoinedAt = joinedAt
        };

    public static Gen<Source> SourceGen() =>
        from id in GuidGen()
        from worldId in GuidGen()
        from type in EnumGen<SourceType>()
        from title in NonEmptyStringGen(200)
        from body in NullableStringGen(2000)
        from uri in NullableStringGen(2000)
        from occurredAt in NullableDateTimeOffsetGen()
        from createdAt in DateTimeOffsetGen()
        from createdByUserId in GuidGen()
        from visibility in EnumGen<VisibilityScope>()
        from processingStatus in EnumGen<SourceProcessingStatus>()
        select new Source
        {
            Id = id,
            WorldId = worldId,
            Type = type,
            Title = title,
            Body = body,
            Uri = uri,
            OccurredAt = occurredAt,
            CreatedAt = createdAt,
            CreatedByUserId = createdByUserId,
            Visibility = visibility,
            ProcessingStatus = processingStatus
        };

    public static Gen<SourceExtraction> SourceExtractionGen() =>
        from id in GuidGen()
        from sourceId in GuidGen()
        from extractionType in EnumGen<SourceExtractionType>()
        from text in NonEmptyStringGen(200)
        from confidence in NullableConfidenceGen()
        from createdAt in DateTimeOffsetGen()
        select new SourceExtraction
        {
            Id = id,
            SourceId = sourceId,
            ExtractionType = extractionType,
            Text = text,
            Confidence = confidence,
            CreatedAt = createdAt
        };

    public static Gen<Artifact> ArtifactGen() =>
        from id in GuidGen()
        from worldId in GuidGen()
        from type in EnumGen<ArtifactType>()
        from name in NonEmptyStringGen(200)
        from summary in NullableStringGen(2000)
        from visibility in EnumGen<VisibilityScope>()
        from confidence in NullableConfidenceGen()
        from status in EnumGen<ArtifactStatus>()
        from createdAt in DateTimeOffsetGen()
        from updatedAt in DateTimeOffsetGen()
        select new Artifact
        {
            Id = id,
            WorldId = worldId,
            Type = type,
            Name = name,
            Summary = summary,
            Visibility = visibility,
            Confidence = confidence,
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            RowVersion = []
        };

    public static Gen<ArtifactFact> ArtifactFactGen() =>
        from id in GuidGen()
        from artifactId in GuidGen()
        from predicate in Gen.Elements(Predicates)
        from value in NonEmptyStringGen(2000)
        from confidence in NullableConfidenceGen()
        from truthState in EnumGen<TruthState>()
        from visibility in EnumGen<VisibilityScope>()
        from createdAt in DateTimeOffsetGen()
        from updatedAt in DateTimeOffsetGen()
        select new ArtifactFact
        {
            Id = id,
            ArtifactId = artifactId,
            Predicate = predicate,
            Value = value,
            Confidence = confidence,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            RowVersion = []
        };

    public static Gen<ArtifactRelationship> ArtifactRelationshipGen() =>
        from id in GuidGen()
        from worldId in GuidGen()
        from artifactAId in GuidGen()
        from artifactBId in GuidGen()
        from type in Gen.Elements(RelationshipTypes)
        from description in NullableStringGen(2000)
        from confidence in NullableConfidenceGen()
        from truthState in EnumGen<TruthState>()
        from visibility in EnumGen<VisibilityScope>()
        from createdAt in DateTimeOffsetGen()
        from updatedAt in DateTimeOffsetGen()
        select new ArtifactRelationship
        {
            Id = id,
            WorldId = worldId,
            ArtifactAId = artifactAId,
            ArtifactBId = artifactBId,
            Type = type,
            Description = description,
            Confidence = confidence,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            RowVersion = []
        };

    public static Gen<SourceReference> SourceReferenceGen() =>
        from id in GuidGen()
        from sourceId in GuidGen()
        from targetType in EnumGen<SourceReferenceTargetType>()
        from targetId in GuidGen()
        from quote in NullableStringGen(2000)
        from notes in NullableStringGen(2000)
        from createdAt in DateTimeOffsetGen()
        select new SourceReference
        {
            Id = id,
            SourceId = sourceId,
            TargetType = targetType,
            TargetId = targetId,
            Quote = quote,
            Notes = notes,
            CreatedAt = createdAt
        };

    public static Gen<ReviewBatch> ReviewBatchGen() =>
        from id in GuidGen()
        from worldId in GuidGen()
        from sourceId in GuidGen()
        from status in EnumGen<ReviewBatchStatus>()
        from createdAt in DateTimeOffsetGen()
        from completedAt in NullableDateTimeOffsetGen()
        select new ReviewBatch
        {
            Id = id,
            WorldId = worldId,
            SourceId = sourceId,
            Status = status,
            CreatedAt = createdAt,
            CompletedAt = completedAt
        };

    public static Gen<ReviewProposal> ReviewProposalGen() =>
        from id in GuidGen()
        from reviewBatchId in GuidGen()
        from changeType in EnumGen<ReviewChangeType>()
        from targetType in EnumGen<ReviewTargetType>()
        from targetId in NullableGuidGen()
        from proposedValueJson in NonEmptyStringGen(200).Select(s => $"{{\"name\":\"{s}\"}}")
        from rationale in NullableStringGen(2000)
        from confidence in NullableConfidenceGen()
        from status in EnumGen<ReviewProposalStatus>()
        from createdAt in DateTimeOffsetGen()
        from reviewedAt in NullableDateTimeOffsetGen()
        from reviewedByUserId in NullableGuidGen()
        select new ReviewProposal
        {
            Id = id,
            ReviewBatchId = reviewBatchId,
            ChangeType = changeType,
            TargetType = targetType,
            TargetId = targetId,
            ProposedValueJson = proposedValueJson,
            Rationale = rationale,
            Confidence = confidence,
            Status = status,
            CreatedAt = createdAt,
            ReviewedAt = reviewedAt,
            ReviewedByUserId = reviewedByUserId,
            RowVersion = []
        };

    public static Gen<AiUsageRecord> AiUsageRecordGen() =>
        from id in GuidGen()
        from worldId in NullableGuidGen()
        from userId in NullableGuidGen()
        from operationType in EnumGen<AiOperationType>()
        from model in Gen.Elements(Models)
        from inputTokens in Gen.Choose(1, 10000)
        from outputTokens in Gen.Choose(1, 5000)
        from estimatedCostUsd in ConfidenceGen()
        from sourceId in NullableGuidGen()
        from reviewBatchId in NullableGuidGen()
        from durationMs in Gen.Choose(50, 30000)
        from succeeded in ArbMap.Default.GeneratorFor<bool>()
        from errorCode in NullableStringGen(200)
        from createdAt in DateTimeOffsetGen()
        select new AiUsageRecord
        {
            Id = id,
            WorldId = worldId,
            UserId = userId,
            OperationType = operationType,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            EstimatedCostUsd = estimatedCostUsd,
            SourceId = sourceId,
            ReviewBatchId = reviewBatchId,
            DurationMs = durationMs,
            Succeeded = succeeded,
            ErrorCode = succeeded ? null : errorCode,
            CreatedAt = createdAt
        };

    /// <summary>
    /// Registers all custom Arbitrary generators for use in FsCheck property tests.
    /// Use this class as the Arbitrary parameter in [FsCheck.NUnit.Property] attributes.
    /// </summary>
    public class DomainArbitraries
    {
        public static Arbitrary<User> Users() =>
            UserGen().ToArbitrary();

        public static Arbitrary<World> Worlds() =>
            WorldGen().ToArbitrary();

        public static Arbitrary<WorldMember> WorldMembers() =>
            WorldMemberGen().ToArbitrary();

        public static Arbitrary<Source> Sources() =>
            SourceGen().ToArbitrary();

        public static Arbitrary<SourceExtraction> SourceExtractions() =>
            SourceExtractionGen().ToArbitrary();

        public static Arbitrary<Artifact> Artifacts() =>
            ArtifactGen().ToArbitrary();

        public static Arbitrary<ArtifactFact> ArtifactFacts() =>
            ArtifactFactGen().ToArbitrary();

        public static Arbitrary<ArtifactRelationship> ArtifactRelationships() =>
            ArtifactRelationshipGen().ToArbitrary();

        public static Arbitrary<SourceReference> SourceReferences() =>
            SourceReferenceGen().ToArbitrary();

        public static Arbitrary<ReviewBatch> ReviewBatches() =>
            ReviewBatchGen().ToArbitrary();

        public static Arbitrary<ReviewProposal> ReviewProposals() =>
            ReviewProposalGen().ToArbitrary();

        public static Arbitrary<AiUsageRecord> AiUsageRecords() =>
            AiUsageRecordGen().ToArbitrary();
    }
}
