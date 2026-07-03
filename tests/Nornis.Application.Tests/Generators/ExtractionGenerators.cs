using FsCheck;
using FsCheck.Fluent;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Generators;

/// <summary>
/// FsCheck generators for extraction-related property tests.
/// </summary>
public static class ExtractionGenerators
{
    private static readonly string[] ValidChangeTypeValues =
    [
        "CreateArtifact", "UpdateArtifact", "MergeArtifact",
        "AddFact", "UpdateFact", "AddRelationship", "UpdateRelationship"
    ];

    private static readonly string[] ValidTargetTypeValues =
    [
        "Artifact", "ArtifactFact", "ArtifactRelationship"
    ];

    private static readonly string[] SampleBodies =
    [
        "We questioned Captain Voss in Black Harbor.",
        "The party discovered a hidden passage behind the waterfall.",
        "Tavrin found the Silver Key in the captain's quarters.",
        "The missing caravan was last seen heading north toward the mountains.",
        "A mysterious figure was spotted at the crossroads near Iron Gate.",
        "The Red Lodge members were seen negotiating with faction leaders.",
        "Whisperwind delivered a cryptic message about the coming storm."
    ];

    private static readonly string[] ArtifactNames =
    [
        "Captain Voss", "Tavrin", "Black Harbor", "Silver Key",
        "Missing Caravan", "The Red Lodge", "Iron Gate", "Whisperwind"
    ];

    /// <summary>
    /// Generates non-null, non-empty, non-whitespace strings suitable for source body content.
    /// </summary>
    public static Gen<string> ValidSourceBody =>
        Gen.Elements(SampleBodies);

    /// <summary>
    /// Generates null, empty, or whitespace-only strings for empty body scenarios.
    /// </summary>
    public static Gen<string?> EmptySourceBody =>
        Gen.Elements<string?>(null, string.Empty, "   ", "\t\n", " \r\n  ");

    /// <summary>
    /// Generates valid ExtractionProposal objects conforming to schema constraints.
    /// ChangeType is a valid enum value, rationale is 1-500 chars, confidence is 0.0-1.0.
    /// </summary>
    public static Gen<ExtractionProposal> ExtractionProposalGen =>
        from changeType in Gen.Elements(ValidChangeTypeValues)
        from targetType in Gen.Elements(ValidTargetTypeValues)
        from hasTargetId in ArbMap.Default.GeneratorFor<bool>()
        from targetId in ArbMap.Default.GeneratorFor<Guid>()
        from rationaleLength in Gen.Choose(1, 100)
        from confidence in Gen.Choose(0, 100)
        select new ExtractionProposal
        {
            ChangeType = changeType,
            TargetType = targetType,
            TargetId = hasTargetId ? targetId : null,
            ProposedValue = new Dictionary<string, object>
            {
                ["name"] = $"Artifact-{changeType}",
                ["visibility"] = "PartyVisible"
            },
            Rationale = new string('a', rationaleLength),
            Confidence = (decimal)confidence / 100m
        };

    /// <summary>
    /// Generates AI responses that violate schema constraints in various ways.
    /// </summary>
    public static Gen<AiExtractionResponse> InvalidExtractionResponse =>
        Gen.OneOf(
            // Invalid ChangeType
            Gen.Constant(CreateInvalidChangeTypeResponse()),
            // Invalid TargetType
            Gen.Constant(CreateInvalidTargetTypeResponse()),
            // Rationale exceeds 500 chars
            Gen.Constant(CreateLongRationaleResponse()),
            // Confidence outside 0.0-1.0
            Gen.Constant(CreateInvalidConfidenceResponse()),
            // More than 50 proposals
            Gen.Constant(CreateTooManyProposalsResponse())
        );

    /// <summary>
    /// Generates an Artifact with associated ArtifactFacts.
    /// </summary>
    public static Gen<(Artifact Artifact, List<ArtifactFact> Facts)> ArtifactWithFacts =>
        from artifactType in Gen.Elements(Enum.GetValues<ArtifactType>())
        from visibility in Gen.Elements(Enum.GetValues<VisibilityScope>())
        from factCount in Gen.Choose(0, 30)
        from campaignId in ArbMap.Default.GeneratorFor<Guid>()
        from name in Gen.Elements(ArtifactNames)
        let artifactId = Guid.NewGuid()
        let artifact = new Artifact
        {
            Id = artifactId,
            CampaignId = campaignId,
            Type = artifactType,
            Name = name,
            Summary = $"Summary of {name}",
            Visibility = visibility,
            Confidence = 0.8m,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        }
        let facts = Enumerable.Range(0, factCount).Select(i => new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifactId,
            Predicate = $"predicate-{i}",
            Value = $"value-{i}",
            Confidence = 0.7m,
            TruthState = TruthState.Likely,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-i)
        }).ToList()
        select (artifact, facts);

    /// <summary>
    /// Generates random int pairs for input/output token counts.
    /// </summary>
    public static Gen<(int InputTokens, int OutputTokens)> TokenCounts =>
        from input in Gen.Choose(0, 100_000)
        from output in Gen.Choose(0, 50_000)
        select (input, output);

    /// <summary>
    /// Generates random ModelPricing with valid decimal rates.
    /// </summary>
    public static Gen<ModelPricing> ModelPricingGen =>
        from inputRate in Gen.Choose(1, 5000)
        from outputRate in Gen.Choose(1, 20000)
        select new ModelPricing
        {
            InputPerMillionTokensUsd = (decimal)inputRate / 100m,
            OutputPerMillionTokensUsd = (decimal)outputRate / 100m
        };

    /// <summary>
    /// Generates combinations of source visibility with expected allowed scopes.
    /// Used to verify context assembly respects visibility boundaries.
    /// </summary>
    public static Gen<(VisibilityScope SourceVisibility, VisibilityScope[] AllowedScopes)> SourceVisibilityScenario =>
        Gen.Elements(
            (VisibilityScope.Private, new[] { VisibilityScope.Private }),
            (VisibilityScope.GMOnly, new[] { VisibilityScope.GMOnly, VisibilityScope.PartyVisible }),
            (VisibilityScope.PartyVisible, new[] { VisibilityScope.PartyVisible })
        );

    /// <summary>
    /// Generates a Source entity in Queued status with a valid non-empty body.
    /// </summary>
    public static Gen<Source> QueuedSourceWithBody =>
        from body in ValidSourceBody
        from sourceType in Gen.Elements(
            SourceType.SessionNote, SourceType.JournalEntry, SourceType.GMNote,
            SourceType.Transcript, SourceType.WebLink)
        from visibility in Gen.Elements(Enum.GetValues<VisibilityScope>())
        from hasOccurredAt in ArbMap.Default.GeneratorFor<bool>()
        from daysAgo in Gen.Choose(1, 365)
        select new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = Guid.NewGuid(),
            Type = sourceType,
            Title = $"Session {daysAgo}",
            Body = body,
            OccurredAt = hasOccurredAt ? DateTimeOffset.UtcNow.AddDays(-daysAgo) : null,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = Guid.NewGuid(),
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Queued
        };

    /// <summary>
    /// Generates a Source entity in Queued status with a valid non-empty body and null OccurredAt.
    /// </summary>
    public static Gen<Source> QueuedSourceWithNullOccurredAt =>
        from body in ValidSourceBody
        from sourceType in Gen.Elements(
            SourceType.SessionNote, SourceType.JournalEntry, SourceType.GMNote,
            SourceType.Transcript, SourceType.WebLink)
        from visibility in Gen.Elements(Enum.GetValues<VisibilityScope>())
        select new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = Guid.NewGuid(),
            Type = sourceType,
            Title = "Session without date",
            Body = body,
            OccurredAt = null,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = Guid.NewGuid(),
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Queued
        };

    /// <summary>
    /// Generates a Source entity in Queued status with a valid non-empty body and non-null OccurredAt.
    /// </summary>
    public static Gen<Source> QueuedSourceWithNonNullOccurredAt =>
        from body in ValidSourceBody
        from sourceType in Gen.Elements(
            SourceType.SessionNote, SourceType.JournalEntry, SourceType.GMNote,
            SourceType.Transcript, SourceType.WebLink)
        from visibility in Gen.Elements(Enum.GetValues<VisibilityScope>())
        from daysAgo in Gen.Choose(1, 365)
        select new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = Guid.NewGuid(),
            Type = sourceType,
            Title = $"Session {daysAgo}",
            Body = body,
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = Guid.NewGuid(),
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Queued
        };

    /// <summary>
    /// Generates a Source entity in Queued status with an empty/null/whitespace body.
    /// </summary>
    public static Gen<Source> QueuedSourceWithEmptyBody =>
        from body in EmptySourceBody
        from sourceType in Gen.Elements(
            SourceType.SessionNote, SourceType.JournalEntry, SourceType.GMNote)
        from visibility in Gen.Elements(Enum.GetValues<VisibilityScope>())
        select new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = Guid.NewGuid(),
            Type = sourceType,
            Title = "Empty source",
            Body = body,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = Guid.NewGuid(),
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Queued
        };

    /// <summary>
    /// Generates a Source entity in a non-Queued status.
    /// </summary>
    public static Gen<Source> NonQueuedSource =>
        from status in Gen.Elements(
            SourceProcessingStatus.Draft,
            SourceProcessingStatus.Ready,
            SourceProcessingStatus.Processing,
            SourceProcessingStatus.Processed,
            SourceProcessingStatus.Failed)
        from visibility in Gen.Elements(Enum.GetValues<VisibilityScope>())
        select new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = Guid.NewGuid(),
            Type = SourceType.SessionNote,
            Title = "Non-queued source",
            Body = "Some body content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = Guid.NewGuid(),
            Visibility = visibility,
            ProcessingStatus = status
        };

    /// <summary>
    /// Generates a valid AiExtractionResponse with 1-50 proposals.
    /// </summary>
    public static Gen<AiExtractionResponse> ValidExtractionResponse =>
        from proposalCount in Gen.Choose(1, 10)
        from proposals in ExtractionProposalGen.ListOf(proposalCount)
        from inputTokens in Gen.Choose(100, 10000)
        from outputTokens in Gen.Choose(50, 5000)
        from durationMs in Gen.Choose(100, 5000)
        select new AiExtractionResponse
        {
            Proposals = proposals.ToList(),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            DurationMs = durationMs,
            Model = "gpt-4o"
        };

    private static AiExtractionResponse CreateInvalidChangeTypeResponse() => new()
    {
        Proposals =
        [
            new ExtractionProposal
            {
                ChangeType = "InvalidType",
                TargetType = "Artifact",
                ProposedValue = new { name = "Test" },
                Rationale = "Valid rationale",
                Confidence = 0.5m
            }
        ],
        InputTokens = 100,
        OutputTokens = 50,
        TotalTokens = 150,
        DurationMs = 500,
        Model = "gpt-4o"
    };

    private static AiExtractionResponse CreateInvalidTargetTypeResponse() => new()
    {
        Proposals =
        [
            new ExtractionProposal
            {
                ChangeType = "CreateArtifact",
                TargetType = "InvalidTarget",
                ProposedValue = new { name = "Test" },
                Rationale = "Valid rationale",
                Confidence = 0.5m
            }
        ],
        InputTokens = 100,
        OutputTokens = 50,
        TotalTokens = 150,
        DurationMs = 500,
        Model = "gpt-4o"
    };

    private static AiExtractionResponse CreateLongRationaleResponse() => new()
    {
        Proposals =
        [
            new ExtractionProposal
            {
                ChangeType = "CreateArtifact",
                TargetType = "Artifact",
                ProposedValue = new { name = "Test" },
                Rationale = new string('x', 501),
                Confidence = 0.5m
            }
        ],
        InputTokens = 100,
        OutputTokens = 50,
        TotalTokens = 150,
        DurationMs = 500,
        Model = "gpt-4o"
    };

    private static AiExtractionResponse CreateInvalidConfidenceResponse() => new()
    {
        Proposals =
        [
            new ExtractionProposal
            {
                ChangeType = "CreateArtifact",
                TargetType = "Artifact",
                ProposedValue = new { name = "Test" },
                Rationale = "Valid rationale",
                Confidence = 1.5m
            }
        ],
        InputTokens = 100,
        OutputTokens = 50,
        TotalTokens = 150,
        DurationMs = 500,
        Model = "gpt-4o"
    };

    private static AiExtractionResponse CreateTooManyProposalsResponse()
    {
        var proposals = Enumerable.Range(0, 51).Select(i => new ExtractionProposal
        {
            ChangeType = "CreateArtifact",
            TargetType = "Artifact",
            ProposedValue = new { name = $"Artifact-{i}" },
            Rationale = $"Rationale for proposal {i}",
            Confidence = 0.5m
        }).ToList();

        return new AiExtractionResponse
        {
            Proposals = proposals,
            InputTokens = 100,
            OutputTokens = 50,
            TotalTokens = 150,
            DurationMs = 500,
            Model = "gpt-4o"
        };
    }
}

/// <summary>
/// FsCheck Arbitrary registrations for extraction-related property tests.
/// Use as [Property(Arbitrary = [typeof(ExtractionArbitraries)])]
/// </summary>
public class ExtractionArbitraries
{
    public static Arbitrary<Source> QueuedSources() =>
        ExtractionGenerators.QueuedSourceWithBody.ToArbitrary();

    public static Arbitrary<AiExtractionResponse> ValidResponses() =>
        ExtractionGenerators.ValidExtractionResponse.ToArbitrary();

    public static Arbitrary<ExtractionProposal> ValidProposals() =>
        ExtractionGenerators.ExtractionProposalGen.ToArbitrary();

    public static Arbitrary<ModelPricing> ModelPricings() =>
        ExtractionGenerators.ModelPricingGen.ToArbitrary();
}
