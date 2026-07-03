using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 9: Confidence Indicator Determination
///
/// For any knowledge context, the computed confidence SHALL be Low when no artifacts are
/// retrieved, Medium when at least 1 confirmed/likely fact exists or at least 2 total facts
/// exist, and High when at least 3 confirmed/likely facts plus relationships plus source
/// references exist. The result SHALL always be one of High, Medium, or Low.
///
/// **Validates: Requirements 8.4**
/// </summary>
[TestFixture]
[Category("Feature: ask-loremaster, Property 9: Confidence Indicator Determination")]
public class ConfidenceIndicatorDeterminationTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ConfidenceIndicatorArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 9: Confidence Indicator Determination")]
    public void DetermineConfidence_AlwaysReturnsCorrectLevel_ForAnyKnowledgeContext(
        ConfidenceIndicatorScenario scenario)
    {
        // Act
        var result = LoremasterService.DetermineConfidence(scenario.Context);

        // Assert — result is always one of the valid enum values
        Assert.That(result, Is.AnyOf(ConfidenceLevel.High, ConfidenceLevel.Medium, ConfidenceLevel.Low),
            "Confidence must always be High, Medium, or Low.");

        // Assert — Low when no artifacts
        if (scenario.Context.Artifacts.Count == 0)
        {
            Assert.That(result, Is.EqualTo(ConfidenceLevel.Low),
                "Confidence SHALL be Low when no artifacts are retrieved.");
            return;
        }

        // Compute expected classification
        var confirmedLikelyCount = scenario.Context.Facts
            .Count(f => f.TruthState is TruthState.Confirmed or TruthState.Likely);
        var totalFactCount = scenario.Context.Facts.Count;
        var hasRelationships = scenario.Context.Relationships.Count > 0;
        var hasSourceReferences = scenario.Context.SourceReferences.Count > 0;

        // Assert — High when ≥3 confirmed/likely + relationships + source references
        if (confirmedLikelyCount >= 3 && hasRelationships && hasSourceReferences)
        {
            Assert.That(result, Is.EqualTo(ConfidenceLevel.High),
                $"Confidence SHALL be High when confirmedLikelyCount={confirmedLikelyCount}, " +
                $"hasRelationships={hasRelationships}, hasSourceReferences={hasSourceReferences}.");
            return;
        }

        // Assert — Medium when ≥1 confirmed/likely or ≥2 total facts
        if (confirmedLikelyCount >= 1 || totalFactCount >= 2)
        {
            Assert.That(result, Is.EqualTo(ConfidenceLevel.Medium),
                $"Confidence SHALL be Medium when confirmedLikelyCount={confirmedLikelyCount}, " +
                $"totalFactCount={totalFactCount}.");
            return;
        }

        // Assert — Low otherwise (artifacts exist but insufficient facts)
        Assert.That(result, Is.EqualTo(ConfidenceLevel.Low),
            $"Confidence SHALL be Low when confirmedLikelyCount={confirmedLikelyCount}, " +
            $"totalFactCount={totalFactCount}, no High/Medium conditions met.");
    }
}

/// <summary>
/// Input model for confidence indicator scenarios.
/// Contains a KnowledgeContext with varying numbers of artifacts, facts (with different
/// TruthStates), relationships, and source references.
/// </summary>
public record ConfidenceIndicatorScenario(KnowledgeContext Context);

/// <summary>
/// Custom FsCheck arbitraries for confidence indicator property tests.
/// Generates KnowledgeContext combinations covering all confidence levels:
/// - Empty artifacts (Low)
/// - Various fact counts with mixed truth states (Medium/Low)
/// - High confidence scenarios with 3+ confirmed/likely facts, relationships, and source refs
/// </summary>
public class ConfidenceIndicatorArbitraries
{
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

    public static Arbitrary<ConfidenceIndicatorScenario> ConfidenceIndicatorScenarios()
    {
        var gen =
            from artifactCount in Gen.Choose(0, 5)
            from factCount in Gen.Choose(0, 6)
            from relationshipCount in Gen.Choose(0, 4)
            from sourceRefCount in Gen.Choose(0, 4)
            from artifacts in GenArtifacts(artifactCount)
            from facts in GenFacts(factCount)
            from relationships in GenRelationships(relationshipCount)
            from sourceRefs in GenSourceReferences(sourceRefCount)
            let context = new KnowledgeContext
            {
                Artifacts = artifacts,
                Facts = facts,
                Relationships = relationships,
                SourceReferences = sourceRefs
            }
            select new ConfidenceIndicatorScenario(context);

        return gen.ToArbitrary();
    }

    private static Gen<List<KnowledgeArtifact>> GenArtifacts(int count)
    {
        if (count == 0)
            return Gen.Constant(new List<KnowledgeArtifact>());

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
