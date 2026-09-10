using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The two repository halves of "a campaign files its own sources", run against a relational
/// provider: the date bounds on the list projection (which have to translate, and have to
/// leave undated rows out — a null compared against a bound is not "inside") and the bulk
/// filing statement, whose predicate is the only thing stopping one campaign's cleanup from
/// taking another's sessions.
/// </summary>
[TestFixture]
public class SourceCampaignFilingTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Began = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayAfterEnd = new(2024, 6, 2, 0, 0, 0, TimeSpan.Zero);

    private SourceRepository _repository = null!;
    private Guid _worldId;
    private Guid _gmId;
    private Guid _saltRoadId;
    private Guid _sideGameId;

    [SetUp]
    public async Task SetUp()
    {
        _worldId = Guid.NewGuid();
        _gmId = Guid.NewGuid();
        _saltRoadId = Guid.NewGuid();
        _sideGameId = Guid.NewGuid();

        Context.Sources.RemoveRange(Context.Sources);
        Context.Campaigns.RemoveRange(Context.Campaigns);
        Context.Worlds.RemoveRange(Context.Worlds);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        Context.Users.Add(new User
        {
            Id = _gmId,
            Auth0SubjectId = $"auth0|{_gmId:N}",
            Username = "kelda",
            Email = "kelda@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        Context.Worlds.Add(new World
        {
            Id = _worldId,
            Name = "Black Harbor",
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await Context.SaveChangesAsync();

        Context.Campaigns.AddRange(
            MakeCampaign(_saltRoadId, "The Salt Road"),
            MakeCampaign(_sideGameId, "The Side Game"));
        await Context.SaveChangesAsync();

        _repository = new SourceRepository(Context);
    }

    private Campaign MakeCampaign(Guid id, string name) => new()
    {
        Id = id,
        WorldId = _worldId,
        Name = name,
        CreatedByUserId = _gmId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<Guid> SeedAsync(string title, DateTimeOffset? occurredAt, Guid? campaignId = null)
    {
        var id = Guid.NewGuid();
        Context.Sources.Add(new Source
        {
            Id = id,
            WorldId = _worldId,
            CampaignId = campaignId,
            Type = SourceType.SessionNote,
            Title = title,
            OccurredAt = occurredAt,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return id;
    }

    [Test]
    public async Task Bounds_keep_dated_rows_inside_the_span_and_drop_undated_ones()
    {
        var inside = await SeedAsync("Session 3", Began.AddDays(40));
        var lastEvening = await SeedAsync("Last session", DayAfterEnd.AddHours(-5));
        await SeedAsync("Before", Began.AddSeconds(-1));
        await SeedAsync("The morning after", DayAfterEnd);
        await SeedAsync("Undated lore", occurredAt: null);

        var result = await _repository.ListSummariesByWorldAsync(
            _worldId, _gmId, WorldRole.GM, unassignedOnly: true, occurredFrom: Began, occurredBefore: DayAfterEnd);

        Assert.That(result.Select(s => s.Id), Is.EquivalentTo(new[] { inside, lastEvening }));
    }

    [Test]
    public async Task Each_bound_stands_alone()
    {
        var early = await SeedAsync("Long ago", Began.AddYears(-3));
        var late = await SeedAsync("Years later", DayAfterEnd.AddYears(3));
        await SeedAsync("Undated", occurredAt: null);

        var fromOnly = await _repository.ListSummariesByWorldAsync(
            _worldId, _gmId, WorldRole.GM, occurredFrom: Began);
        var beforeOnly = await _repository.ListSummariesByWorldAsync(
            _worldId, _gmId, WorldRole.GM, occurredBefore: DayAfterEnd);

        Assert.Multiple(() =>
        {
            Assert.That(fromOnly.Select(s => s.Id), Is.EqualTo(new[] { late }));
            Assert.That(beforeOnly.Select(s => s.Id), Is.EqualTo(new[] { early }));
        });
    }

    [Test]
    public async Task Filing_moves_only_sources_that_have_no_campaign()
    {
        var free = await SeedAsync("Session 3", Began.AddDays(40));
        var theirs = await SeedAsync("Their session", Began.AddDays(40), campaignId: _sideGameId);
        var untouched = await SeedAsync("Session 4", Began.AddDays(47));

        await _repository.FileUnderCampaignAsync([free, theirs], _saltRoadId);

        var byId = Context.Sources.ToDictionary(s => s.Id, s => s.CampaignId);
        Assert.Multiple(() =>
        {
            Assert.That(byId[free], Is.EqualTo(_saltRoadId));
            Assert.That(byId[theirs], Is.EqualTo(_sideGameId), "a source filed elsewhere was taken");
            Assert.That(byId[untouched], Is.Null, "a source that was not named was filed");
        });
    }
}
