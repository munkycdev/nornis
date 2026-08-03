using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// ListTimelineBeforeAsync feeds extraction's location carry-over: the timeline sources
/// strictly before a pivot moment, nearest first. These tests pin the strict tuple
/// comparison (effective date, then CreatedAt), the timeline type set, campaign scoping,
/// and visibility filtering — against real SQL translation, not the in-memory fake.
/// </summary>
[TestFixture]
public class SourceRepositoryTimelineTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Day5 = new(2026, 7, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day10 = new(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day15 = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day20 = new(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);

    private static readonly VisibilityFilter PartyFilter = new()
    {
        Scopes = [VisibilityScope.PartyVisible]
    };

    private (World World, User User) SeedWorldAndUser()
    {
        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|{tag}",
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = now,
            UpdatedAt = now,
            RowVersion = []
        };
        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = "World",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = user.Id,
            RowVersion = []
        };
        Context.Users.Add(user);
        Context.Worlds.Add(world);
        Context.SaveChanges();
        return (world, user);
    }

    private Source SeedSource(
        Guid worldId,
        Guid userId,
        string title,
        DateTimeOffset? occurredAt,
        DateTimeOffset createdAt,
        SourceType type = SourceType.SessionNote,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        Guid? campaignId = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            CampaignId = campaignId,
            Type = type,
            Title = title,
            Body = "Body",
            OccurredAt = occurredAt,
            CreatedAt = createdAt,
            CreatedByUserId = userId,
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        Context.Sources.Add(source);
        Context.SaveChanges();
        return source;
    }

    private Campaign SeedCampaign(Guid worldId, Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Name = $"Campaign {Guid.NewGuid():N}",
            Status = CampaignStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = userId
        };
        Context.Campaigns.Add(campaign);
        Context.SaveChanges();
        return campaign;
    }

    [Test]
    public async Task ReturnsOnlyStrictlyEarlierSources_NearestFirst()
    {
        var (world, user) = SeedWorldAndUser();
        SeedSource(world.Id, user.Id, "S1", Day5, Day5);
        SeedSource(world.Id, user.Id, "S2", Day10, Day10);
        SeedSource(world.Id, user.Id, "S4", Day20, Day20);
        var repo = new SourceRepository(Context);

        var result = await repo.ListTimelineBeforeAsync(
            world.Id, null, Day15, Day15, PartyFilter, 10);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["S2", "S1"]));
    }

    [Test]
    public async Task SameEffectiveDate_CreatedAtBreaksTheTie()
    {
        // Two notes stamped with the same in-game day; only the one uploaded earlier
        // than the pivot's own upload moment counts as "before".
        var (world, user) = SeedWorldAndUser();
        SeedSource(world.Id, user.Id, "Uploaded first", Day10, Day10);
        SeedSource(world.Id, user.Id, "Uploaded after pivot", Day10, Day10.AddMinutes(30));
        var repo = new SourceRepository(Context);

        var result = await repo.ListTimelineBeforeAsync(
            world.Id, null, Day10, Day10.AddMinutes(10), PartyFilter, 10);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["Uploaded first"]));
    }

    [Test]
    public async Task UndatedImportedNotes_ParticipateByCreatedAt()
    {
        var (world, user) = SeedWorldAndUser();
        SeedSource(world.Id, user.Id, "Imported wiki page", null, Day10, SourceType.ImportedNote);
        var repo = new SourceRepository(Context);

        var result = await repo.ListTimelineBeforeAsync(
            world.Id, null, Day15, Day15, PartyFilter, 10);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["Imported wiki page"]));
    }

    [Test]
    public async Task NonTimelineTypes_AreExcluded()
    {
        var (world, user) = SeedWorldAndUser();
        SeedSource(world.Id, user.Id, "A map", Day5, Day5, SourceType.Map);
        SeedSource(world.Id, user.Id, "An upload", Day5, Day5, SourceType.Upload);
        SeedSource(world.Id, user.Id, "A GM note", Day5, Day5, SourceType.GMNote);
        SeedSource(world.Id, user.Id, "Legacy transcript", Day10, Day10, SourceType.Transcript);
        var repo = new SourceRepository(Context);

        var result = await repo.ListTimelineBeforeAsync(
            world.Id, null, Day15, Day15, PartyFilter, 10);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["Legacy transcript"]));
    }

    [Test]
    public async Task CampaignScoping_ExcludesOtherCampaigns_KeepsCampaignless()
    {
        var (world, user) = SeedWorldAndUser();
        var mine = SeedCampaign(world.Id, user.Id);
        var other = SeedCampaign(world.Id, user.Id);
        SeedSource(world.Id, user.Id, "Mine", Day10, Day10, campaignId: mine.Id);
        SeedSource(world.Id, user.Id, "Other era", Day10, Day10.AddMinutes(1), campaignId: other.Id);
        SeedSource(world.Id, user.Id, "Untagged", Day5, Day5);
        var repo = new SourceRepository(Context);

        var result = await repo.ListTimelineBeforeAsync(
            world.Id, mine.Id, Day15, Day15, PartyFilter, 10);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["Mine", "Untagged"]));
    }

    [Test]
    public async Task VisibilityFilter_HidesOutOfScopeSources()
    {
        var (world, user) = SeedWorldAndUser();
        SeedSource(world.Id, user.Id, "GM aside", Day10, Day10, visibility: VisibilityScope.GMOnly);
        SeedSource(world.Id, user.Id, "Someone's private note", Day10, Day10.AddMinutes(1),
            visibility: VisibilityScope.Private);
        SeedSource(world.Id, user.Id, "Party session", Day5, Day5);
        var repo = new SourceRepository(Context);

        var result = await repo.ListTimelineBeforeAsync(
            world.Id, null, Day15, Day15, PartyFilter, 10);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["Party session"]));
    }

    [Test]
    public async Task MaxCount_CapsTheWalk()
    {
        var (world, user) = SeedWorldAndUser();
        SeedSource(world.Id, user.Id, "S1", Day5, Day5);
        SeedSource(world.Id, user.Id, "S2", Day10, Day10);
        SeedSource(world.Id, user.Id, "S3", Day15, Day15);
        var repo = new SourceRepository(Context);

        var result = await repo.ListTimelineBeforeAsync(
            world.Id, null, Day20, Day20, PartyFilter, 2);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["S3", "S2"]));
    }

    // ------------------------------------------------- Replay queue (After) --

    [Test]
    public async Task After_ReturnsOnlyStrictlyLaterEligibleSources_EarliestFirst()
    {
        var (world, user) = SeedWorldAndUser();
        SeedSource(world.Id, user.Id, "Before", Day5, Day5);
        SeedSource(world.Id, user.Id, "Later A", Day15, Day15);
        SeedSource(world.Id, user.Id, "Later B", Day20, Day20);
        var repo = new SourceRepository(Context);

        var result = await repo.ListExtractableAfterAsync(world.Id, Day10, Day10, 10);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["Later A", "Later B"]));
    }

    [Test]
    public async Task After_SameEffectiveDate_CreatedAtBreaksTheTie()
    {
        var (world, user) = SeedWorldAndUser();
        SeedSource(world.Id, user.Id, "Uploaded before pivot", Day10, Day10);
        SeedSource(world.Id, user.Id, "Uploaded after pivot", Day10, Day10.AddMinutes(30));
        var repo = new SourceRepository(Context);

        var result = await repo.ListExtractableAfterAsync(
            world.Id, Day10, Day10.AddMinutes(10), 10);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["Uploaded after pivot"]));
    }

    [Test]
    public async Task After_ExcludesIneligibleSources()
    {
        var (world, user) = SeedWorldAndUser();
        var noExtraction = SeedSource(world.Id, user.Id, "No extraction", Day15, Day15);
        noExtraction.ExtractionEnabled = false;
        Context.SaveChanges();
        var draft = SeedSource(world.Id, user.Id, "Still a draft", Day15, Day15);
        draft.ProcessingStatus = SourceProcessingStatus.Draft;
        Context.SaveChanges();
        SeedSource(world.Id, user.Id, "Failed but retryable", Day20, Day20)
            .ProcessingStatus = SourceProcessingStatus.Failed;
        Context.SaveChanges();
        var repo = new SourceRepository(Context);

        var result = await repo.ListExtractableAfterAsync(world.Id, Day10, Day10, 10);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["Failed but retryable"]));
    }

    [Test]
    public async Task After_IncludesEveryExtractableType_NotJustTimelineOnes()
    {
        // The replay re-extracts the whole world. While this predicate was timeline-only,
        // GM notes, uploads and maps came back empty from a re-extraction that reported
        // itself complete. Guards the SQL predicate against the in-memory fake drifting.
        var (world, user) = SeedWorldAndUser();
        SeedSource(world.Id, user.Id, "Session", Day15, Day15);
        SeedSource(world.Id, user.Id, "GM prep", Day15, Day15.AddMinutes(1), SourceType.GMNote);
        SeedSource(world.Id, user.Id, "Lore PDF", Day15, Day15.AddMinutes(2), SourceType.Upload);
        SeedSource(world.Id, user.Id, "Region map", Day15, Day15.AddMinutes(3), SourceType.Map);
        SeedSource(world.Id, user.Id, "Scanned page", Day15, Day15.AddMinutes(4), SourceType.HandwrittenNotes);
        var repo = new SourceRepository(Context);

        var result = await repo.ListExtractableAfterAsync(world.Id, Day10, Day10, 10);

        Assert.That(result.Select(s => s.Title),
            Is.EqualTo(["Session", "GM prep", "Lore PDF", "Region map", "Scanned page"]));
    }

    [Test]
    public async Task After_UndatedSourcesTakeTheirPositionFromCreatedAt()
    {
        // 30 of the 34 GM notes in a real imported world carry no session date; without a
        // CreatedAt fallback they would never enter the walk at all.
        var (world, user) = SeedWorldAndUser();
        var undated = SeedSource(world.Id, user.Id, "Undated GM note", Day15, Day15, SourceType.GMNote);
        undated.OccurredAt = null;
        Context.SaveChanges();
        var repo = new SourceRepository(Context);

        var result = await repo.ListExtractableAfterAsync(world.Id, Day10, Day10, 10);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["Undated GM note"]));
    }

    [Test]
    public async Task After_CountMatchesUnboundedList()
    {
        var (world, user) = SeedWorldAndUser();
        SeedSource(world.Id, user.Id, "S1", Day10, Day10);
        SeedSource(world.Id, user.Id, "S2", Day15, Day15);
        SeedSource(world.Id, user.Id, "S3", Day20, Day20);
        var repo = new SourceRepository(Context);

        var count = await repo.CountExtractableAfterAsync(world.Id, Day5, Day5);

        Assert.That(count, Is.EqualTo(3));
    }
}
