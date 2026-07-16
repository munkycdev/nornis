using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Tests.Generators;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.PropertyTests;

/// <summary>
/// Property 14: Proposal Visibility Always Matches Source Visibility
///
/// For any source with any VisibilityScope and any AI response containing proposals
/// (regardless of what visibility the AI suggests in ProposedValueJson), every persisted
/// ReviewProposal's visibility within ProposedValueJson SHALL match the source's
/// VisibilityScope — Private sources produce only Private proposals, GMOnly sources
/// produce only GMOnly proposals, and PartyVisible sources produce only PartyVisible proposals.
///
/// **Validates: Requirements 8.1, 8.2**
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 14: Proposal Visibility Always Matches Source Visibility")]
public class ProposalVisibilityMatchesSourceVisibilityPropertyTests
{
    private static readonly string[] MismatchedVisibilities =
    [
        "Private", "GMOnly", "PartyVisible", "Public", "Unknown", "party_visible", ""
    ];

    private static readonly string[] ValidChangeTypes =
    [
        "CreateArtifact", "UpdateArtifact", "MergeArtifact",
        "AddFact", "UpdateFact", "AddRelationship", "UpdateRelationship"
    ];

    private static readonly string[] ValidTargetTypes =
    [
        "Artifact", "ArtifactFact", "ArtifactRelationship"
    ];

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 14: Proposal Visibility Always Matches Source Visibility")]
    public Property All_persisted_proposal_visibilities_match_source_visibility_regardless_of_ai_suggestion()
    {
        var gen =
            from sourceVisibility in Gen.Elements(Enum.GetValues<VisibilityScope>())
            from proposalCount in Gen.Choose(1, 10)
            from mismatchedVis in Gen.Elements(MismatchedVisibilities).ListOf(proposalCount)
            from changeTypes in Gen.Elements(ValidChangeTypes).ListOf(proposalCount)
            from targetTypes in Gen.Elements(ValidTargetTypes).ListOf(proposalCount)
            select (sourceVisibility, proposalCount, mismatchedVis, changeTypes, targetTypes);

        return Prop.ForAll(
            gen.ToArbitrary(),
            tuple =>
            {
                var (sourceVisibility, proposalCount, mismatchedVisibilities, changeTypes, targetTypes) = tuple;

                // Arrange: create source with specified visibility
                var source = new Source
                {
                    Id = Guid.NewGuid(),
                    WorldId = Guid.NewGuid(),
                    Type = SourceType.SessionNote,
                    Title = "Test Session",
                    Body = "We questioned Captain Voss in Black Harbor.",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    CreatedByUserId = Guid.NewGuid(),
                    Visibility = sourceVisibility,
                    ProcessingStatus = SourceProcessingStatus.Queued
                };

                // Build AI response with deliberately WRONG visibility values
                var proposals = Enumerable.Range(0, proposalCount).Select(i => new ExtractionProposal
                {
                    ChangeType = changeTypes[i],
                    TargetType = targetTypes[i],
                    TargetId = null,
                    ProposedValue = new Dictionary<string, object>
                    {
                        ["name"] = $"Artifact-{i}",
                        ["visibility"] = mismatchedVisibilities[i],
                        ["summary"] = "Some summary"
                    },
                    Rationale = $"Found reference to artifact {i} in source",
                    Confidence = 0.8m
                }).ToList();

                var aiResponse = new AiExtractionResponse
                {
                    Proposals = proposals,
                    InputTokens = 500,
                    OutputTokens = 200,
                    TotalTokens = 700,
                    DurationMs = 1000,
                    Model = "gpt-4o"
                };

                var sourceRepo = new InMemorySourceRepository();
                var reviewBatchRepo = new InMemoryReviewBatchRepository();
                var reviewProposalRepo = new InMemoryReviewProposalRepository();
                var sourceReferenceRepo = new InMemorySourceReferenceRepository();
                var aiUsageRecordRepo = new InMemoryAiUsageRecordRepository();
                var artifactRepo = new InMemoryArtifactRepository();
                var artifactFactRepo = new InMemoryArtifactFactRepository();
                var fakeAiClient = new FakeAiExtractionClient();
                var unitOfWork = new FakeUnitOfWork();

                var options = Options.Create(new ExtractionOptions
                {
                    AiModel = "gpt-4o",
                    AiEndpoint = "https://test.openai.azure.com/",
                    AiTimeoutSeconds = 60,
                    MaxArtifactContextCount = 50,
                    MaxFactsPerArtifact = 20,
                    MaxParseRetryAttempts = 2,
                    ModelPricing = new Dictionary<string, ModelPricing>
                    {
                        ["gpt-4o"] = new ModelPricing
                        {
                            InputPerMillionTokensUsd = 2.50m,
                            OutputPerMillionTokensUsd = 10.00m
                        }
                    }
                });

                var logger = NullLogger<ExtractionService>.Instance;

                var service = new ExtractionService(
                    sourceRepo,
                    new InMemoryCampaignRepository(),
                    reviewBatchRepo,
                    reviewProposalRepo,
                    sourceReferenceRepo,
                    aiUsageRecordRepo,
                    artifactRepo,
                    artifactFactRepo,
            new InMemoryArtifactRelationshipRepository(),
                    new InMemorySourceAttachmentRepository(),
                    new FakeBlobStorageService(),
                    fakeAiClient,
                    new FakeHandwritingTranscriptionClient(),
                    new FakeAiBudgetGuard(), unitOfWork,
                    options,
                    logger);

                sourceRepo.Seed(source);
                fakeAiClient.SetupSuccess(aiResponse);

                // Act
                service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: every persisted proposal's ProposedValueJson has visibility = source visibility
                var expectedVisibility = sourceVisibility.ToString();
                var persistedProposals = reviewProposalRepo.Proposals;

                var allMatch = persistedProposals.All(proposal =>
                {
                    var json = JsonDocument.Parse(proposal.ProposedValueJson);
                    if (json.RootElement.TryGetProperty("visibility", out var visProp))
                    {
                        return visProp.GetString() == expectedVisibility;
                    }
                    return false;
                });

                var noBroaderVisibility = persistedProposals.All(proposal =>
                {
                    var json = JsonDocument.Parse(proposal.ProposedValueJson);
                    if (json.RootElement.TryGetProperty("visibility", out var visProp))
                    {
                        var proposalVis = visProp.GetString();
                        return !IsBroaderThanSource(proposalVis, sourceVisibility);
                    }
                    return true;
                });

                return (persistedProposals.Count == proposalCount)
                    .Label($"Expected {proposalCount} proposals, got {persistedProposals.Count}")
                    .And(allMatch
                        .Label($"All proposals should have visibility={expectedVisibility} for {sourceVisibility} source"))
                    .And(noBroaderVisibility
                        .Label($"No proposal should have broader visibility than {sourceVisibility} source"));
            });
    }

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 14: Private source never produces GMOnly or PartyVisible proposals")]
    public Property Private_source_never_produces_broader_visibility_proposals()
    {
        var gen =
            from proposalCount in Gen.Choose(1, 10)
            from broadVisibilities in Gen.Elements("GMOnly", "PartyVisible", "Public").ListOf(proposalCount)
            select (proposalCount, broadVisibilities);

        return Prop.ForAll(
            gen.ToArbitrary(),
            tuple =>
            {
                var (proposalCount, broadVisibilities) = tuple;

                var source = new Source
                {
                    Id = Guid.NewGuid(),
                    WorldId = Guid.NewGuid(),
                    Type = SourceType.GMNote,
                    Title = "Private GM Note",
                    Body = "Secret information about the villain's plans.",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    CreatedByUserId = Guid.NewGuid(),
                    Visibility = VisibilityScope.Private,
                    ProcessingStatus = SourceProcessingStatus.Queued
                };

                var proposals = Enumerable.Range(0, proposalCount).Select(i => new ExtractionProposal
                {
                    ChangeType = "CreateArtifact",
                    TargetType = "Artifact",
                    ProposedValue = new Dictionary<string, object>
                    {
                        ["name"] = $"Secret-{i}",
                        ["visibility"] = broadVisibilities[i]
                    },
                    Rationale = "AI wrongly suggests broad visibility",
                    Confidence = 0.9m
                }).ToList();

                var aiResponse = new AiExtractionResponse
                {
                    Proposals = proposals,
                    InputTokens = 300,
                    OutputTokens = 150,
                    TotalTokens = 450,
                    DurationMs = 800,
                    Model = "gpt-4o"
                };

                var (service, reviewProposalRepo) = CreateServiceAndRepo(source, aiResponse);

                // Act
                service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: all proposals must have "Private" visibility
                var allPrivate = reviewProposalRepo.Proposals.All(p =>
                {
                    var json = JsonDocument.Parse(p.ProposedValueJson);
                    return json.RootElement.TryGetProperty("visibility", out var vis)
                        && vis.GetString() == "Private";
                });

                return allPrivate
                    .Label("Private source must never produce GMOnly or PartyVisible proposals");
            });
    }

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 14: GMOnly source never produces PartyVisible proposals")]
    public Property GMOnly_source_never_produces_PartyVisible_proposals()
    {
        var gen =
            from proposalCount in Gen.Choose(1, 10)
            from broadVisibilities in Gen.Elements("PartyVisible", "Public", "Private").ListOf(proposalCount)
            select (proposalCount, broadVisibilities);

        return Prop.ForAll(
            gen.ToArbitrary(),
            tuple =>
            {
                var (proposalCount, broadVisibilities) = tuple;

                var source = new Source
                {
                    Id = Guid.NewGuid(),
                    WorldId = Guid.NewGuid(),
                    Type = SourceType.GMNote,
                    Title = "GM-Only Note",
                    Body = "The Red Lodge is planning an ambush at the crossroads.",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    CreatedByUserId = Guid.NewGuid(),
                    Visibility = VisibilityScope.GMOnly,
                    ProcessingStatus = SourceProcessingStatus.Queued
                };

                var proposals = Enumerable.Range(0, proposalCount).Select(i => new ExtractionProposal
                {
                    ChangeType = "AddFact",
                    TargetType = "ArtifactFact",
                    ProposedValue = new Dictionary<string, object>
                    {
                        ["predicate"] = "plan",
                        ["value"] = $"Ambush plan {i}",
                        ["visibility"] = broadVisibilities[i]
                    },
                    Rationale = "AI suggests wrong visibility",
                    Confidence = 0.7m
                }).ToList();

                var aiResponse = new AiExtractionResponse
                {
                    Proposals = proposals,
                    InputTokens = 400,
                    OutputTokens = 200,
                    TotalTokens = 600,
                    DurationMs = 900,
                    Model = "gpt-4o"
                };

                var (service, reviewProposalRepo) = CreateServiceAndRepo(source, aiResponse);

                // Act
                service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: all proposals must have "GMOnly" visibility
                var allGmOnly = reviewProposalRepo.Proposals.All(p =>
                {
                    var json = JsonDocument.Parse(p.ProposedValueJson);
                    return json.RootElement.TryGetProperty("visibility", out var vis)
                        && vis.GetString() == "GMOnly";
                });

                return allGmOnly
                    .Label("GMOnly source must never produce PartyVisible proposals — all must be GMOnly");
            });
    }

    /// <summary>
    /// Determines whether the given proposal visibility string is broader than the source visibility.
    /// </summary>
    private static bool IsBroaderThanSource(string? proposalVisibility, VisibilityScope sourceVisibility)
    {
        return sourceVisibility switch
        {
            VisibilityScope.Private => proposalVisibility is "GMOnly" or "PartyVisible",
            VisibilityScope.GMOnly => proposalVisibility is "PartyVisible",
            VisibilityScope.PartyVisible => false,
            _ => false
        };
    }

    private static (ExtractionService Service, InMemoryReviewProposalRepository ProposalRepo)
        CreateServiceAndRepo(Source source, AiExtractionResponse aiResponse)
    {
        var sourceRepo = new InMemorySourceRepository();
        var reviewBatchRepo = new InMemoryReviewBatchRepository();
        var reviewProposalRepo = new InMemoryReviewProposalRepository();
        var sourceReferenceRepo = new InMemorySourceReferenceRepository();
        var aiUsageRecordRepo = new InMemoryAiUsageRecordRepository();
        var artifactRepo = new InMemoryArtifactRepository();
        var artifactFactRepo = new InMemoryArtifactFactRepository();
        var fakeAiClient = new FakeAiExtractionClient();
        var unitOfWork = new FakeUnitOfWork();

        var options = Options.Create(new ExtractionOptions
        {
            AiModel = "gpt-4o",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 10.00m
                }
            }
        });

        var logger = NullLogger<ExtractionService>.Instance;

        var service = new ExtractionService(
            sourceRepo,
            new InMemoryCampaignRepository(),
            reviewBatchRepo,
            reviewProposalRepo,
            sourceReferenceRepo,
            aiUsageRecordRepo,
            artifactRepo,
            artifactFactRepo,
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceAttachmentRepository(),
            new FakeBlobStorageService(),
            fakeAiClient,
            new FakeHandwritingTranscriptionClient(),
            new FakeAiBudgetGuard(), unitOfWork,
            options,
            logger);

        sourceRepo.Seed(source);
        fakeAiClient.SetupSuccess(aiResponse);

        return (service, reviewProposalRepo);
    }
}
