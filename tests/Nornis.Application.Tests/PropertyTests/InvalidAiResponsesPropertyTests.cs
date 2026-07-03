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
/// Property 10: Invalid AI Responses Are Treated as Failures
///
/// For any AI response JSON that violates the structured output schema (missing required fields,
/// changeType not in allowed values, rationale exceeding 500 characters, confidence outside 0.0–1.0,
/// or proposals exceeding 50), the ExtractionService SHALL NOT create a ReviewBatch and SHALL
/// classify the result as a non-transient failure.
///
/// **Validates: Requirements 5.5, 7.6**
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 10: Invalid AI Responses Are Treated as Failures")]
public class InvalidAiResponsesPropertyTests
{
    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 10: Invalid AI Responses Are Treated as Failures")]
    public Property Invalid_ai_response_produces_non_transient_failure_and_no_batch()
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
                    reviewBatchRepo,
                    reviewProposalRepo,
                    sourceReferenceRepo,
                    aiUsageRecordRepo,
                    artifactRepo,
                    artifactFactRepo,
                    fakeAiClient,
                    unitOfWork,
                    options,
                    logger);

                // Seed source in Queued status with non-empty body
                sourceRepo.Seed(source);

                // Configure fake AI client to always return the invalid response
                fakeAiClient.SetupSuccess(invalidResponse);

                // Act
                var outcome = service.ProcessExtractionAsync(source.Id, source.CampaignId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert
                return (outcome.Type == OutcomeType.NonTransientFailure)
                    .Label("Outcome should be NonTransientFailure for invalid AI response")
                    .And((outcome.ErrorCategory == ErrorCategories.ParseFailure)
                        .Label("Error category should be ParseFailure"))
                    .And((reviewBatchRepo.Batches.Count == 0)
                        .Label("No ReviewBatch should be created for invalid responses"))
                    .And((reviewProposalRepo.Proposals.Count == 0)
                        .Label("No ReviewProposals should be created for invalid responses"))
                    .And((fakeAiClient.CallCount == 1 + options.Value.MaxParseRetryAttempts)
                        .Label($"AI should be called exactly {1 + options.Value.MaxParseRetryAttempts} times (initial + retries)"));
            });
    }

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 10: Invalid AI Responses Are Treated as Failures")]
    public Property Invalid_ai_response_transitions_source_to_failed()
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
                    reviewBatchRepo,
                    reviewProposalRepo,
                    sourceReferenceRepo,
                    aiUsageRecordRepo,
                    artifactRepo,
                    artifactFactRepo,
                    fakeAiClient,
                    unitOfWork,
                    options,
                    logger);

                // Seed source
                sourceRepo.Seed(source);

                // Configure fake AI client to always return the invalid response
                fakeAiClient.SetupSuccess(invalidResponse);

                // Act
                service.ProcessExtractionAsync(source.Id, source.CampaignId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert
                var updatedSource = sourceRepo.Sources.First(s => s.Id == source.Id);

                return (updatedSource.ProcessingStatus == SourceProcessingStatus.Failed)
                    .Label("Source should be transitioned to Failed status after invalid AI response");
            });
    }
}
