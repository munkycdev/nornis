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

namespace Nornis.Application.Tests.PropertyTests;

/// <summary>
/// Property 5: Empty Body Short-Circuits to Completed Batch
///
/// For any source in Queued status whose Body is null, empty, or composed entirely
/// of whitespace characters, the ExtractionService SHALL skip AI invocation, create
/// a ReviewBatch with Status=Completed and zero ReviewProposal records, and transition
/// the source ProcessingStatus to Processed.
///
/// **Validates: Requirements 3.2**
/// </summary>
[TestFixture]
public class EmptyBodyShortCircuitsPropertyTests
{
    [FsCheck.NUnit.Property]
    public Property Empty_body_source_produces_success_outcome_with_no_ai_call()
    {
        return Prop.ForAll(
            ExtractionGenerators.QueuedSourceWithEmptyBody.ToArbitrary(),
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
                    new FakeAiBudgetGuard(), unitOfWork,
                    options,
                    logger);

                // Seed source — DO NOT configure FakeAiExtractionClient
                // (it throws if called, proving AI was never invoked)
                sourceRepo.Seed(source);

                // Act
                var outcome = service.ProcessExtractionAsync(source.Id, source.CampaignId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert
                var updatedSource = sourceRepo.Sources.First(s => s.Id == source.Id);

                return (outcome.Type == OutcomeType.Success)
                    .Label("Outcome should be Success")
                    .And((updatedSource.ProcessingStatus == SourceProcessingStatus.Processed)
                        .Label("Source should be in Processed status"))
                    .And((reviewBatchRepo.Batches.Count == 1)
                        .Label("Exactly one ReviewBatch should be created"))
                    .And((reviewBatchRepo.Batches[0].Status == ReviewBatchStatus.Completed)
                        .Label("ReviewBatch should have Status=Completed"))
                    .And((reviewProposalRepo.Proposals.Count == 0)
                        .Label("Zero ReviewProposals should be created"))
                    .And((fakeAiClient.CallCount == 0)
                        .Label("No AI call should be made"));
            });
    }
}
