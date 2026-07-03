using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 11: Cost Calculation Correctness
///
/// For any AI response with InputTokens and OutputTokens and a configured ModelPricing,
/// the EstimatedCostUsd SHALL equal
/// (InputTokens × InputPerMillionTokensUsd / 1,000,000) + (OutputTokens × OutputPerMillionTokensUsd / 1,000,000).
///
/// **Validates: Requirements 9.4**
/// </summary>
[TestFixture]
[Category("Feature: ask-loremaster, Property 11: Cost Calculation Correctness")]
public class CostCalculationCorrectnessTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(CostCalculationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 11: Cost Calculation Correctness")]
    public void AskAsync_WithConfiguredPricing_EstimatedCostMatchesFormula(
        CostCalculationScenario scenario)
    {
        // Arrange
        var knowledgeRetriever = new FakeKnowledgeRetriever
        {
            NextContext = scenario.Context
        };
        var aiClient = new FakeLoremasterAiClient();
        aiClient.SetupSuccess(scenario.AiResponse);

        var usageRepo = new InMemoryAiUsageRecordRepository();
        var options = Options.Create(new LoremasterOptions
        {
            MaxQuestionLength = 2000,
            AiModel = scenario.ModelName,
            AiTimeoutSeconds = 30,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                [scenario.ModelName] = new ModelPricing
                {
                    InputPerMillionTokensUsd = scenario.InputPerMillionTokensUsd,
                    OutputPerMillionTokensUsd = scenario.OutputPerMillionTokensUsd
                }
            }
        });

        var service = new LoremasterService(
            knowledgeRetriever,
            aiClient,
            usageRepo,
            options);

        var command = new AskLoremasterCommand(
            CampaignId: scenario.CampaignId,
            Question: scenario.Question,
            UserId: scenario.UserId,
            UserRole: CampaignRole.GM,
            ConversationContext: null);

        // Act
        var result = service.AskAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — call succeeded
        Assert.That(result.IsSuccess, Is.True,
            $"Expected success but got error: {result.Error?.Message}");

        // Assert — exactly one usage record created
        Assert.That(usageRepo.Records.Count, Is.EqualTo(1),
            "Exactly one AiUsageRecord should be created.");

        var record = usageRepo.Records[0];

        // Calculate expected cost using the formula
        var expectedCost =
            (scenario.AiResponse.InputTokens * scenario.InputPerMillionTokensUsd / 1_000_000m)
            + (scenario.AiResponse.OutputTokens * scenario.OutputPerMillionTokensUsd / 1_000_000m);

        // Assert — EstimatedCostUsd matches formula exactly
        Assert.That(record.EstimatedCostUsd, Is.EqualTo(expectedCost),
            $"EstimatedCostUsd should equal (InputTokens={scenario.AiResponse.InputTokens} × " +
            $"InputRate={scenario.InputPerMillionTokensUsd} / 1,000,000) + " +
            $"(OutputTokens={scenario.AiResponse.OutputTokens} × " +
            $"OutputRate={scenario.OutputPerMillionTokensUsd} / 1,000,000) = {expectedCost}, " +
            $"but got {record.EstimatedCostUsd}.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(CostCalculationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 11: Cost Calculation Correctness — cost is non-negative")]
    public void AskAsync_WithConfiguredPricing_EstimatedCostIsNonNegative(
        CostCalculationScenario scenario)
    {
        // Arrange
        var knowledgeRetriever = new FakeKnowledgeRetriever
        {
            NextContext = scenario.Context
        };
        var aiClient = new FakeLoremasterAiClient();
        aiClient.SetupSuccess(scenario.AiResponse);

        var usageRepo = new InMemoryAiUsageRecordRepository();
        var options = Options.Create(new LoremasterOptions
        {
            MaxQuestionLength = 2000,
            AiModel = scenario.ModelName,
            AiTimeoutSeconds = 30,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                [scenario.ModelName] = new ModelPricing
                {
                    InputPerMillionTokensUsd = scenario.InputPerMillionTokensUsd,
                    OutputPerMillionTokensUsd = scenario.OutputPerMillionTokensUsd
                }
            }
        });

        var service = new LoremasterService(
            knowledgeRetriever,
            aiClient,
            usageRepo,
            options);

        var command = new AskLoremasterCommand(
            CampaignId: scenario.CampaignId,
            Question: scenario.Question,
            UserId: scenario.UserId,
            UserRole: CampaignRole.Player,
            ConversationContext: null);

        // Act
        var result = service.AskAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — call succeeded
        Assert.That(result.IsSuccess, Is.True,
            $"Expected success but got error: {result.Error?.Message}");

        // Assert — cost is non-negative
        var record = usageRepo.Records[0];
        Assert.That(record.EstimatedCostUsd, Is.GreaterThanOrEqualTo(0m),
            "EstimatedCostUsd must always be non-negative.");
    }
}

/// <summary>
/// Input model for cost calculation scenarios.
/// Contains a valid question, non-empty knowledge context, AI response with token counts,
/// and configured model pricing rates.
/// </summary>
public record CostCalculationScenario(
    Guid CampaignId,
    Guid UserId,
    string Question,
    string ModelName,
    decimal InputPerMillionTokensUsd,
    decimal OutputPerMillionTokensUsd,
    LoremasterAiResponse AiResponse,
    KnowledgeContext Context);

/// <summary>
/// Custom FsCheck arbitraries for generating cost calculation scenarios with varied
/// token counts and pricing configurations.
/// </summary>
public class CostCalculationArbitraries
{
    private static readonly string[] ModelNames =
    [
        "gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-3.5-turbo"
    ];

    public static Arbitrary<CostCalculationScenario> CostCalculationScenarios()
    {
        var gen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from modelIdx in Gen.Choose(0, ModelNames.Length - 1)
            let modelName = ModelNames[modelIdx]
            from inputTokens in Gen.Choose(1, 100_000)
            from outputTokens in Gen.Choose(1, 50_000)
            from inputRateCents in Gen.Choose(1, 5000) // 0.01 to 50.00 USD per million
            from outputRateCents in Gen.Choose(1, 10000) // 0.01 to 100.00 USD per million
            let inputRate = inputRateCents / 100m
            let outputRate = outputRateCents / 100m
            let aiResponse = new LoremasterAiResponse
            {
                AnswerText = "The Loremaster provides this answer based on campaign knowledge.",
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                DurationMs = 200 + (outputTokens / 10),
                Model = modelName
            }
            let context = new KnowledgeContext
            {
                Artifacts = new List<KnowledgeArtifact>
                {
                    new KnowledgeArtifact
                    {
                        Id = Guid.NewGuid(),
                        Name = "Captain Voss",
                        Type = "Character",
                        Summary = "A suspicious harbor captain",
                        ReferenceId = $"art-{Guid.NewGuid():N}"
                    }
                },
                Facts = new List<KnowledgeFact>(),
                Relationships = new List<KnowledgeRelationship>(),
                SourceReferences = new List<KnowledgeSourceReference>()
            }
            select new CostCalculationScenario(
                campaignId,
                userId,
                "What do we know about Captain Voss?",
                modelName,
                inputRate,
                outputRate,
                aiResponse,
                context);

        return gen.ToArbitrary();
    }
}
