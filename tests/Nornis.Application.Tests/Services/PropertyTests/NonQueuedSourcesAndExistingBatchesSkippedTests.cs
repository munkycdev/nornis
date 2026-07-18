using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Tests.Generators;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 2: Non-Queued Sources and Existing Batches Are Skipped
///
/// For any source whose ProcessingStatus is not Queued (Draft, Ready, Processed, or Failed —
/// Processing is excluded: with no batch it is a crashed run the worker resumes),
/// or any source that already has a ReviewBatch in Pending, InReview, or Completed status,
/// processing the extraction message SHALL return a Skipped outcome without creating new ReviewBatch,
/// ReviewProposal, or AiUsageRecord records, and without modifying the source's ProcessingStatus.
///
/// **Validates: Requirements 1.4, 2.1, 2.2**
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 2: Non-Queued Sources and Existing Batches Are Skipped")]
public class NonQueuedSourcesAndExistingBatchesSkippedTests
{
    private static ExtractionService CreateService(
        InMemorySourceRepository sourceRepo,
        InMemoryReviewBatchRepository batchRepo,
        InMemoryReviewProposalRepository proposalRepo,
        InMemorySourceReferenceRepository sourceRefRepo,
        InMemoryAiUsageRecordRepository usageRepo,
        InMemoryArtifactRepository artifactRepo,
        InMemoryArtifactFactRepository factRepo,
        FakeAiExtractionClient aiClient,
        FakeUnitOfWork unitOfWork)
    {
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

        return new ExtractionService(
            sourceRepo,
            new InMemoryCampaignRepository(),
            batchRepo,
            proposalRepo,
            sourceRefRepo,
            usageRepo,
            artifactRepo,
            factRepo,
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceAttachmentRepository(),
            new InMemoryMapPlacemarkRepository(),
            new FakeBlobStorageService(),
            new FakePdfTextExtractor(),
            aiClient,
            new FakeHandwritingTranscriptionClient(),
            new FakeImageReadingClient(),
            new FakeMapExtractionClient(),
            new FakeAiBudgetGuard(), unitOfWork,
            options,
            NullLogger<ExtractionService>.Instance);
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonQueuedSourceArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 2: Non-Queued Sources and Existing Batches Are Skipped")]
    public void NonQueuedSource_ReturnsSkipped_NoNewRecords_StatusUnchanged(Source source)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var batchRepo = new InMemoryReviewBatchRepository();
        var proposalRepo = new InMemoryReviewProposalRepository();
        var sourceRefRepo = new InMemorySourceReferenceRepository();
        var usageRepo = new InMemoryAiUsageRecordRepository();
        var artifactRepo = new InMemoryArtifactRepository();
        var factRepo = new InMemoryArtifactFactRepository();
        var aiClient = new FakeAiExtractionClient();
        var unitOfWork = new FakeUnitOfWork();

        sourceRepo.Seed(source);
        var originalStatus = source.ProcessingStatus;

        var service = CreateService(
            sourceRepo, batchRepo, proposalRepo, sourceRefRepo,
            usageRepo, artifactRepo, factRepo, aiClient, unitOfWork);

        // Act
        var outcome = service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert - outcome is Skipped
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Skipped),
            $"Non-Queued source (status={originalStatus}) should return Skipped outcome.");

        // Assert - no new ReviewBatch created
        Assert.That(batchRepo.Batches, Is.Empty,
            "No new ReviewBatch should be created for non-Queued sources.");

        // Assert - no new ReviewProposal created
        Assert.That(proposalRepo.Proposals, Is.Empty,
            "No new ReviewProposal should be created for non-Queued sources.");

        // Assert - no AI calls made
        Assert.That(aiClient.CallCount, Is.EqualTo(0),
            "No AI calls should be made for non-Queued sources.");

        // Assert - no AiUsageRecord created
        Assert.That(usageRepo.Records, Is.Empty,
            "No AiUsageRecord should be created for non-Queued sources.");

        // Assert - source status unchanged
        var updatedSource = sourceRepo.Sources.First(s => s.Id == source.Id);
        Assert.That(updatedSource.ProcessingStatus, Is.EqualTo(originalStatus),
            "Source ProcessingStatus should not be modified for non-Queued sources.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(QueuedSourceWithExistingBatchArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 2: Non-Queued Sources and Existing Batches Are Skipped")]
    public void QueuedSourceWithExistingBatch_ReturnsSkipped_NoAdditionalBatch(
        SourceWithExistingBatch input)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var batchRepo = new InMemoryReviewBatchRepository();
        var proposalRepo = new InMemoryReviewProposalRepository();
        var sourceRefRepo = new InMemorySourceReferenceRepository();
        var usageRepo = new InMemoryAiUsageRecordRepository();
        var artifactRepo = new InMemoryArtifactRepository();
        var factRepo = new InMemoryArtifactFactRepository();
        var aiClient = new FakeAiExtractionClient();
        var unitOfWork = new FakeUnitOfWork();

        sourceRepo.Seed(input.Source);
        batchRepo.CreateAsync(input.ExistingBatch).GetAwaiter().GetResult();

        var initialBatchCount = batchRepo.Batches.Count;

        var service = CreateService(
            sourceRepo, batchRepo, proposalRepo, sourceRefRepo,
            usageRepo, artifactRepo, factRepo, aiClient, unitOfWork);

        // Act
        var outcome = service.ProcessExtractionAsync(
            input.Source.Id, input.Source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert - outcome is Skipped
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Skipped),
            $"Source with existing ReviewBatch (status={input.ExistingBatch.Status}) should return Skipped outcome.");

        // Assert - no additional ReviewBatch created
        Assert.That(batchRepo.Batches.Count, Is.EqualTo(initialBatchCount),
            "No additional ReviewBatch should be created when one already exists.");

        // Assert - no new ReviewProposal created
        Assert.That(proposalRepo.Proposals, Is.Empty,
            "No new ReviewProposal should be created when existing batch exists.");

        // Assert - no AI calls made
        Assert.That(aiClient.CallCount, Is.EqualTo(0),
            "No AI calls should be made when existing batch exists.");

        // Assert - no AiUsageRecord created
        Assert.That(usageRepo.Records, Is.Empty,
            "No AiUsageRecord should be created when existing batch exists.");

        // Assert - source status remains Queued (not modified)
        var updatedSource = sourceRepo.Sources.First(s => s.Id == input.Source.Id);
        Assert.That(updatedSource.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued),
            "Source ProcessingStatus should remain Queued when skipped due to existing batch.");
    }
}

/// <summary>
/// Input model for the existing batch scenario.
/// </summary>
public record SourceWithExistingBatch(Source Source, ReviewBatch ExistingBatch);

/// <summary>
/// Arbitrary for non-Queued sources using ExtractionGenerators.NonQueuedSource.
/// </summary>
public class NonQueuedSourceArbitraries
{
    public static Arbitrary<Source> Sources() =>
        ExtractionGenerators.NonQueuedSource.ToArbitrary();
}

/// <summary>
/// Arbitrary for Queued sources that already have a ReviewBatch in Pending/InReview/Completed.
/// </summary>
public class QueuedSourceWithExistingBatchArbitraries
{
    public static Arbitrary<SourceWithExistingBatch> SourceWithExistingBatches()
    {
        var gen =
            from source in ExtractionGenerators.QueuedSourceWithBody
            from batchStatus in Gen.Elements(
                ReviewBatchStatus.Pending,
                ReviewBatchStatus.InReview,
                ReviewBatchStatus.Completed)
            let batch = new ReviewBatch
            {
                Id = Guid.NewGuid(),
                WorldId = source.WorldId,
                SourceId = source.Id,
                Status = batchStatus,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            }
            select new SourceWithExistingBatch(source, batch);

        return gen.ToArbitrary();
    }
}
