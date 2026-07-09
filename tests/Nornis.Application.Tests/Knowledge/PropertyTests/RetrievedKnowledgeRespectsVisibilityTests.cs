using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Knowledge;
using NUnit.Framework;

namespace Nornis.Application.Tests.Knowledge.PropertyTests;

/// <summary>
/// Property 4: Retrieved Knowledge Respects Visibility
///
/// For any set of artifacts retrieved by the knowledge retriever, all associated ArtifactFacts
/// and ArtifactRelationships loaded into the knowledge context SHALL have a visibility scope
/// permitted for the requesting user's role. No fact or relationship with a disallowed visibility
/// SHALL appear in the context.
///
/// **Validates: Requirements 4.2, 4.3**
/// </summary>
[TestFixture]
[Category("Feature: ask-loremaster, Property 4: Retrieved Knowledge Respects Visibility")]
public class RetrievedKnowledgeRespectsVisibilityTests
{
    private static readonly LoremasterOptions DefaultOptions = new()
    {
        MaxRetrievalCount = 30,
        MaxFactsPerArtifact = 15
    };

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(RetrievedKnowledgeVisibilityArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 4: Retrieved Knowledge Respects Visibility")]
    public void RetrievedFacts_And_Relationships_NeverContainDisallowedVisibility(
        RetrievedKnowledgeVisibilityScenario scenario)
    {
        // Arrange
        var artifactRepo = new InMemoryArtifactRepository();
        var factRepo = new InMemoryArtifactFactRepository();
        var relationshipRepo = new InMemoryArtifactRelationshipRepository();
        var sourceRefRepo = new InMemorySourceReferenceRepository();
        var options = Options.Create(DefaultOptions);

        // Seed artifacts (all PartyVisible so they are retrieved for any role)
        artifactRepo.Seed(scenario.Artifacts.ToArray());

        // Seed facts with mixed visibilities
        factRepo.Seed(scenario.Facts.ToArray());

        // Seed relationships with mixed visibilities
        relationshipRepo.Seed(scenario.Relationships.ToArray());

        var retriever = new KeywordKnowledgeRetriever(
            artifactRepo, factRepo, relationshipRepo, sourceRefRepo, options);

        // Determine allowed visibility scopes for the role
        var allowedScopes = GetAllowedScopes(scenario.Role);

        // Act — use a question that includes an artifact name so retrieval is triggered
        var result = retriever.RetrieveAsync(
            scenario.QuestionText,
            scenario.WorldId,
            scenario.UserId,
            scenario.Role,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — every fact in the result must have an allowed visibility
        foreach (var fact in result.Facts)
        {
            var originalFact = scenario.Facts.First(f => f.Id == fact.Id);
            Assert.That(allowedScopes, Does.Contain(originalFact.Visibility),
                $"Fact {fact.Id} has visibility {originalFact.Visibility} which is not allowed for role {scenario.Role}");
        }

        // Assert — every relationship in the result must have an allowed visibility
        foreach (var rel in result.Relationships)
        {
            var originalRel = scenario.Relationships.First(r => r.Id == rel.Id);
            Assert.That(allowedScopes, Does.Contain(originalRel.Visibility),
                $"Relationship {rel.Id} has visibility {originalRel.Visibility} which is not allowed for role {scenario.Role}");
        }
    }

    private static IReadOnlyList<VisibilityScope> GetAllowedScopes(WorldRole role) =>
        role switch
        {
            WorldRole.GM => [VisibilityScope.PartyVisible, VisibilityScope.GMOnly, VisibilityScope.Private],
            WorldRole.Player => [VisibilityScope.PartyVisible, VisibilityScope.Private],
            WorldRole.Observer => [VisibilityScope.PartyVisible],
            _ => [VisibilityScope.PartyVisible]
        };
}

/// <summary>
/// Input model for the retrieved knowledge visibility scenario.
/// Contains artifacts (all PartyVisible so they get retrieved), with associated facts and
/// relationships at mixed visibilities to verify filtering.
/// </summary>
public record RetrievedKnowledgeVisibilityScenario(
    Guid WorldId,
    Guid UserId,
    WorldRole Role,
    string QuestionText,
    List<Artifact> Artifacts,
    List<ArtifactFact> Facts,
    List<ArtifactRelationship> Relationships);

/// <summary>
/// Custom FsCheck arbitraries for retrieved knowledge visibility property tests.
/// Generates worlds with PartyVisible artifacts (to ensure retrieval),
/// associated facts and relationships with mixed visibilities.
/// </summary>
public class RetrievedKnowledgeVisibilityArbitraries
{
    private static readonly string[] ArtifactNames =
    [
        "Captain Voss", "Black Harbor", "Silver Key", "Missing Caravan",
        "Tavrin", "Iron Gate", "Ancient Map", "Dark Shrine"
    ];

    public static Arbitrary<RetrievedKnowledgeVisibilityScenario> RetrievedKnowledgeVisibilityScenarios()
    {
        var roleGen = Gen.Elements(
            WorldRole.GM,
            WorldRole.Player,
            WorldRole.Observer);

        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from role in roleGen
            from scenario in GenScenario(worldId, userId, role)
            select scenario;

        return gen.ToArbitrary();
    }

    private static Gen<RetrievedKnowledgeVisibilityScenario> GenScenario(
        Guid worldId, Guid userId, WorldRole role)
    {
        var visibilityGen = Gen.Elements(
            VisibilityScope.Private,
            VisibilityScope.GMOnly,
            VisibilityScope.PartyVisible);

        var truthStateGen = Gen.Elements(
            TruthState.Confirmed,
            TruthState.Likely,
            TruthState.Rumor,
            TruthState.Disputed);

        return
            from artifactCount in Gen.Choose(1, 4)
            from nameIndices in Gen.Choose(0, ArtifactNames.Length - 1).ArrayOf(artifactCount)
            let chosenNames = nameIndices.Distinct().ToArray()
            let artifacts = chosenNames.Select(idx => new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                Type = ArtifactType.Character,
                Name = ArtifactNames[idx],
                Summary = $"Summary of {ArtifactNames[idx]}",
                Visibility = VisibilityScope.PartyVisible,
                Confidence = 0.9m,
                Status = ArtifactStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                RowVersion = [1, 2, 3, 4]
            }).ToList()
            from factsPerArtifact in Gen.Choose(2, 5)
            from facts in GenFacts(artifacts, factsPerArtifact, visibilityGen, truthStateGen)
            from relationships in GenRelationships(artifacts, worldId, visibilityGen, truthStateGen)
            let questionText = $"What do we know about {artifacts[0].Name}?"
            select new RetrievedKnowledgeVisibilityScenario(
                worldId, userId, role, questionText, artifacts, facts, relationships);
    }

    private static Gen<List<ArtifactFact>> GenFacts(
        List<Artifact> artifacts,
        int factsPerArtifact,
        Gen<VisibilityScope> visibilityGen,
        Gen<TruthState> truthStateGen)
    {
        var factGens = new List<Gen<ArtifactFact>>();

        foreach (var artifact in artifacts)
        {
            for (var i = 0; i < factsPerArtifact; i++)
            {
                var predicateIndex = i;
                var factGen =
                    from visibility in visibilityGen
                    from truthState in truthStateGen
                    select new ArtifactFact
                    {
                        Id = Guid.NewGuid(),
                        ArtifactId = artifact.Id,
                        Predicate = $"predicate_{predicateIndex}",
                        Value = $"value_{predicateIndex} of {artifact.Name}",
                        Confidence = 0.8m,
                        TruthState = truthState,
                        Visibility = visibility,
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
                        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                        RowVersion = [1, 2, 3, 4]
                    };
                factGens.Add(factGen);
            }
        }

        if (factGens.Count == 0)
            return Gen.Constant(new List<ArtifactFact>());

        return factGens
            .Aggregate(
                Gen.Constant(new List<ArtifactFact>()),
                (acc, factGen) =>
                    from list in acc
                    from fact in factGen
                    select new List<ArtifactFact>(list) { fact });
    }

    private static Gen<List<ArtifactRelationship>> GenRelationships(
        List<Artifact> artifacts,
        Guid worldId,
        Gen<VisibilityScope> visibilityGen,
        Gen<TruthState> truthStateGen)
    {
        if (artifacts.Count < 2)
            return Gen.Constant(new List<ArtifactRelationship>());

        return
            from relCount in Gen.Choose(1, Math.Min(4, artifacts.Count))
            from relationships in GenRelationshipList(artifacts, worldId, relCount, visibilityGen, truthStateGen)
            select relationships;
    }

    private static Gen<List<ArtifactRelationship>> GenRelationshipList(
        List<Artifact> artifacts,
        Guid worldId,
        int count,
        Gen<VisibilityScope> visibilityGen,
        Gen<TruthState> truthStateGen)
    {
        var relGens = new List<Gen<ArtifactRelationship>>();

        for (var i = 0; i < count; i++)
        {
            var artifactA = artifacts[i % artifacts.Count];
            var artifactB = artifacts[(i + 1) % artifacts.Count];

            var relGen =
                from visibility in visibilityGen
                from truthState in truthStateGen
                select new ArtifactRelationship
                {
                    Id = Guid.NewGuid(),
                    WorldId = worldId,
                    ArtifactAId = artifactA.Id,
                    ArtifactBId = artifactB.Id,
                    Type = "ConnectedTo",
                    Description = $"{artifactA.Name} is connected to {artifactB.Name}",
                    Confidence = 0.85m,
                    TruthState = truthState,
                    Visibility = visibility,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    RowVersion = [1, 2, 3, 4]
                };
            relGens.Add(relGen);
        }

        return relGens
            .Aggregate(
                Gen.Constant(new List<ArtifactRelationship>()),
                (acc, relGen) =>
                    from list in acc
                    from rel in relGen
                    select new List<ArtifactRelationship>(list) { rel });
    }
}
