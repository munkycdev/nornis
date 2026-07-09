using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 6: Prompt Contains Question and Context
///
/// For any valid question and non-empty knowledge context, the prompt sent to the AI client
/// SHALL contain the original question text and at least one artifact name from the retrieved context.
///
/// **Validates: Requirements 5.1**
/// </summary>
[TestFixture]
[Category("Feature: ask-loremaster, Property 6: Prompt Contains Question and Context")]
public class PromptContainsQuestionAndContextTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(PromptContainsQuestionAndContextArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 6: Prompt Contains Question and Context")]
    public void AskAsync_ValidQuestionWithNonEmptyContext_PromptContainsQuestionAndArtifactName(
        PromptContainsQuestionAndContextScenario scenario)
    {
        // Arrange
        var knowledgeRetriever = new FakeKnowledgeRetriever
        {
            NextContext = scenario.Context
        };
        var aiClient = new FakeLoremasterAiClient();
        aiClient.SetupSuccess("The Loremaster knows this answer.");
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
            knowledgeRetriever,
            aiClient,
            usageRepo,
            new FakeAiBudgetGuard(), options);

        var command = new AskLoremasterCommand(
            WorldId: scenario.WorldId,
            Question: scenario.Question,
            UserId: scenario.UserId,
            UserRole: WorldRole.GM,
            ConversationContext: null);

        // Act
        var result = service.AskAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — call succeeded and AI client was invoked
        Assert.That(result.IsSuccess, Is.True,
            $"Expected success but got error: {result.Error?.Message}");
        Assert.That(aiClient.CallCount, Is.EqualTo(1),
            "AI client should have been called exactly once.");

        var capturedRequest = aiClient.LastRequest!;

        // Assert — prompt contains the original question text
        Assert.That(capturedRequest.UserMessage, Does.Contain(scenario.Question),
            "The UserMessage sent to the AI client must contain the original question text.");

        // Assert — prompt contains at least one artifact name from the knowledge context
        var artifactNames = scenario.Context.Artifacts.Select(a => a.Name).ToList();
        var containsAtLeastOneArtifactName = artifactNames.Any(name =>
            capturedRequest.UserMessage.Contains(name) || capturedRequest.SystemPrompt.Contains(name));

        Assert.That(containsAtLeastOneArtifactName, Is.True,
            $"The prompt must contain at least one artifact name from the context. " +
            $"Artifact names: [{string.Join(", ", artifactNames)}]");
    }
}

/// <summary>
/// Input model for prompt contains question and context scenarios.
/// Contains a valid question and a non-empty knowledge context with at least one artifact.
/// </summary>
public record PromptContainsQuestionAndContextScenario(
    Guid WorldId,
    Guid UserId,
    string Question,
    KnowledgeContext Context);

/// <summary>
/// Custom FsCheck arbitraries for generating valid questions with non-empty knowledge contexts.
/// </summary>
public class PromptContainsQuestionAndContextArbitraries
{
    private static readonly string[] QuestionTemplates =
    [
        "What do we know about {0}?",
        "Tell me about {0}.",
        "Where is {0} located?",
        "Who is {0}?",
        "What happened at {0}?",
        "Is {0} trustworthy?",
        "What are the connections of {0}?",
        "Has {0} been seen recently?",
        "What does the party know about {0}?",
        "Give me details on {0}."
    ];

    private static readonly string[] ArtifactNames =
    [
        "Captain Voss", "Black Harbor", "Silver Key", "Missing Caravan",
        "Tavrin", "Kelda", "Jorin", "Shadow Cult", "Iron Gate",
        "Crystal Tower", "Storm Keep", "Ember Crown", "Dark Hollow",
        "Serpent Isle", "Elder Tree", "Grimjaw"
    ];

    private static readonly string[] ArtifactTypes =
    [
        "Character", "Location", "Item", "Faction", "Event", "Storyline"
    ];

    private static readonly string[] Predicates =
    [
        "allegiance", "location", "possession", "status", "motive",
        "identity", "origin", "weakness", "goal", "connection"
    ];

    private static readonly string[] Values =
    [
        "Black Harbor", "Captain Voss", "Silver Key", "Missing Caravan",
        "Shadow Cult", "Iron Gate", "Crystal Tower", "Tavrin",
        "Storm Keep", "Ember Crown", "Dark Hollow", "Serpent Isle"
    ];

    private static readonly string[] RelationshipTypes =
    [
        "AlliedWith", "EnemyOf", "LocatedIn", "SuspectedIn",
        "Possesses", "Betrayed", "Protects", "Seeks"
    ];

    public static Arbitrary<PromptContainsQuestionAndContextScenario> PromptContainsQuestionAndContextScenarios()
    {
        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from artifactCount in Gen.Choose(1, 5)
            from artifacts in GenArtifacts(artifactCount)
            from factCount in Gen.Choose(0, 4)
            from facts in GenFacts(artifacts, factCount)
            from relCount in Gen.Choose(0, 3)
            from relationships in GenRelationships(artifacts, relCount)
            from question in GenQuestion()
            let context = new KnowledgeContext
            {
                Artifacts = artifacts,
                Facts = facts,
                Relationships = relationships,
                SourceReferences = []
            }
            select new PromptContainsQuestionAndContextScenario(
                worldId,
                userId,
                question,
                context);

        return gen.ToArbitrary();
    }

    private static Gen<string> GenQuestion()
    {
        // Generate valid questions: non-empty, non-whitespace, ≤2000 characters
        var templateBasedGen =
            from templateIdx in Gen.Choose(0, QuestionTemplates.Length - 1)
            from nameIdx in Gen.Choose(0, ArtifactNames.Length - 1)
            select string.Format(QuestionTemplates[templateIdx], ArtifactNames[nameIdx]);

        var freeformGen =
            from length in Gen.Choose(5, 200)
            from chars in Gen.Elements(
                'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
                'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't',
                'u', 'v', 'w', 'x', 'y', 'z', ' ', '?')
                .ArrayOf(length)
            select new string(chars).Trim() + "?";

        return Gen.Frequency(
            (3, templateBasedGen),
            (1, freeformGen));
    }

    private static Gen<List<KnowledgeArtifact>> GenArtifacts(int count)
    {
        var singleGen =
            from nameIdx in Gen.Choose(0, ArtifactNames.Length - 1)
            from typeIdx in Gen.Choose(0, ArtifactTypes.Length - 1)
            select new { Name = ArtifactNames[nameIdx], Type = ArtifactTypes[typeIdx] };

        return singleGen.ListOf(count).Select(items =>
        {
            var usedNames = new HashSet<string>();
            var artifacts = new List<KnowledgeArtifact>();
            var suffix = 1;

            foreach (var item in items)
            {
                var name = item.Name;
                while (!usedNames.Add(name))
                {
                    name = $"{item.Name} {suffix++}";
                }

                artifacts.Add(new KnowledgeArtifact
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Type = item.Type,
                    Summary = $"Summary of {name}",
                    ReferenceId = $"art-{Guid.NewGuid():N}"
                });
            }

            return artifacts;
        });
    }

    private static Gen<List<KnowledgeFact>> GenFacts(List<KnowledgeArtifact> artifacts, int count)
    {
        if (count == 0 || artifacts.Count == 0)
            return Gen.Constant(new List<KnowledgeFact>());

        var truthStates = new[] { TruthState.Confirmed, TruthState.Likely, TruthState.Rumor, TruthState.Disputed };

        var singleGen =
            from artifactIdx in Gen.Choose(0, artifacts.Count - 1)
            from predIdx in Gen.Choose(0, Predicates.Length - 1)
            from valIdx in Gen.Choose(0, Values.Length - 1)
            from tsIdx in Gen.Choose(0, truthStates.Length - 1)
            select new KnowledgeFact
            {
                Id = Guid.NewGuid(),
                ArtifactId = artifacts[artifactIdx].Id,
                Predicate = Predicates[predIdx],
                Value = Values[valIdx],
                TruthState = truthStates[tsIdx],
                ReferenceId = $"fact-{Guid.NewGuid():N}"
            };

        return singleGen.ListOf(count).Select(items => items.ToList());
    }

    private static Gen<List<KnowledgeRelationship>> GenRelationships(
        List<KnowledgeArtifact> artifacts, int count)
    {
        if (count == 0 || artifacts.Count < 2)
            return Gen.Constant(new List<KnowledgeRelationship>());

        var truthStates = new[] { TruthState.Confirmed, TruthState.Likely, TruthState.Rumor, TruthState.Disputed };

        var singleGen =
            from aIdx in Gen.Choose(0, artifacts.Count - 1)
            from bIdx in Gen.Choose(0, artifacts.Count - 1)
            from typeIdx in Gen.Choose(0, RelationshipTypes.Length - 1)
            from tsIdx in Gen.Choose(0, truthStates.Length - 1)
            where aIdx != bIdx
            select new KnowledgeRelationship
            {
                Id = Guid.NewGuid(),
                ArtifactAId = artifacts[aIdx].Id,
                ArtifactBId = artifacts[bIdx].Id,
                Type = RelationshipTypes[typeIdx],
                Description = $"{artifacts[aIdx].Name} {RelationshipTypes[typeIdx]} {artifacts[bIdx].Name}",
                TruthState = truthStates[tsIdx],
                ReferenceId = $"rel-{Guid.NewGuid():N}"
            };

        return singleGen.ListOf(count).Select(items => items.ToList());
    }
}
