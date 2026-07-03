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

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 5: Retrieval Count Cap
///
/// For any campaign with N artifacts (where N exceeds the configured MaxRetrievalCount),
/// the knowledge retriever SHALL return at most MaxRetrievalCount artifacts, with no artifact
/// appearing more than once.
///
/// **Validates: Requirements 4.5**
/// </summary>
[TestFixture]
[Category("Feature: ask-loremaster, Property 5: Retrieval Count Cap")]
public class RetrievalCountCapTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(RetrievalCountCapArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 5: Retrieval Count Cap")]
    public void RetrieveAsync_NeverExceedsMaxRetrievalCount_AndHasNoDuplicates(RetrievalCountCapScenario scenario)
    {
        // Arrange
        var artifactRepo = new InMemoryArtifactRepository();
        var factRepo = new InMemoryArtifactFactRepository();
        var relationshipRepo = new InMemoryArtifactRelationshipRepository();
        var sourceRefRepo = new InMemorySourceReferenceRepository();

        artifactRepo.Seed(scenario.Artifacts);

        var options = Options.Create(new LoremasterOptions
        {
            MaxRetrievalCount = scenario.MaxRetrievalCount,
            MaxFactsPerArtifact = 15
        });

        var retriever = new KeywordKnowledgeRetriever(
            artifactRepo,
            factRepo,
            relationshipRepo,
            sourceRefRepo,
            options);

        // Act
        var result = retriever.RetrieveAsync(
            scenario.Question,
            scenario.CampaignId,
            scenario.RequestingUserId,
            scenario.RequestingRole,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — at most MaxRetrievalCount artifacts returned
        Assert.That(result.Artifacts.Count, Is.LessThanOrEqualTo(scenario.MaxRetrievalCount),
            $"Retrieved {result.Artifacts.Count} artifacts but MaxRetrievalCount is {scenario.MaxRetrievalCount}.");

        // Assert — no duplicate artifact IDs in result
        var artifactIds = result.Artifacts.Select(a => a.Id).ToList();
        var uniqueIds = artifactIds.ToHashSet();
        Assert.That(uniqueIds.Count, Is.EqualTo(artifactIds.Count),
            "Duplicate artifact IDs found in retrieval result.");
    }
}

/// <summary>
/// Input model for retrieval count cap scenarios.
/// Represents a campaign with more artifacts than MaxRetrievalCount, a question that
/// references some artifact names, and a requesting user.
/// </summary>
public record RetrievalCountCapScenario(
    Guid CampaignId,
    Guid RequestingUserId,
    CampaignRole RequestingRole,
    List<Artifact> Artifacts,
    string Question,
    int MaxRetrievalCount);

/// <summary>
/// Custom FsCheck arbitraries for retrieval count cap tests.
/// Generates campaigns with artifact count exceeding MaxRetrievalCount,
/// ensuring some artifacts are name-matched and some are recent.
/// </summary>
public class RetrievalCountCapArbitraries
{
    private static readonly string[] ArtifactNames =
    [
        "Captain Voss", "Black Harbor", "Silver Key", "Missing Caravan",
        "Tavrin", "Jorin", "Kelda", "Iron Gate", "Shadow Cult",
        "Crystal Tower", "Ancient Map", "Blood Raven", "Storm Keep",
        "Ember Crown", "Frost Blade", "Dark Hollow", "Golden Chalice",
        "Serpent Isle", "Thunder Peak", "Midnight Forge", "Broken Shield",
        "Crimson Dawn", "Silent Valley", "Bone Throne", "Ash Wastes"
    ];

    public static Arbitrary<RetrievalCountCapScenario> RetrievalCountCapScenarios()
    {
        var roleGen = Gen.Elements(
            CampaignRole.GM,
            CampaignRole.Player,
            CampaignRole.Observer);

        // MaxRetrievalCount between 3 and 8 for manageable test sizes
        var maxCountGen = Gen.Choose(3, 8);

        var gen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from requestingUserId in ArbMap.Default.GeneratorFor<Guid>()
            from role in roleGen
            from maxCount in maxCountGen
            // Generate more artifacts than maxCount (between maxCount+2 and maxCount+15)
            from extraCount in Gen.Choose(2, 15)
            let artifactCount = maxCount + extraCount
            from artifacts in GenArtifacts(campaignId, artifactCount)
            // Pick 1-3 indices of artifacts to mention in the question (name-matching)
            from nameMatchCount in Gen.Choose(1, Math.Min(3, artifactCount))
            from matchedIndices in Gen.Choose(0, artifactCount - 1).ArrayOf(nameMatchCount)
            let question = BuildQuestion(artifacts, matchedIndices.Distinct().ToList())
            select new RetrievalCountCapScenario(
                campaignId,
                requestingUserId,
                role,
                artifacts,
                question,
                maxCount);

        return gen.ToArbitrary();
    }

    private static Gen<List<Artifact>> GenArtifacts(Guid campaignId, int count)
    {
        var artifactTypeGen = Gen.Elements(
            ArtifactType.Character,
            ArtifactType.Location,
            ArtifactType.Item,
            ArtifactType.Faction,
            ArtifactType.Event,
            ArtifactType.Thread,
            ArtifactType.Concept);

        // All artifacts are PartyVisible so all roles can see them
        var singleArtifactGen =
            from index in Gen.Choose(0, ArtifactNames.Length - 1)
            from artifactType in artifactTypeGen
            from daysAgo in Gen.Choose(0, 365)
            from hoursOffset in Gen.Choose(0, 23)
            select new
            {
                BaseName = ArtifactNames[index],
                Type = artifactType,
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo).AddHours(-hoursOffset)
            };

        return singleArtifactGen.ListOf(count).Select(items =>
        {
            var usedNames = new HashSet<string>();
            var artifacts = new List<Artifact>();
            var suffix = 1;

            foreach (var item in items)
            {
                // Ensure unique names by appending a suffix if needed
                var name = item.BaseName;
                while (!usedNames.Add(name))
                {
                    name = $"{item.BaseName} {suffix++}";
                }

                artifacts.Add(new Artifact
                {
                    Id = Guid.NewGuid(),
                    CampaignId = campaignId,
                    Type = item.Type,
                    Name = name,
                    Summary = $"Summary of {name}",
                    Visibility = VisibilityScope.PartyVisible,
                    Confidence = 0.9m,
                    Status = ArtifactStatus.Active,
                    CreatedAt = item.UpdatedAt.AddDays(-1),
                    UpdatedAt = item.UpdatedAt,
                    RowVersion = []
                });
            }

            return artifacts;
        });
    }

    private static string BuildQuestion(List<Artifact> artifacts, List<int> matchedIndices)
    {
        var names = matchedIndices
            .Where(i => i >= 0 && i < artifacts.Count)
            .Select(i => artifacts[i].Name)
            .Distinct()
            .ToList();

        if (names.Count == 0)
            return "Tell me about the campaign";

        return $"What do we know about {string.Join(" and ", names)}?";
    }
}
