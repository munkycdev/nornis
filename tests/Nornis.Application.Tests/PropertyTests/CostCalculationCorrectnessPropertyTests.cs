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
/// Property 12: Cost Calculation Correctness
///
/// For any AI response with InputTokens and OutputTokens and a configured ModelPricing
/// with InputPerMillionTokensUsd and OutputPerMillionTokensUsd, the EstimatedCostUsd
/// SHALL equal (InputTokens × InputPerMillionTokensUsd / 1,000,000) + (OutputTokens × OutputPerMillionTokensUsd / 1,000,000).
///
/// **Validates: Requirements 6.2**
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 12: Cost Calculation Correctness")]
public class CostCalculationCorrectnessPropertyTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(CostCalculationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 12: Cost Calculation Correctness")]
    public void Estimated_cost_equals_input_times_input_rate_plus_output_times_output_rate_divided_by_million(
        CostScenario scenario)
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

        var modelName = "gpt-4o";

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
                    InputPerMillionTokensUsd = scenario.InputRate,
                    OutputPerMillionTokensUsd = scenario.OutputRate
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

        // Create a queued source with non-empty body
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = Guid.NewGuid(),
            Type = SourceType.SessionNote,
            Title = "Cost test session",
            Body = "We questioned Captain Voss in Black Harbor.",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = Guid.NewGuid(),
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Queued
        };
        sourceRepo.Seed(source);

        // Configure AI response with specific token counts
        var aiResponse = new AiExtractionResponse
        {
            Proposals =
            [
                new ExtractionProposal
                {
                    ChangeType = "CreateArtifact",
                    TargetType = "Artifact",
                    ProposedValue = new Dictionary<string, object>
                    {
                        ["name"] = "Captain Voss",
                        ["visibility"] = "PartyVisible"
                    },
                    Rationale = "Mentioned in source",
                    Confidence = 0.8m
                }
            ],
            InputTokens = scenario.InputTokens,
            OutputTokens = scenario.OutputTokens,
            TotalTokens = scenario.InputTokens + scenario.OutputTokens,
            DurationMs = 500,
            Model = modelName
        };
        fakeAiClient.SetupSuccess(aiResponse);

        // Act
        var outcome = service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert — verify cost calculation in the persisted AiUsageRecord
        Assert.That(aiUsageRecordRepo.Records, Has.Count.EqualTo(1),
            "Exactly one AiUsageRecord should be created.");

        var record = aiUsageRecordRepo.Records[0];

        var expectedCost =
            (scenario.InputTokens * scenario.InputRate / 1_000_000m) +
            (scenario.OutputTokens * scenario.OutputRate / 1_000_000m);

        Assert.That(record.EstimatedCostUsd, Is.EqualTo(expectedCost),
            $"EstimatedCostUsd should equal ({scenario.InputTokens} × {scenario.InputRate} / 1,000,000) + " +
            $"({scenario.OutputTokens} × {scenario.OutputRate} / 1,000,000) = {expectedCost}, " +
            $"but got {record.EstimatedCostUsd}.");
    }
}

/// <summary>
/// Represents a cost calculation test scenario with random token counts and pricing rates.
/// </summary>
public record CostScenario(int InputTokens, int OutputTokens, decimal InputRate, decimal OutputRate);

/// <summary>
/// FsCheck Arbitrary for cost calculation property tests.
/// Generates random (inputTokens, outputTokens, inputRate, outputRate) tuples.
/// </summary>
public class CostCalculationArbitraries
{
    public static Arbitrary<CostScenario> CostScenarios()
    {
        var gen =
            from inputTokens in Gen.Choose(0, 100_000)
            from outputTokens in Gen.Choose(0, 50_000)
            from inputRateInt in Gen.Choose(1, 5000)
            from outputRateInt in Gen.Choose(1, 20000)
            let inputRate = (decimal)inputRateInt / 100m
            let outputRate = (decimal)outputRateInt / 100m
            select new CostScenario(inputTokens, outputTokens, inputRate, outputRate);

        return gen.ToArbitrary();
    }
}
