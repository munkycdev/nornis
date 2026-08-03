using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The sources list is polled every four seconds while anything is processing, and used to load
/// whole rows — including <c>Body</c> and <c>DerivedText</c>, which in production hold ~1.5 MB of
/// session transcript across one world and which no list view reads.
///
/// It is now a SQL projection, so it also carries the visibility rule into the database. These
/// run the real query against a relational provider: an in-memory fake could agree with the C#
/// while the generated SQL leaked.
/// </summary>
[TestFixture]
public class SourceListProjectionTests : IntegrationTestBase
{
    private SourceRepository _repository = null!;
    private Guid _worldId;
    private Guid _gmId;
    private Guid _playerId;
    private Guid _campaignId;

    [SetUp]
    public async Task SetUp()
    {
        _worldId = Guid.NewGuid();
        _gmId = Guid.NewGuid();
        _playerId = Guid.NewGuid();
        _campaignId = Guid.NewGuid();

        Context.Sources.RemoveRange(Context.Sources);
        Context.Campaigns.RemoveRange(Context.Campaigns);
        Context.Worlds.RemoveRange(Context.Worlds);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        Context.Users.AddRange(MakeUser(_gmId, "kelda"), MakeUser(_playerId, "tavrin"));
        Context.Worlds.Add(new World
        {
            Id = _worldId,
            Name = "Black Harbor",
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await Context.SaveChangesAsync();

        Context.Campaigns.Add(new Campaign
        {
            Id = _campaignId,
            WorldId = _worldId,
            Name = "The Salt Road",
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await Context.SaveChangesAsync();

        _repository = new SourceRepository(Context);
    }

    private static User MakeUser(Guid id, string name) => new()
    {
        Id = id,
        Auth0SubjectId = $"auth0|{id:N}",
        Username = name,
        Email = $"{name}@example.com",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<Guid> SeedAsync(
        string title,
        VisibilityScope visibility,
        Guid createdBy,
        Guid? campaignId = null,
        int ageMinutes = 0,
        SourceProcessingStatus status = SourceProcessingStatus.Processed)
    {
        var id = Guid.NewGuid();
        Context.Sources.Add(new Source
        {
            Id = id,
            WorldId = _worldId,
            CampaignId = campaignId,
            Type = SourceType.SessionNote,
            Title = title,
            Body = new string('b', 20_000),
            DerivedText = new string('d', 20_000),
            Visibility = visibility,
            ProcessingStatus = status,
            CreatedByUserId = createdBy,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-ageMinutes),
        });
        await Context.SaveChangesAsync();
        return id;
    }

    [Test]
    public async Task ProjectionCarriesEveryFieldTheListDtoReads()
    {
        await SeedAsync("Session 4", VisibilityScope.PartyVisible, _gmId, _campaignId);

        var only = (await _repository.ListSummariesByWorldAsync(_worldId, _gmId, WorldRole.GM)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(only.Title, Is.EqualTo("Session 4"));
            Assert.That(only.WorldId, Is.EqualTo(_worldId));
            Assert.That(only.Type, Is.EqualTo(SourceType.SessionNote));
            Assert.That(only.Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
            Assert.That(only.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
            Assert.That(only.CreatedByUserId, Is.EqualTo(_gmId));
            Assert.That(only.CampaignId, Is.EqualTo(_campaignId));
            Assert.That(only.CampaignName, Is.EqualTo("The Salt Road"),
                "the campaign name comes through the navigation without materialising the campaign");
        });
    }

    [Test]
    public void ProjectionTypeCannotCarryTheBodyColumns()
    {
        // A type-level guarantee rather than a behavioural one: if someone adds Body to
        // SourceListItem later, the cost this change removed comes straight back.
        var properties = typeof(SourceListItem).GetProperties().Select(p => p.Name).ToList();

        Assert.That(properties, Has.No.Member("Body"));
        Assert.That(properties, Has.No.Member("DerivedText"));
    }

    [Test]
    public async Task VisibilityIsAppliedInSql_PerRole()
    {
        await SeedAsync("Party", VisibilityScope.PartyVisible, _gmId);
        await SeedAsync("GM only", VisibilityScope.GMOnly, _gmId);
        await SeedAsync("Player's private", VisibilityScope.Private, _playerId);

        var asGm = await _repository.ListSummariesByWorldAsync(_worldId, _gmId, WorldRole.GM);
        var asOwner = await _repository.ListSummariesByWorldAsync(_worldId, _playerId, WorldRole.Player);
        var asStranger = await _repository.ListSummariesByWorldAsync(_worldId, Guid.NewGuid(), WorldRole.Player);

        Assert.Multiple(() =>
        {
            Assert.That(asGm.Select(s => s.Title), Is.EquivalentTo(["Party", "GM only", "Player's private"]));
            Assert.That(asOwner.Select(s => s.Title), Is.EquivalentTo(["Party", "Player's private"]));
            Assert.That(asStranger.Select(s => s.Title), Is.EquivalentTo(["Party"]),
                "another player's Private note must not appear in the list");
        });
    }

    [Test]
    public async Task DraftsBelongToTheirAuthorAndTheGm()
    {
        await SeedAsync("Player draft", VisibilityScope.PartyVisible, _playerId, status: SourceProcessingStatus.Draft);

        var asGm = await _repository.ListSummariesByWorldAsync(_worldId, _gmId, WorldRole.GM);
        var asAuthor = await _repository.ListSummariesByWorldAsync(_worldId, _playerId, WorldRole.Player);
        var asStranger = await _repository.ListSummariesByWorldAsync(_worldId, Guid.NewGuid(), WorldRole.Player);

        Assert.Multiple(() =>
        {
            Assert.That(asGm, Has.Count.EqualTo(1));
            Assert.That(asAuthor, Has.Count.EqualTo(1));
            Assert.That(asStranger, Is.Empty, "an unfinished note belongs to its author until it is finished");
        });
    }

    [Test]
    public async Task NewestFirst()
    {
        await SeedAsync("Oldest", VisibilityScope.PartyVisible, _gmId, ageMinutes: 30);
        await SeedAsync("Newest", VisibilityScope.PartyVisible, _gmId, ageMinutes: 1);
        await SeedAsync("Middle", VisibilityScope.PartyVisible, _gmId, ageMinutes: 15);

        var result = await _repository.ListSummariesByWorldAsync(_worldId, _gmId, WorldRole.GM);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["Newest", "Middle", "Oldest"]));
    }

    [Test]
    public async Task CampaignFilters()
    {
        await SeedAsync("In campaign", VisibilityScope.PartyVisible, _gmId, _campaignId);
        await SeedAsync("Unassigned", VisibilityScope.PartyVisible, _gmId);

        var inCampaign = await _repository.ListSummariesByWorldAsync(_worldId, _gmId, WorldRole.GM, campaignId: _campaignId);
        var unassigned = await _repository.ListSummariesByWorldAsync(_worldId, _gmId, WorldRole.GM, unassignedOnly: true);

        Assert.Multiple(() =>
        {
            Assert.That(inCampaign.Select(s => s.Title), Is.EqualTo(["In campaign"]));
            Assert.That(unassigned.Select(s => s.Title), Is.EqualTo(["Unassigned"]));
        });
    }

    [Test]
    public async Task OtherWorldsAreNeverIncluded()
    {
        await SeedAsync("Ours", VisibilityScope.PartyVisible, _gmId);

        var otherWorldId = Guid.NewGuid();
        Context.Worlds.Add(new World
        {
            Id = otherWorldId,
            Name = "Elsewhere",
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        Context.Sources.Add(new Source
        {
            Id = Guid.NewGuid(),
            WorldId = otherWorldId,
            Type = SourceType.SessionNote,
            Title = "Theirs",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await Context.SaveChangesAsync();

        var result = await _repository.ListSummariesByWorldAsync(_worldId, _gmId, WorldRole.GM);

        Assert.That(result.Select(s => s.Title), Is.EqualTo(["Ours"]));
    }
}
