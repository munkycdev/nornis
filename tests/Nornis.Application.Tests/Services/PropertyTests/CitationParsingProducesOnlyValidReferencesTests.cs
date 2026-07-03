using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Knowledge;
using Nornis.Application.Services;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 8: Citation Parsing Produces Only Valid References
///
/// For any AI response containing citation markers ([ref:ID]), the parsed citation list
/// SHALL only contain Citation objects whose reference IDs match items present in the
/// retrieved knowledge context. Markers referencing unknown IDs SHALL be silently excluded
/// from the citations list.
///
/// **Validates: Requirements 7.1, 7.3**
/// </summary>
[TestFixture]
[Category("Feature: ask-loremaster, Property 8: Citation Parsing Produces Only Valid References")]
public class CitationParsingProducesOnlyValidReferencesTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(CitationParsingArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 8: Citation Parsing Produces Only Valid References")]
    public void ParseCitations_ReturnsOnlyCitationsMatchingKnownContextIds(CitationParsingScenario scenario)
    {
        // Act
        var citations = LoremasterService.ParseCitations(scenario.AiResponseText, scenario.Context);

        // Assert — every returned citation has a reference ID present in the knowledge context
        var allKnownReferenceIds = GetAllKnownReferenceIds(scenario.Context);

        foreach (var citation in citations)
        {
            Assert.That(allKnownReferenceIds, Does.Contain(citation.ReferenceId),
                $"Citation with ReferenceId '{citation.ReferenceId}' does not match any known context item.");
        }

        // Assert — no citation has an unknown reference ID (inverse check)
        var unknownCitations = citations
            .Where(c => !allKnownReferenceIds.Contains(c.ReferenceId))
            .ToList();

        Assert.That(unknownCitations, Is.Empty,
            $"Found {unknownCitations.Count} citation(s) with unknown reference IDs.");

        // Assert — all valid [ref:ID] markers from the response that match known IDs are present
        var validMarkersInResponse = scenario.ValidReferenceIdsInResponse;
        var returnedReferenceIds = citations.Select(c => c.ReferenceId).ToHashSet();

        foreach (var validId in validMarkersInResponse)
        {
            Assert.That(returnedReferenceIds, Does.Contain(validId),
                $"Valid reference ID '{validId}' was in the AI response and context but not in parsed citations.");
        }

        // Assert — invalid markers are excluded
        var invalidMarkersInResponse = scenario.InvalidReferenceIdsInResponse;
        foreach (var invalidId in invalidMarkersInResponse)
        {
            Assert.That(returnedReferenceIds, Does.Not.Contain(invalidId),
                $"Invalid reference ID '{invalidId}' should have been silently dropped but appeared in citations.");
        }
    }

    private static HashSet<string> GetAllKnownReferenceIds(KnowledgeContext context)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var a in context.Artifacts)
            ids.Add(a.ReferenceId);
        foreach (var f in context.Facts)
            ids.Add(f.ReferenceId);
        foreach (var r in context.Relationships)
            ids.Add(r.ReferenceId);
        foreach (var s in context.SourceReferences)
            ids.Add(s.ReferenceId);

        return ids;
    }
}

/// <summary>
/// Input model for citation parsing scenarios.
/// Contains a knowledge context with known reference IDs and an AI response
/// text containing a mix of valid and invalid [ref:ID] markers.
/// </summary>
public record CitationParsingScenario(
    KnowledgeContext Context,
    string AiResponseText,
    List<string> ValidReferenceIdsInResponse,
    List<string> InvalidReferenceIdsInResponse);

/// <summary>
/// Custom FsCheck arbitraries for citation parsing property tests.
/// Generates knowledge contexts with known reference IDs and AI responses
/// containing a mix of valid and invalid citation markers.
/// </summary>
public class CitationParsingArbitraries
{
    private static readonly string[] ArtifactNames =
    [
        "Captain Voss", "Black Harbor", "Silver Key", "Missing Caravan",
        "Tavrin", "Kelda", "Iron Gate", "Shadow Cult", "Crystal Tower"
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

    public static Arbitrary<CitationParsingScenario> CitationParsingScenarios()
    {
        var gen =
            from artifactCount in Gen.Choose(0, 4)
            from factCount in Gen.Choose(0, 4)
            from relationshipCount in Gen.Choose(0, 3)
            from sourceRefCount in Gen.Choose(0, 3)
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
            let allKnownIds = GetAllReferenceIds(context)
            from validCount in Gen.Choose(0, Math.Min(allKnownIds.Count, 5))
            from invalidCount in Gen.Choose(0, 4)
            from selectedValidIds in PickRandom(allKnownIds, validCount)
            from invalidIds in GenInvalidIds(invalidCount, allKnownIds)
            let responseText = BuildAiResponse(selectedValidIds, invalidIds)
            select new CitationParsingScenario(
                context,
                responseText,
                selectedValidIds,
                invalidIds);

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
            TruthState.Confirmed, TruthState.Likely, TruthState.Rumor, TruthState.Disputed);

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

    private static List<string> GetAllReferenceIds(KnowledgeContext context)
    {
        var ids = new List<string>();
        ids.AddRange(context.Artifacts.Select(a => a.ReferenceId));
        ids.AddRange(context.Facts.Select(f => f.ReferenceId));
        ids.AddRange(context.Relationships.Select(r => r.ReferenceId));
        ids.AddRange(context.SourceReferences.Select(s => s.ReferenceId));
        return ids;
    }

    private static Gen<List<string>> PickRandom(List<string> items, int count)
    {
        if (items.Count == 0 || count == 0)
            return Gen.Constant(new List<string>());

        return Gen.Shuffle(items.ToArray())
            .Select(shuffled => shuffled.Take(count).ToList());
    }

    private static Gen<List<string>> GenInvalidIds(int count, List<string> knownIds)
    {
        if (count == 0)
            return Gen.Constant(new List<string>());

        var knownSet = knownIds.ToHashSet(StringComparer.Ordinal);

        var invalidIdGen =
            from id in ArbMap.Default.GeneratorFor<Guid>()
            let candidateId = $"unknown-{id:N}"
            // Ensure the generated ID is not in the known set (extremely unlikely but safe)
            where !knownSet.Contains(candidateId)
            select candidateId;

        return invalidIdGen.ListOf(count).Select(items => items.ToList());
    }

    private static string BuildAiResponse(List<string> validIds, List<string> invalidIds)
    {
        var parts = new List<string>();

        parts.Add("Based on the campaign knowledge, here is what I found.");

        foreach (var validId in validIds)
        {
            parts.Add($"This is supported by the source [ref:{validId}].");
        }

        foreach (var invalidId in invalidIds)
        {
            parts.Add($"Additional context suggests [ref:{invalidId}] is relevant.");
        }

        if (validIds.Count == 0 && invalidIds.Count == 0)
        {
            parts.Add("No specific citations are available for this answer.");
        }

        return string.Join(" ", parts);
    }
}
