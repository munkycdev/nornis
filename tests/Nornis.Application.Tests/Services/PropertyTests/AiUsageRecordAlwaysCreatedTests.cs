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
/// Property 10: AiUsageRecord Always Created
///
/// For any Ask invocation that reaches the AI call step (valid question, retrieval succeeds),
/// an AiUsageRecord SHALL be created with OperationType=AskLoremaster, the correct WorldId,
/// UserId, Model, InputTokens, OutputTokens, TotalTokens, EstimatedCostUsd, DurationMs,
/// and Succeeded flag — regardless of whether the AI call succeeds or fails.
///
/// **Validates: Requirements 9.1, 9.2, 9.3**
/// </summary>
[TestFixture]
[Category("Feature: ask-loremaster, Property 10: AiUsageRecord Always Created")]
public class AiUsageRecordAlwaysCreatedTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(AiUsageRecordScenarioArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 10: AiUsageRecord Always Created")]
    public void AskAsync_WhenAiCallReached_AlwaysCreatesAiUsageRecord(
        AiUsageRecordScenario scenario)
    {
        // Arrange
        var knowledgeRetriever = new FakeKnowledgeRetriever();
        knowledgeRetriever.NextContext = scenario.KnowledgeContext;

        var aiClient = new FakeLoremasterAiClient();
        if (scenario.AiSucceeds)
        {
            aiClient.SetupSuccess(scenario.AiResponse);
        }
        else
        {
            switch (scenario.FailureType)
            {
                case AiFailureType.Timeout:
                    aiClient.SetupTimeout();
                    break;
                case AiFailureType.RateLimited:
                    aiClient.SetupRateLimited();
                    break;
                case AiFailureType.ServiceError:
                    aiClient.SetupServiceError();
                    break;
            }
        }

        var usageRepo = new InMemoryAiUsageRecordRepository();
        var options = Options.Create(new LoremasterOptions
        {
            MaxQuestionLength = 2000,
            AiModel = "gpt-4o",
            AiTimeoutSeconds = 30,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 10.00m
                }
            }
        });

        var service = new LoremasterService(
            knowledgeRetriever, new FakeReferencePassageRetriever(),
            aiClient,
            usageRepo,
            new FakeAiBudgetGuard(), options);

        var command = new AskLoremasterCommand(
            WorldId: scenario.WorldId,
            Question: scenario.Question,
            UserId: scenario.UserId,
            UserRole: scenario.UserRole,
            ConversationContext: null);

        // Act
        _ = service.AskAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — exactly one AiUsageRecord was created
        Assert.That(usageRepo.Records.Count, Is.EqualTo(1),
            $"Expected exactly 1 AiUsageRecord (AiSucceeds={scenario.AiSucceeds}, " +
            $"FailureType={scenario.FailureType}) but found {usageRepo.Records.Count}.");

        var record = usageRepo.Records[0];

        // Assert — OperationType is AskLoremaster
        Assert.That(record.OperationType, Is.EqualTo(AiOperationType.AskLoremaster),
            "AiUsageRecord must have OperationType=AskLoremaster.");

        // Assert — correct WorldId
        Assert.That(record.WorldId, Is.EqualTo(scenario.WorldId),
            "AiUsageRecord must have the correct WorldId.");

        // Assert — correct UserId
        Assert.That(record.UserId, Is.EqualTo(scenario.UserId),
            "AiUsageRecord must have the correct UserId.");

        // Assert — Succeeded flag matches scenario
        Assert.That(record.Succeeded, Is.EqualTo(scenario.AiSucceeds),
            $"AiUsageRecord.Succeeded should be {scenario.AiSucceeds}.");

        if (scenario.AiSucceeds)
        {
            // Assert — token counts match the AI response
            Assert.That(record.InputTokens, Is.EqualTo(scenario.AiResponse.InputTokens),
                "AiUsageRecord.InputTokens must match AI response.");
            Assert.That(record.OutputTokens, Is.EqualTo(scenario.AiResponse.OutputTokens),
                "AiUsageRecord.OutputTokens must match AI response.");
            Assert.That(record.TotalTokens, Is.EqualTo(scenario.AiResponse.TotalTokens),
                "AiUsageRecord.TotalTokens must match AI response.");
            Assert.That(record.Model, Is.EqualTo(scenario.AiResponse.Model),
                "AiUsageRecord.Model must match AI response.");

            // Assert — DurationMs is non-negative
            Assert.That(record.DurationMs, Is.GreaterThanOrEqualTo(0),
                "AiUsageRecord.DurationMs must be non-negative on success.");

            // Assert — EstimatedCostUsd is non-negative
            Assert.That(record.EstimatedCostUsd, Is.GreaterThanOrEqualTo(0m),
                "AiUsageRecord.EstimatedCostUsd must be non-negative on success.");
        }
        else
        {
            // Assert — on failure, token counts default to 0 (no response available)
            Assert.That(record.InputTokens, Is.EqualTo(0),
                "AiUsageRecord.InputTokens should be 0 on failure (no response).");
            Assert.That(record.OutputTokens, Is.EqualTo(0),
                "AiUsageRecord.OutputTokens should be 0 on failure (no response).");
            Assert.That(record.TotalTokens, Is.EqualTo(0),
                "AiUsageRecord.TotalTokens should be 0 on failure (no response).");

            // Assert — DurationMs is non-negative even on failure
            Assert.That(record.DurationMs, Is.GreaterThanOrEqualTo(0),
                "AiUsageRecord.DurationMs must be non-negative on failure.");

            // Assert — ErrorCode is set on failure
            Assert.That(record.ErrorCode, Is.Not.Null.And.Not.Empty,
                "AiUsageRecord.ErrorCode must be set on failure.");
        }
    }
}

/// <summary>
/// Input model for AiUsageRecord creation scenarios.
/// </summary>
public record AiUsageRecordScenario(
    Guid WorldId,
    Guid UserId,
    WorldRole UserRole,
    string Question,
    KnowledgeContext KnowledgeContext,
    bool AiSucceeds,
    LoremasterAiResponse AiResponse,
    AiFailureType FailureType);

/// <summary>
/// Types of AI failures to simulate.
/// </summary>
public enum AiFailureType
{
    None,
    Timeout,
    RateLimited,
    ServiceError
}

/// <summary>
/// Custom FsCheck arbitraries for AiUsageRecord creation property tests.
/// Generates valid questions with knowledge contexts, and configures the AI client
/// to either succeed or fail in various ways.
/// </summary>
public class AiUsageRecordScenarioArbitraries
{
    private static readonly string[] QuestionTemplates =
    [
        "What do we know about Captain Voss?",
        "Where is the Silver Key?",
        "Tell me about Black Harbor.",
        "Who is involved in the Missing Caravan?",
        "What happened at Iron Gate?",
        "What is Tavrin's relationship with Kelda?",
        "Are there any rumors about the Shadow Cult?",
        "What is the current state of the investigation?"
    ];

    private static readonly string[] ArtifactNames =
    [
        "Captain Voss", "Black Harbor", "Silver Key", "Missing Caravan",
        "Tavrin", "Kelda", "Iron Gate", "Shadow Cult"
    ];

    private static readonly string[] Predicates =
    [
        "location", "occupation", "allegiance", "status", "current owner"
    ];

    private static readonly string[] Values =
    [
        "Black Harbor", "Captain", "Shadow Cult", "missing", "Tavrin"
    ];

    private static readonly string[] RelationshipTypes =
    [
        "LocatedIn", "SuspectedIn", "AlliedWith", "OwnerOf", "EnemyOf"
    ];

    public static Arbitrary<AiUsageRecordScenario> AiUsageRecordScenarios()
    {
        var roleGen = Gen.Elements(
            WorldRole.GM,
            WorldRole.Player,
            WorldRole.Observer);

        var questionGen = Gen.Elements(QuestionTemplates);

        var failureTypeGen = Gen.Elements(
            AiFailureType.Timeout,
            AiFailureType.RateLimited,
            AiFailureType.ServiceError);

        // Generate a response that the AI would return on success
        var aiResponseGen =
            from inputTokens in Gen.Choose(50, 2000)
            from outputTokens in Gen.Choose(20, 500)
            from durationMs in Gen.Choose(100, 5000)
            select new LoremasterAiResponse
            {
                AnswerText = "Captain Voss is known to frequent Black Harbor [ref:art-123].",
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                DurationMs = durationMs,
                Model = "gpt-4o"
            };

        // Generate a knowledge context with at least one artifact
        var contextGen =
            from artifactCount in Gen.Choose(1, 4)
            from factCount in Gen.Choose(0, 5)
            from relCount in Gen.Choose(0, 3)
            from srcRefCount in Gen.Choose(0, 3)
            from artifacts in GenArtifacts(artifactCount)
            from facts in GenFacts(factCount)
            from relationships in GenRelationships(relCount)
            from sourceRefs in GenSourceReferences(srcRefCount)
            select new KnowledgeContext
            {
                Artifacts = artifacts,
                Facts = facts,
                Relationships = relationships,
                SourceReferences = sourceRefs
            };

        // Success scenario
        var successGen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from role in roleGen
            from question in questionGen
            from context in contextGen
            from aiResponse in aiResponseGen
            select new AiUsageRecordScenario(
                worldId, userId, role, question, context,
                AiSucceeds: true,
                AiResponse: aiResponse,
                FailureType: AiFailureType.None);

        // Failure scenario
        var failureGen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from role in roleGen
            from question in questionGen
            from context in contextGen
            from aiResponse in aiResponseGen
            from failureType in failureTypeGen
            select new AiUsageRecordScenario(
                worldId, userId, role, question, context,
                AiSucceeds: false,
                AiResponse: aiResponse,
                FailureType: failureType);

        // Mix of success and failure scenarios with balanced weighting
        var gen = Gen.Frequency(
            (1, successGen),
            (1, failureGen));

        return gen.ToArbitrary();
    }

    private static Gen<List<KnowledgeArtifact>> GenArtifacts(int count)
    {
        var singleGen =
            from nameIndex in Gen.Choose(0, ArtifactNames.Length - 1)
            from id in ArbMap.Default.GeneratorFor<Guid>()
            select new KnowledgeArtifact
            {
                Id = id,
                Name = ArtifactNames[nameIndex],
                Type = "Character",
                Summary = $"Summary of {ArtifactNames[nameIndex]}",
                ReferenceId = $"art-{id:N}"
            };

        return singleGen.ListOf(count).Select(items => items.ToList());
    }

    private static Gen<List<KnowledgeFact>> GenFacts(int count)
    {
        if (count == 0)
            return Gen.Constant(new List<KnowledgeFact>());

        var truthStateGen = Gen.Elements(
            TruthState.Confirmed, TruthState.Likely, TruthState.Rumor,
            TruthState.Disputed, TruthState.False, TruthState.Hidden);

        var singleGen =
            from id in ArbMap.Default.GeneratorFor<Guid>()
            from artifactId in ArbMap.Default.GeneratorFor<Guid>()
            from predIndex in Gen.Choose(0, Predicates.Length - 1)
            from valIndex in Gen.Choose(0, Values.Length - 1)
            from truthState in truthStateGen
            select new KnowledgeFact
            {
                Id = id,
                ArtifactId = artifactId,
                Predicate = Predicates[predIndex],
                Value = Values[valIndex],
                TruthState = truthState,
                ReferenceId = $"fact-{id:N}"
            };

        return singleGen.ListOf(count).Select(items => items.ToList());
    }

    private static Gen<List<KnowledgeRelationship>> GenRelationships(int count)
    {
        if (count == 0)
            return Gen.Constant(new List<KnowledgeRelationship>());

        var truthStateGen = Gen.Elements(
            TruthState.Confirmed, TruthState.Likely, TruthState.Rumor);

        var singleGen =
            from id in ArbMap.Default.GeneratorFor<Guid>()
            from artifactAId in ArbMap.Default.GeneratorFor<Guid>()
            from artifactBId in ArbMap.Default.GeneratorFor<Guid>()
            from typeIndex in Gen.Choose(0, RelationshipTypes.Length - 1)
            from truthState in truthStateGen
            select new KnowledgeRelationship
            {
                Id = id,
                ArtifactAId = artifactAId,
                ArtifactBId = artifactBId,
                Type = RelationshipTypes[typeIndex],
                Description = $"Relationship: {RelationshipTypes[typeIndex]}",
                TruthState = truthState,
                ReferenceId = $"rel-{id:N}"
            };

        return singleGen.ListOf(count).Select(items => items.ToList());
    }

    private static Gen<List<KnowledgeSourceReference>> GenSourceReferences(int count)
    {
        if (count == 0)
            return Gen.Constant(new List<KnowledgeSourceReference>());

        var singleGen =
            from id in ArbMap.Default.GeneratorFor<Guid>()
            from sourceId in ArbMap.Default.GeneratorFor<Guid>()
            from targetId in ArbMap.Default.GeneratorFor<Guid>()
            select new KnowledgeSourceReference
            {
                Id = id,
                SourceId = sourceId,
                TargetId = targetId,
                Quote = "Relevant quote from source material",
                ReferenceId = $"src-{id:N}"
            };

        return singleGen.ListOf(count).Select(items => items.ToList());
    }
}
