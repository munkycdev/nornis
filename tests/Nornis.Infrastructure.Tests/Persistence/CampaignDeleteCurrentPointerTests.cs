using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Deleting the campaign a world is currently playing.
///
/// This is a database-level test rather than a service one because the constraint it guards is
/// a database fact: <c>World.CurrentCampaignId</c> is a Restrict foreign key, deliberately —
/// Campaigns already cascade from Worlds, and a second cascade back would give SQL Server two
/// paths between the same two tables, which it refuses. So the delete has to detach the
/// pointer itself, and if it ever stops doing so the failure is a foreign key violation on a
/// GM's ordinary delete, not a dangling row anyone would notice later.
/// </summary>
[TestFixture]
public class CampaignDeleteCurrentPointerTests : IntegrationTestBase
{
    private CampaignRepository _sut = null!;
    private World _world = null!;
    private Campaign _campaign = null!;

    [SetUp]
    public void SetUp()
    {
        _sut = new CampaignRepository(Context);

        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");

        var gm = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|gm-{tag}",
            Username = $"gm-{tag}",
            Email = $"gm-{tag}@example.com",
            CreatedAt = now,
            UpdatedAt = now,
            RowVersion = []
        };
        Context.Users.Add(gm);

        _world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Black Harbor",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = gm.Id,
            RowVersion = []
        };
        Context.Worlds.Add(_world);

        _campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Name = "Missing Caravan Arc",
            Status = CampaignStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = gm.Id
        };
        Context.Campaigns.Add(_campaign);
        Context.SaveChanges();

        _world.CurrentCampaignId = _campaign.Id;
        Context.SaveChanges();
    }

    [Test]
    public async Task DeletingTheCurrentCampaign_ClearsThePointerAndSucceeds()
    {
        await _sut.DeleteAsync(_campaign.Id, CancellationToken.None);

        Context.ChangeTracker.Clear();
        var world = await Context.Worlds.FindAsync(_world.Id);

        Assert.That(world!.CurrentCampaignId, Is.Null,
            "the world cannot point at a campaign that no longer exists");
        Assert.That(Context.Campaigns.Any(c => c.Id == _campaign.Id), Is.False);
    }

    [Test]
    public async Task DeletingSomeOtherCampaign_LeavesThePointerAlone()
    {
        var other = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Name = "Black Harbor Nights",
            Status = CampaignStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _world.CreatedByUserId
        };
        Context.Campaigns.Add(other);
        await Context.SaveChangesAsync();

        await _sut.DeleteAsync(other.Id, CancellationToken.None);

        Context.ChangeTracker.Clear();
        var world = await Context.Worlds.FindAsync(_world.Id);

        Assert.That(world!.CurrentCampaignId, Is.EqualTo(_campaign.Id));
    }
}
