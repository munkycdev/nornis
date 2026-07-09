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
/// Property 11: AiUsageRecord Always Created
///
/// For any extraction that reaches the AI invocation step (source has non-empty body
/// and passes idempotency checks), an AiUsageRecord SHALL be created with the correct
/// WorldId, SourceId, OperationType=SourceExtraction, Model name, and DurationMs ≥ 0
/// — regardless of whether the AI call succeeds or fails.
///
/// **Validates: Requirements 6.1, 6.3**
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 11: AiUsageRecord Always Created")]
public class AiUsageRecordAlwaysCreatedPropertyTests
{
    private static ExtractionService CreateService(
        InMemorySourceRepository sourceRepo,
        InMemoryReviewBatchRepository reviewBatchRepo,
        InMemoryReviewProposalRepository reviewProposalRepo,
        InMemorySourceReferenceRepository sourceReferenceRepo,
        InMemoryAiUsageRecordRepository aiUsageRecordRepo,
        InMemoryArtifactRepository artifactRepo,
        InMemoryArtifactFactRepository artifactFactRepo,
        FakeAiExtractionClient fakeAiClient,
        FakeUnitOfWork unitOfWork,
        string modelName = "gpt-4o")
    {
        var options = Options.Create(new ExtractionOptions
        {
            AiModel = modelName,
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                [modelName] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 10.00m
                }
            }
        });

        var logger = NullLogger<ExtractionService>.Instance;

        return new ExtractionService(
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
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ExtractionArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 11: AiUsageRecord Always Created — success scenario")]
    public Property Successful_extraction_creates_ai_usage_record_with_correct_fields(
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

        var service = CreateService(
            sourceRepo, reviewBatchRepo, reviewProposalRepo,
            sourceReferenceRepo, aiUsageRecordRepo, artifactRepo,
            artifactFactRepo, fakeAiClient, unitOfWork);

        sourceRepo.Seed(source);
        fakeAiClient.SetupSuccess(aiResponse);

        // Act
        service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        var records = aiUsageRecordRepo.Records;

        return (records.Count >= 1)
            .Label("At least one AiUsageRecord should be created on success")
            .And((records.Any(r => r.WorldId == source.WorldId))
                .Label($"AiUsageRecord should have WorldId={source.WorldId}"))
            .And((records.Any(r => r.SourceId == source.Id))
                .Label($"AiUsageRecord should have SourceId={source.Id}"))
            .And((records.All(r => r.OperationType == AiOperationType.SourceExtraction))
                .Label("AiUsageRecord OperationType should be SourceExtraction"))
            .And((records.All(r => !string.IsNullOrEmpty(r.Model)))
                .Label("AiUsageRecord Model should not be empty"))
            .And((records.All(r => r.DurationMs >= 0))
                .Label("AiUsageRecord DurationMs should be >= 0"));
    }

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 11: AiUsageRecord Always Created — transient failure scenario")]
    public Property Transient_failure_creates_ai_usage_record_with_correct_fields()
    {
        return Prop.ForAll(
            ExtractionGenerators.QueuedSourceWithBody.ToArbitrary(),
            (Source source) =>
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

                var service = CreateService(
                    sourceRepo, reviewBatchRepo, reviewProposalRepo,
                    sourceReferenceRepo, aiUsageRecordRepo, artifactRepo,
                    artifactFactRepo, fakeAiClient, unitOfWork);

                sourceRepo.Seed(source);
                fakeAiClient.SetupTransientFailure(new HttpRequestException("Service unavailable"));

                // Act
                service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert
                var records = aiUsageRecordRepo.Records;

                return (records.Count >= 1)
                    .Label("At least one AiUsageRecord should be created on transient failure")
                    .And((records.Any(r => r.WorldId == source.WorldId))
                        .Label($"AiUsageRecord should have WorldId={source.WorldId}"))
                    .And((records.Any(r => r.SourceId == source.Id))
                        .Label($"AiUsageRecord should have SourceId={source.Id}"))
                    .And((records.All(r => r.OperationType == AiOperationType.SourceExtraction))
                        .Label("AiUsageRecord OperationType should be SourceExtraction"))
                    .And((records.All(r => !string.IsNullOrEmpty(r.Model)))
                        .Label("AiUsageRecord Model should not be empty"))
                    .And((records.All(r => r.DurationMs >= 0))
                        .Label("AiUsageRecord DurationMs should be >= 0"))
                    .And((records.All(r => !r.Succeeded))
                        .Label("AiUsageRecord Succeeded should be false on failure"));
            });
    }

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 11: AiUsageRecord Always Created — parse failure scenario")]
    public Property Parse_failure_creates_ai_usage_record_with_correct_fields()
    {
        return Prop.ForAll(
            ExtractionGenerators.QueuedSourceWithBody.ToArbitrary(),
            ExtractionGenerators.InvalidExtractionResponse.ToArbitrary(),
            (Source source, AiExtractionResponse invalidResponse) =>
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

                var service = CreateService(
                    sourceRepo, reviewBatchRepo, reviewProposalRepo,
                    sourceReferenceRepo, aiUsageRecordRepo, artifactRepo,
                    artifactFactRepo, fakeAiClient, unitOfWork);

                sourceRepo.Seed(source);
                fakeAiClient.SetupSuccess(invalidResponse);

                // Act
                service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert
                var records = aiUsageRecordRepo.Records;

                return (records.Count >= 1)
                    .Label("At least one AiUsageRecord should be created on parse failure")
                    .And((records.Any(r => r.WorldId == source.WorldId))
                        .Label($"AiUsageRecord should have WorldId={source.WorldId}"))
                    .And((records.Any(r => r.SourceId == source.Id))
                        .Label($"AiUsageRecord should have SourceId={source.Id}"))
                    .And((records.All(r => r.OperationType == AiOperationType.SourceExtraction))
                        .Label("AiUsageRecord OperationType should be SourceExtraction"))
                    .And((records.All(r => !string.IsNullOrEmpty(r.Model)))
                        .Label("AiUsageRecord Model should not be empty"))
                    .And((records.All(r => r.DurationMs >= 0))
                        .Label("AiUsageRecord DurationMs should be >= 0"))
                    .And((records.All(r => !r.Succeeded))
                        .Label("AiUsageRecord Succeeded should be false on parse failure"));
            });
    }

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 11: AiUsageRecord Always Created — timeout failure scenario")]
    public Property Timeout_failure_creates_ai_usage_record_with_correct_fields()
    {
        return Prop.ForAll(
            ExtractionGenerators.QueuedSourceWithBody.ToArbitrary(),
            (Source source) =>
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

                var service = CreateService(
                    sourceRepo, reviewBatchRepo, reviewProposalRepo,
                    sourceReferenceRepo, aiUsageRecordRepo, artifactRepo,
                    artifactFactRepo, fakeAiClient, unitOfWork);

                sourceRepo.Seed(source);
                // TaskCanceledException simulates a timeout when the service's CT is not canceled
                fakeAiClient.SetupTransientFailure(new TaskCanceledException("AI call timed out"));

                // Act
                service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert
                var records = aiUsageRecordRepo.Records;

                return (records.Count >= 1)
                    .Label("At least one AiUsageRecord should be created on timeout")
                    .And((records.Any(r => r.WorldId == source.WorldId))
                        .Label($"AiUsageRecord should have WorldId={source.WorldId}"))
                    .And((records.Any(r => r.SourceId == source.Id))
                        .Label($"AiUsageRecord should have SourceId={source.Id}"))
                    .And((records.All(r => r.OperationType == AiOperationType.SourceExtraction))
                        .Label("AiUsageRecord OperationType should be SourceExtraction"))
                    .And((records.All(r => !string.IsNullOrEmpty(r.Model)))
                        .Label("AiUsageRecord Model should not be empty"))
                    .And((records.All(r => r.DurationMs >= 0))
                        .Label("AiUsageRecord DurationMs should be >= 0"))
                    .And((records.All(r => !r.Succeeded))
                        .Label("AiUsageRecord Succeeded should be false on timeout"));
            });
    }
}
