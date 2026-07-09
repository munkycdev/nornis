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

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 3: Non-Transient Failures Transition Source to Failed
///
/// For any source in Queued status where a non-transient error occurs (parse failure after retries
/// exhausted, validation failure, or malformed AI structured output), the ExtractionService SHALL
/// transition the source ProcessingStatus to Failed and return a NonTransientFailure outcome.
///
/// **Validates: Requirements 1.7, 10.1, 10.3**
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 3: Non-Transient Failures Transition Source to Failed")]
public class NonTransientFailuresTransitionSourceToFailedTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(NonTransientFailureArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 3: Non-Transient Failures Transition Source to Failed")]
    public void ParseFailureAfterRetriesExhausted_TransitionsSourceToFailed(Source source)
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
        var logger = NullLogger<ExtractionService>.Instance;

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

        var service = new ExtractionService(
            sourceRepo,
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

        // Seed the source in Queued status with a non-empty body
        sourceRepo.Seed(source);

        // Configure the AI client to return parse failures (invalid enum values)
        fakeAiClient.SetupParseFailure();

        // Act
        var outcome = service.ProcessExtractionAsync(source.Id, source.CampaignId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert — outcome type is NonTransientFailure
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure),
            "Outcome must be NonTransientFailure when parse retries are exhausted.");

        // Assert — source ProcessingStatus is Failed
        var updatedSource = sourceRepo.Sources.First(s => s.Id == source.Id);
        Assert.That(updatedSource.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed),
            "Source ProcessingStatus must transition to Failed after non-transient failure.");

        // Assert — no ReviewBatch in Pending status was created
        var pendingBatches = reviewBatchRepo.Batches
            .Where(b => b.SourceId == source.Id && b.Status == ReviewBatchStatus.Pending)
            .ToList();
        Assert.That(pendingBatches, Is.Empty,
            "No ReviewBatch in Pending status should be created on parse failure.");

        // Assert — AI client was called 3 times (initial + 2 retries)
        Assert.That(fakeAiClient.CallCount, Is.EqualTo(3),
            "AI client should be called 1 + MaxParseRetryAttempts (2) = 3 times total.");
    }
}

/// <summary>
/// Custom FsCheck arbitraries for non-transient failure property tests.
/// Provides sources in Queued status with non-empty bodies.
/// </summary>
public class NonTransientFailureArbitraries
{
    public static Arbitrary<Source> Sources() =>
        ExtractionGenerators.QueuedSourceWithBody.ToArbitrary();
}
