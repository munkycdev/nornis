using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Ai;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Tests.Generators;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.PropertyTests;

/// <summary>
/// Property 6: Source Fields Correctly Mapped to AI Request
///
/// For any source with non-empty Body, the ExtractionRequest passed to IAiExtractionClient
/// SHALL contain the source's Body, Title, Type name, and Visibility name. If the source's
/// OccurredAt is non-null, the request SHALL include it; if OccurredAt is null, the request's
/// OccurredAt SHALL be null.
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 6: Source Fields Correctly Mapped to AI Request")]
public class SourceFieldsMappedToAiRequestPropertyTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ExtractionArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 6: Source Fields Correctly Mapped to AI Request")]
    public void Source_body_title_type_visibility_and_occurred_at_are_correctly_mapped_to_extraction_request(
        Source source,
        AiExtractionResponse aiResponse)
    {
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

        var usageRecorder = TestUsageRecorder.Wrap(aiUsageRecordRepo);
        var budgetGuard = new FakeAiBudgetGuard();
        var attachmentRepo = new InMemorySourceAttachmentRepository();
        var blobStorage = new FakeBlobStorageService();

        var mapPipeline = new MapExtractionPipeline(
            attachmentRepo,
            new InMemoryMapPlacemarkRepository(),
            artifactRepo,
            blobStorage,
            new FakeMapExtractionClient(),
            budgetGuard,
            usageRecorder,
            options,
            NullLogger<MapExtractionPipeline>.Instance);

        var textDerivation = new SourceTextDerivation(
            sourceRepo,
            attachmentRepo,
            blobStorage,
            new FakePdfTextExtractor(),
            new FakeHandwritingTranscriptionClient(),
            new FakeImageReadingClient(),
            budgetGuard,
            usageRecorder,
            options,
            NullLogger<SourceTextDerivation>.Instance);

        var service = new ExtractionService(
            sourceRepo,
            new InMemoryCampaignRepository(),
            reviewBatchRepo,
            reviewProposalRepo,
            sourceReferenceRepo,
            usageRecorder,
            artifactRepo,
            artifactFactRepo,
            new InMemoryArtifactRelationshipRepository(),
            fakeAiClient,
            mapPipeline,
            textDerivation,
            budgetGuard,
            unitOfWork,
            options,
            logger,
            passageRetriever: NoOpReferencePassageRetriever.Instance,
            replayAdvancer: NoOpExtractionReplayAdvancer.Instance);

        // Seed source in Queued status with non-empty body (guaranteed by ExtractionArbitraries)
        sourceRepo.Seed(source);

        // Configure fake AI client to return valid response
        fakeAiClient.SetupSuccess(aiResponse);

        // Act
        var outcome = service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert — the fake AI client should have captured exactly one request
        Assert.That(fakeAiClient.Requests, Has.Count.EqualTo(1),
            "Exactly one AI call should be made for a source with non-empty body.");

        var parsed = ExtractionPromptReader.Parse(fakeAiClient.Requests[0].UserMessage);

        // Boundary newlines on the body are consumed by the prompt's section framing, so compare trimmed.
        Assert.That(parsed.SourceBody, Is.EqualTo(source.Body!.Trim('\n')),
            "The prompt's Source Content section should match the source's Body.");

        Assert.That(parsed.SourceTitle, Is.EqualTo(source.Title),
            "The prompt's Title line should match the source's Title.");

        Assert.That(parsed.SourceType, Is.EqualTo(source.Type.ToString()),
            "The prompt's Type line should match the source's Type name.");

        Assert.That(parsed.SourceVisibility, Is.EqualTo(source.Visibility.ToString()),
            "The prompt's Visibility line should match the source's Visibility name.");

        Assert.That(parsed.OccurredAt, Is.EqualTo(source.OccurredAt),
            "The prompt's Occurred At line should match the source's OccurredAt (including null).");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ExtractionArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 6: null OccurredAt maps to null in request")]
    public void Source_with_null_occurred_at_produces_null_in_request(
        Source source,
        AiExtractionResponse aiResponse)
    {
        // Force OccurredAt to null for this specific test scenario
        source.OccurredAt = null;

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

        var usageRecorder = TestUsageRecorder.Wrap(aiUsageRecordRepo);
        var budgetGuard = new FakeAiBudgetGuard();
        var attachmentRepo = new InMemorySourceAttachmentRepository();
        var blobStorage = new FakeBlobStorageService();

        var mapPipeline = new MapExtractionPipeline(
            attachmentRepo,
            new InMemoryMapPlacemarkRepository(),
            artifactRepo,
            blobStorage,
            new FakeMapExtractionClient(),
            budgetGuard,
            usageRecorder,
            options,
            NullLogger<MapExtractionPipeline>.Instance);

        var textDerivation = new SourceTextDerivation(
            sourceRepo,
            attachmentRepo,
            blobStorage,
            new FakePdfTextExtractor(),
            new FakeHandwritingTranscriptionClient(),
            new FakeImageReadingClient(),
            budgetGuard,
            usageRecorder,
            options,
            NullLogger<SourceTextDerivation>.Instance);

        var service = new ExtractionService(
            sourceRepo,
            new InMemoryCampaignRepository(),
            reviewBatchRepo,
            reviewProposalRepo,
            sourceReferenceRepo,
            usageRecorder,
            artifactRepo,
            artifactFactRepo,
            new InMemoryArtifactRelationshipRepository(),
            fakeAiClient,
            mapPipeline,
            textDerivation,
            budgetGuard,
            unitOfWork,
            options,
            logger,
            passageRetriever: NoOpReferencePassageRetriever.Instance,
            replayAdvancer: NoOpExtractionReplayAdvancer.Instance);

        sourceRepo.Seed(source);
        fakeAiClient.SetupSuccess(aiResponse);

        // Act
        service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(fakeAiClient.Requests, Has.Count.EqualTo(1));
        var parsed = ExtractionPromptReader.Parse(fakeAiClient.Requests[0].UserMessage);

        Assert.That(parsed.OccurredAt, Is.Null,
            "The prompt should carry no Occurred At line when source OccurredAt is null.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ExtractionArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 6: non-null OccurredAt is preserved in request")]
    public void Source_with_non_null_occurred_at_is_included_in_request(
        Source source,
        AiExtractionResponse aiResponse)
    {
        // Force OccurredAt to a non-null value for this specific test scenario
        source.OccurredAt = DateTimeOffset.UtcNow.AddDays(-7);

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

        var usageRecorder = TestUsageRecorder.Wrap(aiUsageRecordRepo);
        var budgetGuard = new FakeAiBudgetGuard();
        var attachmentRepo = new InMemorySourceAttachmentRepository();
        var blobStorage = new FakeBlobStorageService();

        var mapPipeline = new MapExtractionPipeline(
            attachmentRepo,
            new InMemoryMapPlacemarkRepository(),
            artifactRepo,
            blobStorage,
            new FakeMapExtractionClient(),
            budgetGuard,
            usageRecorder,
            options,
            NullLogger<MapExtractionPipeline>.Instance);

        var textDerivation = new SourceTextDerivation(
            sourceRepo,
            attachmentRepo,
            blobStorage,
            new FakePdfTextExtractor(),
            new FakeHandwritingTranscriptionClient(),
            new FakeImageReadingClient(),
            budgetGuard,
            usageRecorder,
            options,
            NullLogger<SourceTextDerivation>.Instance);

        var service = new ExtractionService(
            sourceRepo,
            new InMemoryCampaignRepository(),
            reviewBatchRepo,
            reviewProposalRepo,
            sourceReferenceRepo,
            usageRecorder,
            artifactRepo,
            artifactFactRepo,
            new InMemoryArtifactRelationshipRepository(),
            fakeAiClient,
            mapPipeline,
            textDerivation,
            budgetGuard,
            unitOfWork,
            options,
            logger,
            passageRetriever: NoOpReferencePassageRetriever.Instance,
            replayAdvancer: NoOpExtractionReplayAdvancer.Instance);

        sourceRepo.Seed(source);
        fakeAiClient.SetupSuccess(aiResponse);

        // Act
        service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(fakeAiClient.Requests, Has.Count.EqualTo(1));
        var parsed = ExtractionPromptReader.Parse(fakeAiClient.Requests[0].UserMessage);

        Assert.That(parsed.OccurredAt, Is.Not.Null,
            "The prompt's Occurred At line should be present when source OccurredAt is set.");

        Assert.That(parsed.OccurredAt, Is.EqualTo(source.OccurredAt),
            "The prompt's Occurred At line should match source OccurredAt value.");
    }
}
