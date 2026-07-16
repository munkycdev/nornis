using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Tests.Generators;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.PropertyTests;

/// <summary>
/// Property 9: Context Assembly Respects Visibility Scope
///
/// For any source with a given VisibilityScope, the artifacts included in the context
/// SHALL only contain artifacts whose visibility is permitted for that scope:
/// Private sources include only Private artifacts (of the same creator);
/// GMOnly sources include GMOnly and PartyVisible artifacts;
/// PartyVisible sources include only PartyVisible artifacts.
///
/// **Validates: Requirements 4.5**
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 9: Context Assembly Respects Visibility Scope")]
public class ContextAssemblyRespectsVisibilityScopePropertyTests
{
    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 9: Context Assembly Respects Visibility Scope")]
    public Property Context_assembly_only_includes_artifacts_with_permitted_visibility()
    {
        return Prop.ForAll(
            ExtractionGenerators.SourceVisibilityScenario.ToArbitrary(),
            scenario =>
            {
                var (sourceVisibility, allowedScopes) = scenario;

                // Arrange
                var worldId = Guid.NewGuid();
                var creatorUserId = Guid.NewGuid();

                var source = new Source
                {
                    Id = Guid.NewGuid(),
                    WorldId = worldId,
                    Type = SourceType.SessionNote,
                    Title = "Test Session",
                    Body = "We questioned Captain Voss in Black Harbor near Iron Gate.",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    CreatedByUserId = creatorUserId,
                    Visibility = sourceVisibility,
                    ProcessingStatus = SourceProcessingStatus.Queued
                };

                // Create artifacts for each visibility scope
                var privateArtifact = CreateArtifact(worldId, "Captain Voss", VisibilityScope.Private);
                var gmOnlyArtifact = CreateArtifact(worldId, "Black Harbor", VisibilityScope.GMOnly);
                var partyVisibleArtifact = CreateArtifact(worldId, "Iron Gate", VisibilityScope.PartyVisible);

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

                // Seed data
                sourceRepo.Seed(source);
                artifactRepo.Seed(privateArtifact, gmOnlyArtifact, partyVisibleArtifact);

                // Configure AI to return a valid response
                fakeAiClient.SetupSuccess(new AiExtractionResponse
                {
                    Proposals =
                    [
                        new ExtractionProposal
                        {
                            ChangeType = "CreateArtifact",
                            TargetType = "Artifact",
                            ProposedValue = new Dictionary<string, object>
                            {
                                ["name"] = "New Artifact",
                                ["visibility"] = sourceVisibility.ToString()
                            },
                            Rationale = "Found in source",
                            Confidence = 0.8m
                        }
                    ],
                    InputTokens = 500,
                    OutputTokens = 200,
                    TotalTokens = 700,
                    DurationMs = 1000,
                    Model = "gpt-4o"
                });

                // Act
                service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: the AI request's ExistingArtifacts only contains permitted visibility artifacts
                var request = fakeAiClient.Requests.Single();
                var contextArtifactNames = request.ExistingArtifacts.Select(a => a.Name).ToList();

                // Determine which artifacts should and should not appear
                var allArtifacts = new[]
                {
                    (privateArtifact.Name, privateArtifact.Visibility),
                    (gmOnlyArtifact.Name, gmOnlyArtifact.Visibility),
                    (partyVisibleArtifact.Name, partyVisibleArtifact.Visibility)
                };

                var expectedNames = allArtifacts
                    .Where(a => allowedScopes.Contains(a.Visibility))
                    .Select(a => a.Name)
                    .ToHashSet();

                var forbiddenNames = allArtifacts
                    .Where(a => !allowedScopes.Contains(a.Visibility))
                    .Select(a => a.Name)
                    .ToHashSet();

                var allExpectedPresent = expectedNames.All(n => contextArtifactNames.Contains(n));
                var noForbiddenPresent = !contextArtifactNames.Any(n => forbiddenNames.Contains(n));

                return allExpectedPresent
                    .Label($"Expected artifacts {string.Join(", ", expectedNames)} to be in context for {sourceVisibility} source")
                    .And(noForbiddenPresent
                        .Label($"Forbidden artifacts {string.Join(", ", forbiddenNames)} should NOT be in context for {sourceVisibility} source"));
            });
    }

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 9: Context Assembly Respects Visibility Scope")]
    public Property Context_assembly_with_random_artifact_counts_respects_visibility()
    {
        var gen =
            from sourceVisibility in Gen.Elements(Enum.GetValues<VisibilityScope>())
            from artifactCount in Gen.Choose(3, 15)
            select (sourceVisibility, artifactCount);

        return Prop.ForAll(
            gen.ToArbitrary(),
            tuple =>
            {
                var (sourceVisibility, artifactCount) = tuple;

                var allowedScopes = sourceVisibility switch
                {
                    VisibilityScope.Private => new[] { VisibilityScope.Private },
                    VisibilityScope.GMOnly => new[] { VisibilityScope.GMOnly, VisibilityScope.PartyVisible },
                    VisibilityScope.PartyVisible => new[] { VisibilityScope.PartyVisible },
                    _ => new[] { VisibilityScope.PartyVisible }
                };

                var worldId = Guid.NewGuid();

                // Create a source with a body that contains some artifact names
                var source = new Source
                {
                    Id = Guid.NewGuid(),
                    WorldId = worldId,
                    Type = SourceType.SessionNote,
                    Title = "Random Session",
                    Body = "We explored the area and found interesting things.",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    CreatedByUserId = Guid.NewGuid(),
                    Visibility = sourceVisibility,
                    ProcessingStatus = SourceProcessingStatus.Queued
                };

                // Generate artifacts with mixed visibilities
                var artifacts = Enumerable.Range(0, artifactCount).Select(i =>
                {
                    var vis = (VisibilityScope)(i % 3); // Cycles through Private(0), GMOnly(1), PartyVisible(2)
                    return CreateArtifact(worldId, $"Artifact-{i}", vis);
                }).ToArray();

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

                // Seed data
                sourceRepo.Seed(source);
                artifactRepo.Seed(artifacts);

                // Configure AI to return a valid response
                fakeAiClient.SetupSuccess(new AiExtractionResponse
                {
                    Proposals =
                    [
                        new ExtractionProposal
                        {
                            ChangeType = "CreateArtifact",
                            TargetType = "Artifact",
                            ProposedValue = new Dictionary<string, object>
                            {
                                ["name"] = "Generated",
                                ["visibility"] = sourceVisibility.ToString()
                            },
                            Rationale = "Found in source",
                            Confidence = 0.7m
                        }
                    ],
                    InputTokens = 300,
                    OutputTokens = 150,
                    TotalTokens = 450,
                    DurationMs = 800,
                    Model = "gpt-4o"
                });

                // Act
                service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: every artifact in context has a permitted visibility
                var request = fakeAiClient.Requests.Single();
                var contextArtifactNames = request.ExistingArtifacts.Select(a => a.Name).ToHashSet();

                // Check that no artifact with a forbidden visibility leaked into context
                var forbiddenArtifacts = artifacts
                    .Where(a => !allowedScopes.Contains(a.Visibility))
                    .Select(a => a.Name)
                    .ToList();

                var noForbiddenLeaked = !contextArtifactNames.Any(n => forbiddenArtifacts.Contains(n));

                // Check that all artifacts in context have a permitted visibility
                var contextArtifactsFromSource = artifacts
                    .Where(a => contextArtifactNames.Contains(a.Name))
                    .ToList();

                var allContextHavePermittedVisibility = contextArtifactsFromSource
                    .All(a => allowedScopes.Contains(a.Visibility));

                return noForbiddenLeaked
                    .Label($"No forbidden-visibility artifacts should appear in context for {sourceVisibility} source")
                    .And(allContextHavePermittedVisibility
                        .Label($"All context artifacts must have permitted visibility for {sourceVisibility} source"));
            });
    }

    private static Artifact CreateArtifact(Guid worldId, string name, VisibilityScope visibility) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = worldId,
        Type = ArtifactType.Character,
        Name = name,
        Summary = $"Summary of {name}",
        Visibility = visibility,
        Confidence = 0.8m,
        Status = ArtifactStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };
}
