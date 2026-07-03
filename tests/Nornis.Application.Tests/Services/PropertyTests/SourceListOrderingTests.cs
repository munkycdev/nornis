using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 12: List Ordering
///
/// For any campaign with one or more visible sources, listing sources should return them
/// ordered by CreatedAt descending (most recent first).
///
/// **Validates: Requirements 5.4**
/// </summary>
[TestFixture]
[Category("Feature: campaign-sources, Property 12: List Ordering")]
public class SourceListOrderingTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(SourceListOrderingArbitraries)],
        MaxTest = 100)]
    [Description("Feature: campaign-sources, Property 12: List Ordering")]
    public void ListByCampaign_ReturnsSourcesOrderedByCreatedAtDescending(SourceListOrderingInput input)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, queueClient);

        // Seed all sources into the repository
        foreach (var source in input.Sources)
        {
            sourceRepo.CreateAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Act — list as GM who can see everything
        var result = service.ListByCampaignAsync(
            input.CampaignId,
            input.RequestingUserId,
            CampaignRole.GM,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — operation should succeed
        Assert.That(result.IsSuccess, Is.True,
            "ListByCampaignAsync should succeed for a GM.");

        var returnedSources = result.Value!;

        // Assert — all sources are returned (all are PartyVisible, GM sees everything)
        Assert.That(returnedSources.Count, Is.EqualTo(input.Sources.Count),
            $"Expected {input.Sources.Count} sources but got {returnedSources.Count}");

        // Assert — for each consecutive pair, earlier one has CreatedAt >= later one (descending order)
        for (var i = 0; i < returnedSources.Count - 1; i++)
        {
            Assert.That(returnedSources[i].CreatedAt, Is.GreaterThanOrEqualTo(returnedSources[i + 1].CreatedAt),
                $"Sources[{i}].CreatedAt ({returnedSources[i].CreatedAt}) should be >= Sources[{i + 1}].CreatedAt ({returnedSources[i + 1].CreatedAt})");
        }
    }
}

/// <summary>
/// Input model for Source List Ordering property test.
/// </summary>
public record SourceListOrderingInput(
    Guid CampaignId,
    Guid RequestingUserId,
    List<Source> Sources);

/// <summary>
/// Custom FsCheck arbitraries for Source List Ordering test.
/// Generates 2–15 sources with random CreatedAt values spread over different days/hours,
/// all PartyVisible to simplify the test (focus on ordering, not visibility).
/// </summary>
public class SourceListOrderingArbitraries
{
    private static readonly char[] TitleChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -'".ToCharArray();

    public static Arbitrary<SourceListOrderingInput> SourceListOrderingInputs()
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

        var gen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from requestingUserId in ArbMap.Default.GeneratorFor<Guid>()
            from sourceCount in Gen.Choose(2, 15)
            from sources in GenSources(campaignId, requestingUserId, sourceCount, validTitleGen, sourceTypeGen)
            select new SourceListOrderingInput(campaignId, requestingUserId, sources);

        return gen.ToArbitrary();
    }

    private static Gen<List<Source>> GenSources(
        Guid campaignId,
        Guid creatorId,
        int count,
        Gen<string> titleGen,
        Gen<SourceType> sourceTypeGen)
    {
        var baseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var singleSourceGen =
            from title in titleGen
            from sourceType in sourceTypeGen
            from daysOffset in Gen.Choose(0, 365)
            from hoursOffset in Gen.Choose(0, 23)
            from minutesOffset in Gen.Choose(0, 59)
            select new Source
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                Type = sourceType,
                Title = title,
                Body = null,
                Uri = null,
                OccurredAt = null,
                CreatedAt = baseDate.AddDays(daysOffset).AddHours(hoursOffset).AddMinutes(minutesOffset),
                CreatedByUserId = creatorId,
                Visibility = VisibilityScope.PartyVisible,
                ProcessingStatus = SourceProcessingStatus.Draft
            };

        return singleSourceGen.ListOf(count).Select(l => l.ToList());
    }
}
