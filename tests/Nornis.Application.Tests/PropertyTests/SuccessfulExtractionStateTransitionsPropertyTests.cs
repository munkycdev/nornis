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
/// Property 1: Successful Extraction State Transitions
///
/// For any source in Queued status with a non-empty body, when the AI client returns
/// a valid response with one or more proposals, the ExtractionService SHALL transition
/// the source through Queued → Processing → Processed, and the final source status
/// SHALL be Processed.
///
/// **Validates: Requirements 1.1, 1.2**
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 1: Successful Extraction State Transitions")]
public class SuccessfulExtractionStateTransitionsPropertyTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ExtractionArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 1: Successful Extraction State Transitions")]
    public void Successful_extraction_transitions_source_to_processed(
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

        var service = new ExtractionService(
            sourceRepo,
            new InMemoryCampaignRepository(),
            reviewBatchRepo,
            reviewProposalRepo,
            sourceReferenceRepo,
            aiUsageRecordRepo,
            artifactRepo,
            artifactFactRepo,
            fakeAiClient,
            new FakeAiBudgetGuard(), unitOfWork,
            options,
            logger);

        // Seed source in Queued status with non-empty body
        sourceRepo.Seed(source);

        // Configure fake AI client to return valid response
        fakeAiClient.SetupSuccess(aiResponse);

        // Act
        var outcome = service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        var updatedSource = sourceRepo.Sources.First(s => s.Id == source.Id);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success),
            "Outcome should be Success for a valid extraction.");

        Assert.That(updatedSource.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed),
            "Source should be in Processed status after successful extraction.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ExtractionArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 1: Successful Extraction State Transitions")]
    public void Successful_extraction_records_queued_to_processing_to_processed_transitions(
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

        var service = new ExtractionService(
            sourceRepo,
            new InMemoryCampaignRepository(),
            reviewBatchRepo,
            reviewProposalRepo,
            sourceReferenceRepo,
            aiUsageRecordRepo,
            artifactRepo,
            artifactFactRepo,
            fakeAiClient,
            new FakeAiBudgetGuard(), unitOfWork,
            options,
            logger);

        // Seed source
        sourceRepo.Seed(source);

        // Configure fake AI client
        fakeAiClient.SetupSuccess(aiResponse);

        // Act
        service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert — the state transitions should include Queued → Processing and Processing → Processed
        var transitions = sourceRepo.StatusTransitions;

        var hasQueuedToProcessing = transitions.Any(t =>
            t.SourceId == source.Id &&
            t.From == SourceProcessingStatus.Queued &&
            t.To == SourceProcessingStatus.Processing);

        var hasProcessingToProcessed = transitions.Any(t =>
            t.SourceId == source.Id &&
            t.From == SourceProcessingStatus.Processing &&
            t.To == SourceProcessingStatus.Processed);

        Assert.That(hasQueuedToProcessing, Is.True,
            "Should record Queued → Processing transition.");

        Assert.That(hasProcessingToProcessed, Is.True,
            "Should record Processing → Processed transition.");
    }
}
