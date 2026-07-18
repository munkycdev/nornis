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
/// Property 8: Context Payload Respects Facts Limit
///
/// For any artifact included in the context that has more than MaxFactsPerArtifact facts,
/// the context payload SHALL include exactly MaxFactsPerArtifact facts for that artifact
/// ordered by UpdatedAt descending, and each fact SHALL include its Predicate and Value.
///
/// **Validates: Requirements 4.4**
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 8: Context Payload Respects Facts Limit")]
public class ContextPayloadRespectsFactsLimitPropertyTests
{
    private const int MaxFactsPerArtifact = 20;

    /// <summary>
    /// Generator producing an artifact with more than MaxFactsPerArtifact facts,
    /// each with distinct UpdatedAt timestamps for ordering verification.
    /// </summary>
    private static Gen<(Artifact Artifact, List<ArtifactFact> Facts)> ArtifactWithExcessFacts(Guid worldId, VisibilityScope visibility) =>
        from factCount in Gen.Choose(MaxFactsPerArtifact + 1, MaxFactsPerArtifact + 15)
        from name in Gen.Elements("Captain Voss", "Black Harbor", "Silver Key", "Tavrin", "The Red Lodge")
        let artifactId = Guid.NewGuid()
        let artifact = new Artifact
        {
            Id = artifactId,
            WorldId = worldId,
            Type = ArtifactType.Character,
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
            Predicate = $"predicate-{i:D3}",
            Value = $"value-{i:D3}",
            Confidence = 0.7m,
            TruthState = TruthState.Likely,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
            // Distinct UpdatedAt so ordering is deterministic
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
        }).ToList()
        select (artifact, facts);

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 8: Context Payload Respects Facts Limit")]
    public Property Context_includes_exactly_MaxFactsPerArtifact_facts_ordered_by_UpdatedAt_descending()
    {
        return Prop.ForAll(
            ArtifactWithExcessFacts(Guid.NewGuid(), VisibilityScope.PartyVisible).ToArbitrary(),
            ExtractionGenerators.ValidExtractionResponse.ToArbitrary(),
            ((Artifact Artifact, List<ArtifactFact> Facts) artifactData, AiExtractionResponse aiResponse) =>
            {
                var (artifact, allFacts) = artifactData;
                var worldId = artifact.WorldId;

                // Create a source whose body contains the artifact name (so it gets name-matched)
                var source = new Source
                {
                    Id = Guid.NewGuid(),
                    WorldId = worldId,
                    Type = SourceType.SessionNote,
                    Title = "Test session",
                    Body = $"We met {artifact.Name} at the docks.",
                    OccurredAt = null,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    CreatedByUserId = Guid.NewGuid(),
                    Visibility = VisibilityScope.PartyVisible,
                    ProcessingStatus = SourceProcessingStatus.Queued
                };

                // Arrange
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
                    MaxFactsPerArtifact = MaxFactsPerArtifact,
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
                    new InMemoryMapPlacemarkRepository(),
                    new FakeBlobStorageService(),
                    new FakePdfTextExtractor(),
                    fakeAiClient,
                    new FakeHandwritingTranscriptionClient(),
                    new FakeImageReadingClient(),
                    new FakeMapExtractionClient(),
                    new FakeAiBudgetGuard(), unitOfWork,
                    options,
                    logger);

                sourceRepo.Seed(source);
                artifactRepo.Seed(artifact);
                artifactFactRepo.Seed(allFacts);
                fakeAiClient.SetupSuccess(aiResponse);

                // Act
                service.ProcessExtractionAsync(source.Id, worldId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert — inspect the request the fake AI client received
                var request = fakeAiClient.Requests.FirstOrDefault();
                if (request is null)
                    return false.Label("AI client should have been called");

                var contextArtifact = request.ExistingArtifacts
                    .FirstOrDefault(a => a.Id == artifact.Id);

                if (contextArtifact is null)
                    return false.Label($"Artifact '{artifact.Name}' should appear in context");

                // The artifact has more than MaxFactsPerArtifact facts, so exactly MaxFactsPerArtifact should be included
                var factsInContext = contextArtifact.Facts;
                var factsCountCorrect = factsInContext.Count == MaxFactsPerArtifact;

                // Verify ordering: facts should be the top-N by UpdatedAt descending
                var expectedFacts = allFacts
                    .OrderByDescending(f => f.UpdatedAt)
                    .Take(MaxFactsPerArtifact)
                    .ToList();

                var orderCorrect = true;
                for (var i = 0; i < factsInContext.Count && i < expectedFacts.Count; i++)
                {
                    if (factsInContext[i].Predicate != expectedFacts[i].Predicate ||
                        factsInContext[i].Value != expectedFacts[i].Value)
                    {
                        orderCorrect = false;
                        break;
                    }
                }

                // Verify each fact includes Predicate and Value (non-null, non-empty)
                var allFactsHavePredicateAndValue = factsInContext.All(f =>
                    !string.IsNullOrEmpty(f.Predicate) && !string.IsNullOrEmpty(f.Value));

                return factsCountCorrect
                    .Label($"Should include exactly {MaxFactsPerArtifact} facts, but got {factsInContext.Count}")
                    .And(orderCorrect
                        .Label("Facts should be ordered by UpdatedAt descending"))
                    .And(allFactsHavePredicateAndValue
                        .Label("Each fact should include non-empty Predicate and Value"));
            });
    }
}
