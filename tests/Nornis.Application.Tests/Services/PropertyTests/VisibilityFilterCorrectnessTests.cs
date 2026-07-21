using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Infrastructure.Knowledge;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 2: Visibility Filter Correctness
///
/// For any world role (GM, Player, Observer), requesting user ID, and any set of knowledge items
/// with mixed visibility scopes and owners, the visibility filter SHALL return exactly those items
/// permitted for that role: GMs see PartyVisible + GMOnly + own Private; Players see PartyVisible +
/// own Private; Observers see only PartyVisible. Private items owned by a different user SHALL never
/// be included regardless of role.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
/// </summary>
[TestFixture]
[Category("Feature: ask-loremaster, Property 2: Visibility Filter Correctness")]
public class VisibilityFilterCorrectnessTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(VisibilityFilterArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 2: Visibility Filter Correctness")]
    public async Task<bool> RetrieveAsync_ReturnsOnlyVisibleArtifactsFactsAndRelationships(
        VisibilityFilterScenario scenario)
    {
        // Arrange
        var artifactRepo = new InMemoryArtifactRepository();
        var factRepo = new InMemoryArtifactFactRepository();
        var relationshipRepo = new InMemoryArtifactRelationshipRepository();
        var sourceRefRepo = new InMemorySourceReferenceRepository();

        artifactRepo.Seed(scenario.Artifacts);
        factRepo.Seed(scenario.Facts);
        relationshipRepo.Seed(scenario.Relationships);

        var options = Options.Create(new LoremasterOptions
        {
            MaxRetrievalCount = 30,
            MaxFactsPerArtifact = 15
        });

        var retriever = new KeywordKnowledgeRetriever(
            artifactRepo, factRepo, relationshipRepo, sourceRefRepo, new InMemorySourceRepository(), options);

        // Build a question string that contains all artifact names so they get name-matched
        var question = string.Join(" ", scenario.Artifacts.Select(a => a.Name));

        // Act
        var result = await retriever.RetrieveAsync(
            question,
            scenario.WorldId,
            scenario.RequestingUserId,
            scenario.RequestingRole,
            CancellationToken.None);

        // The ownership-aware policy is the expected model: GM sees everything; Player
        // sees PartyVisible plus their OWN Private; Observer sees PartyVisible only.
        var filter = VisibilityFilter.ForRole(scenario.RequestingRole, scenario.RequestingUserId);

        // Assert — Artifacts: exactly those the policy admits
        var expectedArtifactIds = scenario.Artifacts
            .Where(a => filter.CanSee(a.Visibility, a.CreatedByUserId))
            .Select(a => a.Id)
            .ToHashSet();

        var returnedArtifactIds = result.Artifacts.Select(a => a.Id).ToHashSet();

        if (!returnedArtifactIds.SetEquals(expectedArtifactIds))
            return false;

        // Assert — Facts: only policy-admitted facts on returned artifacts
        var expectedFactIds = scenario.Facts
            .Where(f => expectedArtifactIds.Contains(f.ArtifactId) &&
                        filter.CanSee(f.Visibility, f.CreatedByUserId))
            .Select(f => f.Id)
            .ToHashSet();

        var returnedFactIds = result.Facts.Select(f => f.Id).ToHashSet();

        if (!returnedFactIds.SetEquals(expectedFactIds))
            return false;

        // Assert — Relationships: only policy-admitted relationships touching returned artifacts
        var returnedRelationshipIds = result.Relationships.Select(r => r.Id).ToHashSet();

        var expectedRelationshipIds = scenario.Relationships
            .Where(r => (expectedArtifactIds.Contains(r.ArtifactAId) ||
                         expectedArtifactIds.Contains(r.ArtifactBId)) &&
                        filter.CanSee(r.Visibility, r.CreatedByUserId))
            .Select(r => r.Id)
            .ToHashSet();

        if (!returnedRelationshipIds.SetEquals(expectedRelationshipIds))
            return false;

        // Assert (independent of the policy object) — a non-GM must never receive a
        // Private item owned by a different user or with no owner. This is the leak the
        // ownership work exists to prevent; assert it directly rather than through CanSee.
        if (scenario.RequestingRole != WorldRole.GM)
        {
            bool ForeignPrivate(VisibilityScope v, Guid? owner) =>
                v == VisibilityScope.Private && owner != scenario.RequestingUserId;

            var hasForeignPrivateArtifact = result.Artifacts.Any(a =>
                scenario.Artifacts.Any(sa => sa.Id == a.Id && ForeignPrivate(sa.Visibility, sa.CreatedByUserId)));
            if (hasForeignPrivateArtifact)
                return false;

            var hasForeignPrivateFact = result.Facts.Any(f =>
                scenario.Facts.Any(sf => sf.Id == f.Id && ForeignPrivate(sf.Visibility, sf.CreatedByUserId)));
            if (hasForeignPrivateFact)
                return false;

            var hasForeignPrivateRelationship = result.Relationships.Any(r =>
                scenario.Relationships.Any(sr => sr.Id == r.Id && ForeignPrivate(sr.Visibility, sr.CreatedByUserId)));
            if (hasForeignPrivateRelationship)
                return false;
        }

        // Observers additionally see no Private at all, own or otherwise.
        if (scenario.RequestingRole == WorldRole.Observer)
        {
            var hasPrivateArtifact = result.Artifacts.Any(a =>
                scenario.Artifacts.Any(sa => sa.Id == a.Id && sa.Visibility == VisibilityScope.Private));
            if (hasPrivateArtifact)
                return false;
        }

        // Assert — GMOnly never visible to Player or Observer
        if (scenario.RequestingRole == WorldRole.Player ||
            scenario.RequestingRole == WorldRole.Observer)
        {
            var hasGMOnlyArtifact = result.Artifacts.Any(a =>
                scenario.Artifacts.Any(sa => sa.Id == a.Id && sa.Visibility == VisibilityScope.GMOnly));
            if (hasGMOnlyArtifact)
                return false;

            var hasGMOnlyFact = result.Facts.Any(f =>
                scenario.Facts.Any(sf => sf.Id == f.Id && sf.Visibility == VisibilityScope.GMOnly));
            if (hasGMOnlyFact)
                return false;

            var hasGMOnlyRelationship = result.Relationships.Any(r =>
                scenario.Relationships.Any(sr => sr.Id == r.Id && sr.Visibility == VisibilityScope.GMOnly));
            if (hasGMOnlyRelationship)
                return false;
        }

        return true;
    }
}

/// <summary>
/// Input model for visibility filter correctness scenarios.
/// </summary>
public record VisibilityFilterScenario(
    Guid WorldId,
    Guid RequestingUserId,
    WorldRole RequestingRole,
    List<Artifact> Artifacts,
    List<ArtifactFact> Facts,
    List<ArtifactRelationship> Relationships);

/// <summary>
/// Custom FsCheck arbitraries for visibility filter correctness tests.
/// Generates worlds with mixed-visibility artifacts, facts, and relationships
/// owned by different users, with a requesting user and role.
/// </summary>
public class VisibilityFilterArbitraries
{
    private static readonly string[] ArtifactNames =
    [
        "Captain Voss", "Black Harbor", "Silver Key", "Missing Caravan",
        "Tavrin", "Kelda", "Jorin", "Shadowfang", "Old Lighthouse",
        "Crimson Seal", "Iron Gate", "Storm Drake"
    ];

    private static readonly string[] RelationshipTypes =
    [
        "LocatedIn", "SuspectedIn", "AlliedWith", "Possesses", "Knows"
    ];

    public static Arbitrary<VisibilityFilterScenario> VisibilityFilterScenarios()
    {
        var visibilityGen = Gen.Elements(
            VisibilityScope.Private,
            VisibilityScope.GMOnly,
            VisibilityScope.PartyVisible);

        var roleGen = Gen.Elements(
            WorldRole.GM,
            WorldRole.Player,
            WorldRole.Observer);

        var truthStateGen = Gen.Elements(
            TruthState.Confirmed,
            TruthState.Likely,
            TruthState.Rumor,
            TruthState.Disputed);

        var artifactTypeGen = Gen.Elements(
            ArtifactType.Character,
            ArtifactType.Location,
            ArtifactType.Item,
            ArtifactType.Faction,
            ArtifactType.Event);

        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from requestingUserId in ArbMap.Default.GeneratorFor<Guid>()
            from otherUserId in ArbMap.Default.GeneratorFor<Guid>()
            from requestingRole in roleGen
            from artifactCount in Gen.Choose(2, 6)
            from nameIndices in Gen.Elements(Enumerable.Range(0, ArtifactNames.Length).ToArray())
                .ArrayOf(artifactCount)
            let distinctNames = nameIndices.Distinct().Select(i => ArtifactNames[i]).Take(artifactCount).ToArray()
            where distinctNames.Length >= 2
            // Small owner pool {requester, other user, unowned} keeps shrinking effective.
            let ownerGen = Gen.Elements<Guid?>(requestingUserId, otherUserId, null)
            from artifacts in GenArtifacts(worldId, distinctNames, artifactTypeGen, visibilityGen, ownerGen)
            from facts in GenFacts(artifacts, visibilityGen, truthStateGen, ownerGen)
            from relationships in GenRelationships(worldId, artifacts, visibilityGen, truthStateGen, ownerGen)
            select new VisibilityFilterScenario(
                worldId, requestingUserId, requestingRole,
                artifacts, facts, relationships);

        return gen.ToArbitrary();
    }

    private static Gen<List<Artifact>> GenArtifacts(
        Guid worldId,
        string[] names,
        Gen<ArtifactType> typeGen,
        Gen<VisibilityScope> visibilityGen,
        Gen<Guid?> ownerGen)
    {
        // Generate a list of (type, visibility, owner) triples then map to artifacts with fixed names
        var tripleGen =
            from artifactType in typeGen
            from visibility in visibilityGen
            from owner in ownerGen
            select (artifactType, visibility, owner);

        return tripleGen.ArrayOf(names.Length).Select(triples =>
            triples.Zip(names, (t, name) => new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                Type = t.artifactType,
                Name = name,
                Summary = $"Summary of {name}",
                Visibility = t.visibility,
                CreatedByUserId = t.owner,
                Confidence = 0.8m,
                Status = ArtifactStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                RowVersion = []
            }).ToList());
    }

    private static Gen<List<ArtifactFact>> GenFacts(
        List<Artifact> artifacts,
        Gen<VisibilityScope> visibilityGen,
        Gen<TruthState> truthStateGen,
        Gen<Guid?> ownerGen)
    {
        // Generate 2 facts per artifact with random visibility and owner
        var factsPerArtifact = 2;
        var totalFacts = artifacts.Count * factsPerArtifact;

        var singleFactGen =
            from visibility in visibilityGen
            from truthState in truthStateGen
            from owner in ownerGen
            select (visibility, truthState, owner);

        return singleFactGen.ArrayOf(totalFacts).Select(triples =>
        {
            var facts = new List<ArtifactFact>();
            for (var i = 0; i < artifacts.Count; i++)
            {
                for (var j = 0; j < factsPerArtifact; j++)
                {
                    var idx = i * factsPerArtifact + j;
                    facts.Add(new ArtifactFact
                    {
                        Id = Guid.NewGuid(),
                        ArtifactId = artifacts[i].Id,
                        Predicate = $"predicate_{j}",
                        Value = $"value_{j}",
                        Confidence = 0.9m,
                        TruthState = triples[idx].truthState,
                        Visibility = triples[idx].visibility,
                        CreatedByUserId = triples[idx].owner,
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
                        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                        RowVersion = []
                    });
                }
            }
            return facts;
        });
    }

    private static Gen<List<ArtifactRelationship>> GenRelationships(
        Guid worldId,
        List<Artifact> artifacts,
        Gen<VisibilityScope> visibilityGen,
        Gen<TruthState> truthStateGen,
        Gen<Guid?> ownerGen)
    {
        if (artifacts.Count < 2)
            return Gen.Constant(new List<ArtifactRelationship>());

        // Generate 1-3 relationships between random pairs of artifacts
        var relationshipGen =
            from indexA in Gen.Choose(0, artifacts.Count - 1)
            from indexB in Gen.Choose(0, artifacts.Count - 1)
            where indexA != indexB
            from relType in Gen.Elements(RelationshipTypes)
            from visibility in visibilityGen
            from truthState in truthStateGen
            from owner in ownerGen
            select new ArtifactRelationship
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                ArtifactAId = artifacts[indexA].Id,
                ArtifactBId = artifacts[indexB].Id,
                Type = relType,
                Description = $"{artifacts[indexA].Name} {relType} {artifacts[indexB].Name}",
                Confidence = 0.85m,
                TruthState = truthState,
                Visibility = visibility,
                CreatedByUserId = owner,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                RowVersion = []
            };

        return
            from count in Gen.Choose(1, 3)
            from relationships in relationshipGen.ArrayOf(count)
            select relationships.ToList();
    }
}
