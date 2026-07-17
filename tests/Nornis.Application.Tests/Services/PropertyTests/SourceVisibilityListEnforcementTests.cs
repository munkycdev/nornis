using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 6: Visibility Enforcement on List
///
/// For any set of sources in a world and any world member, listing sources should return exactly
/// the subset of sources the member is authorized to see: GMs see all sources; Players see PartyVisible
/// sources plus their own Private sources; Observers see only PartyVisible sources.
///
/// **Validates: Requirements 5.1, 5.2, 5.3**
/// </summary>
[TestFixture]
[Category("Feature: world-sources, Property 6: Visibility Enforcement on List")]
public class SourceVisibilityListEnforcementTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(VisibilityListArbitraries)],
        MaxTest = 100)]
    [Description("Feature: world-sources, Property 6: Visibility Enforcement on List")]
    public void ListByWorld_ReturnsExactlyTheVisibleSubset(VisibilityListScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, new InMemoryCampaignRepository(), queueClient,
            new InMemoryReviewBatchRepository(), new InMemorySourceAttachmentRepository(),
            new FakeBlobStorageService(), NullLogger<SourceService>.Instance);

        // Seed all sources in the repository
        foreach (var source in scenario.Sources)
        {
            sourceRepo.CreateAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Compute expected visible set using the visibility function
        var expectedIds = scenario.Sources
            .Where(s => IsVisibleTo(s, scenario.RequestingUserId, scenario.RequestingRole))
            .Select(s => s.Id)
            .ToHashSet();

        // Act
        var result = service.ListByWorldAsync(
            scenario.WorldId,
            scenario.RequestingUserId,
            scenario.RequestingRole,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — operation should succeed
        Assert.That(result.IsSuccess, Is.True,
            "ListByWorldAsync should always succeed.");

        var returnedSources = result.Value!;
        var returnedIds = returnedSources.Select(s => s.Id).ToHashSet();

        // Assert — returned set exactly matches the expected visible subset
        Assert.That(returnedIds, Is.EquivalentTo(expectedIds),
            $"Role={scenario.RequestingRole}: returned source Ids must match exactly the expected visible subset.");

        // Assert — returned sources are ordered by CreatedAt descending
        for (var i = 1; i < returnedSources.Count; i++)
        {
            Assert.That(returnedSources[i].CreatedAt, Is.LessThanOrEqualTo(returnedSources[i - 1].CreatedAt),
                "Sources must be ordered by CreatedAt descending.");
        }
    }

    private static bool IsVisibleTo(Source source, Guid userId, WorldRole role) => source.Visibility switch
    {
        VisibilityScope.PartyVisible => true,
        VisibilityScope.Private => role == WorldRole.GM || source.CreatedByUserId == userId,
        VisibilityScope.GMOnly => role == WorldRole.GM,
        _ => false
    };
}

/// <summary>
/// Input model for visibility list enforcement scenarios.
/// Represents a world with multiple sources and a requesting member with a specific role.
/// </summary>
public record VisibilityListScenario(
    Guid WorldId,
    List<Source> Sources,
    Guid RequestingUserId,
    WorldRole RequestingRole);

/// <summary>
/// Custom FsCheck arbitraries for visibility list enforcement tests.
/// Generates scenarios with 2–10 sources having random visibilities and random creators,
/// plus a requesting user with a random role.
/// </summary>
public class VisibilityListArbitraries
{
    private static readonly char[] TitleChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'".ToCharArray();

    public static Arbitrary<VisibilityListScenario> VisibilityListScenarios()
    {
        var validTitleGen =
            from length in Gen.Choose(1, 80)
            from chars in Gen.Elements(TitleChars).ArrayOf(length)
            let title = new string(chars).Trim()
            where !string.IsNullOrWhiteSpace(title) && title.Length >= 1 && title.Length <= 200
            select title;

        var sourceTypeGen = Gen.Elements(
            SourceType.SessionNote,
            SourceType.JournalEntry,
            SourceType.Transcript,
            SourceType.Upload,
            SourceType.Image,
            SourceType.HandwrittenNotes,
            SourceType.WebLink,
            SourceType.GMNote,
            SourceType.ImportedNote);

        var visibilityGen = Gen.Elements(
            VisibilityScope.Private,
            VisibilityScope.GMOnly,
            VisibilityScope.PartyVisible);

        var roleGen = Gen.Elements(
            WorldRole.GM,
            WorldRole.Player,
            WorldRole.Observer);

        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from requestingUserId in ArbMap.Default.GeneratorFor<Guid>()
            from requestingRole in roleGen
            from sourceCount in Gen.Choose(2, 10)
            from sources in GenSources(worldId, requestingUserId, sourceCount, validTitleGen, sourceTypeGen, visibilityGen)
            select new VisibilityListScenario(worldId, sources, requestingUserId, requestingRole);

        return gen.ToArbitrary();
    }

    private static Gen<List<Source>> GenSources(
        Guid worldId,
        Guid requestingUserId,
        int count,
        Gen<string> titleGen,
        Gen<SourceType> sourceTypeGen,
        Gen<VisibilityScope> visibilityGen)
    {
        var singleSourceGen =
            from title in titleGen
            from sourceType in sourceTypeGen
            from visibility in visibilityGen
            from creatorIsRequestor in Gen.Elements(true, false)
            from otherCreatorId in ArbMap.Default.GeneratorFor<Guid>()
            from daysAgo in Gen.Choose(0, 365)
            from hoursOffset in Gen.Choose(0, 23)
            let creatorId = creatorIsRequestor ? requestingUserId : otherCreatorId
            select new Source
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                Type = sourceType,
                Title = title,
                Body = null,
                Uri = null,
                OccurredAt = null,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo).AddHours(-hoursOffset),
                CreatedByUserId = creatorId,
                Visibility = visibility,
                ProcessingStatus = SourceProcessingStatus.Draft
            };

        return singleSourceGen.ListOf(count).Select(l => l.ToList());
    }
}
