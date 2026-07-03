using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Knowledge;
using Nornis.Application.Services;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 7: Truth State Qualification in Prompt
///
/// For any knowledge context containing facts or relationships with TruthState of Rumor
/// or Disputed, the formatted context block in the prompt SHALL include the truth state
/// label alongside those items, enabling the AI to qualify claims appropriately.
///
/// **Validates: Requirements 6.2**
/// </summary>
[TestFixture]
[Category("Feature: ask-loremaster, Property 7: Truth State Qualification in Prompt")]
public class TruthStateQualificationInPromptTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(TruthStateQualificationArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 7: Truth State Qualification in Prompt")]
    public void FormatKnowledgeContext_RumorAndDisputedFacts_HaveTruthStateLabels(TruthStateQualificationScenario scenario)
    {
        // Act
        var formattedContext = LoremasterService.FormatKnowledgeContext(scenario.Context);

        // Assert — every Rumor fact should have [Rumor] label in the output
        foreach (var fact in scenario.Context.Facts.Where(f => f.TruthState == TruthState.Rumor))
        {
            // Match on the unique reference ID to avoid ambiguity when predicate/value collide
            var lines = formattedContext.Split('\n');
            var factLine = lines.FirstOrDefault(l => l.Contains(fact.ReferenceId));
            Assert.That(factLine, Is.Not.Null,
                $"Expected a line containing reference '{fact.ReferenceId}' for fact '{fact.Predicate}: {fact.Value}'.");
            Assert.That(factLine, Does.Contain("[Rumor]"),
                $"Expected [Rumor] on the same line as fact '{fact.Predicate}: {fact.Value}'.");
        }

        // Assert — every Disputed fact should have [Disputed] label in the output
        foreach (var fact in scenario.Context.Facts.Where(f => f.TruthState == TruthState.Disputed))
        {
            // Match on the unique reference ID to avoid ambiguity when predicate/value collide
            var lines = formattedContext.Split('\n');
            var factLine = lines.FirstOrDefault(l => l.Contains(fact.ReferenceId));
            Assert.That(factLine, Is.Not.Null,
                $"Expected a line containing reference '{fact.ReferenceId}' for fact '{fact.Predicate}: {fact.Value}'.");
            Assert.That(factLine, Does.Contain("[Disputed]"),
                $"Expected [Disputed] on the same line as fact '{fact.Predicate}: {fact.Value}'.");
        }

        // Assert — every Rumor relationship should have [Rumor] label in the output
        foreach (var rel in scenario.Context.Relationships.Where(r => r.TruthState == TruthState.Rumor))
        {
            var lines = formattedContext.Split('\n');
            var relLine = lines.FirstOrDefault(l => l.Contains(rel.ReferenceId));
            Assert.That(relLine, Is.Not.Null,
                $"Expected a line containing reference '{rel.ReferenceId}' for relationship type '{rel.Type}'.");
            Assert.That(relLine, Does.Contain("[Rumor]"),
                $"Expected [Rumor] on the same line as relationship type '{rel.Type}'.");
        }

        // Assert — every Disputed relationship should have [Disputed] label in the output
        foreach (var rel in scenario.Context.Relationships.Where(r => r.TruthState == TruthState.Disputed))
        {
            var lines = formattedContext.Split('\n');
            var relLine = lines.FirstOrDefault(l => l.Contains(rel.ReferenceId));
            Assert.That(relLine, Is.Not.Null,
                $"Expected a line containing reference '{rel.ReferenceId}' for relationship type '{rel.Type}'.");
            Assert.That(relLine, Does.Contain("[Disputed]"),
                $"Expected [Disputed] on the same line as relationship type '{rel.Type}'.");
        }
    }
}

/// <summary>
/// Input model for truth state qualification scenarios.
/// Contains a knowledge context guaranteed to have at least one Rumor or Disputed fact/relationship.
/// </summary>
public record TruthStateQualificationScenario(KnowledgeContext Context);

/// <summary>
/// Custom FsCheck arbitraries for truth state qualification in prompt tests.
/// Generates knowledge contexts with at least one Rumor or Disputed fact or relationship.
/// </summary>
public class TruthStateQualificationArbitraries
{
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

    private static readonly string[] ArtifactNames =
    [
        "Captain Voss", "Black Harbor", "Silver Key", "Missing Caravan",
        "Tavrin", "Kelda", "Jorin", "Shadow Cult"
    ];

    private static readonly string[] ArtifactTypes =
    [
        "Character", "Location", "Item", "Faction", "Event", "Storyline"
    ];

    public static Arbitrary<TruthStateQualificationScenario> TruthStateQualificationScenarios()
    {
        var gen =
            from artifactCount in Gen.Choose(1, 4)
            from artifacts in GenArtifacts(artifactCount)
            from rumorFactCount in Gen.Choose(1, 3)
            from disputedFactCount in Gen.Choose(1, 3)
            from confirmedFactCount in Gen.Choose(0, 2)
            from rumorFacts in GenFacts(artifacts, TruthState.Rumor, rumorFactCount)
            from disputedFacts in GenFacts(artifacts, TruthState.Disputed, disputedFactCount)
            from confirmedFacts in GenFacts(artifacts, TruthState.Confirmed, confirmedFactCount)
            from rumorRelCount in Gen.Choose(0, 2)
            from disputedRelCount in Gen.Choose(0, 2)
            from confirmedRelCount in Gen.Choose(0, 2)
            from rumorRels in GenRelationships(artifacts, TruthState.Rumor, rumorRelCount)
            from disputedRels in GenRelationships(artifacts, TruthState.Disputed, disputedRelCount)
            from confirmedRels in GenRelationships(artifacts, TruthState.Confirmed, confirmedRelCount)
            let allFacts = rumorFacts.Concat(disputedFacts).Concat(confirmedFacts).ToList()
            let allRels = rumorRels.Concat(disputedRels).Concat(confirmedRels).ToList()
            let context = new KnowledgeContext
            {
                Artifacts = artifacts,
                Facts = allFacts,
                Relationships = allRels,
                SourceReferences = []
            }
            select new TruthStateQualificationScenario(context);

        return gen.ToArbitrary();
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

    private static Gen<List<KnowledgeFact>> GenFacts(
        List<KnowledgeArtifact> artifacts, TruthState truthState, int count)
    {
        if (count == 0 || artifacts.Count == 0)
            return Gen.Constant(new List<KnowledgeFact>());

        var singleGen =
            from artifactIdx in Gen.Choose(0, artifacts.Count - 1)
            from predIdx in Gen.Choose(0, Predicates.Length - 1)
            from valIdx in Gen.Choose(0, Values.Length - 1)
            select new KnowledgeFact
            {
                Id = Guid.NewGuid(),
                ArtifactId = artifacts[artifactIdx].Id,
                Predicate = Predicates[predIdx],
                Value = Values[valIdx],
                TruthState = truthState,
                ReferenceId = $"fact-{Guid.NewGuid():N}"
            };

        return singleGen.ListOf(count).Select(items => items.ToList());
    }

    private static Gen<List<KnowledgeRelationship>> GenRelationships(
        List<KnowledgeArtifact> artifacts, TruthState truthState, int count)
    {
        if (count == 0 || artifacts.Count < 2)
            return Gen.Constant(new List<KnowledgeRelationship>());

        var singleGen =
            from aIdx in Gen.Choose(0, artifacts.Count - 1)
            from bIdx in Gen.Choose(0, artifacts.Count - 1)
            from typeIdx in Gen.Choose(0, RelationshipTypes.Length - 1)
            where aIdx != bIdx
            select new KnowledgeRelationship
            {
                Id = Guid.NewGuid(),
                ArtifactAId = artifacts[aIdx].Id,
                ArtifactBId = artifacts[bIdx].Id,
                Type = RelationshipTypes[typeIdx],
                Description = $"{artifacts[aIdx].Name} {RelationshipTypes[typeIdx]} {artifacts[bIdx].Name}",
                TruthState = truthState,
                ReferenceId = $"rel-{Guid.NewGuid():N}"
            };

        return singleGen.ListOf(count).Select(items => items.ToList());
    }
}
