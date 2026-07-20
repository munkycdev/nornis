using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

[TestFixture]
public class StorylineCampaignRepositoryTests : IntegrationTestBase
{
    private (World World, User User) SeedWorldAndUser()
    {
        var now = DateTimeOffset.UtcNow;
        // Unique per call: the fixture instance (and its in-memory DB) is shared across the
        // test methods, so a fixed Auth0SubjectId would collide on its unique index.
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

    private Artifact SeedStoryline(Guid worldId)
    {
        var now = DateTimeOffset.UtcNow;
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = ArtifactType.Storyline,
            Name = "Arc",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            RowVersion = []
        };
        Context.Artifacts.Add(artifact);
        Context.SaveChanges();
        return artifact;
    }

    private Campaign SeedCampaign(Guid worldId, Guid userId, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Name = name,
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
    public async Task Replace_ConvergesOnTheDesiredSet()
    {
        var (world, user) = SeedWorldAndUser();
        var storyline = SeedStoryline(world.Id);
        var one = SeedCampaign(world.Id, user.Id, "One");
        var two = SeedCampaign(world.Id, user.Id, "Two");
        var three = SeedCampaign(world.Id, user.Id, "Three");
        var repo = new StorylineCampaignRepository(Context);

        await repo.ReplaceForStorylineAsync(storyline.Id, new[] { one.Id, two.Id }, user.Id);
        await repo.ReplaceForStorylineAsync(storyline.Id, new[] { two.Id, three.Id }, user.Id);

        var links = await repo.ListByArtifactIdAsync(storyline.Id);
        Assert.That(links.Select(l => l.CampaignId), Is.EquivalentTo(new[] { two.Id, three.Id }));
    }

    [Test]
    public async Task ListByArtifactIds_ReturnsOnlyTheRequestedStorylines()
    {
        var (world, user) = SeedWorldAndUser();
        var a = SeedStoryline(world.Id);
        var b = SeedStoryline(world.Id);
        var campaign = SeedCampaign(world.Id, user.Id, "One");
        var repo = new StorylineCampaignRepository(Context);
        await repo.ReplaceForStorylineAsync(a.Id, new[] { campaign.Id }, user.Id);
        await repo.ReplaceForStorylineAsync(b.Id, new[] { campaign.Id }, user.Id);

        var links = await repo.ListByArtifactIdsAsync(new[] { a.Id });

        Assert.That(links.Select(l => l.ArtifactId), Is.EqualTo(new[] { a.Id }));
    }

    [Test]
    public async Task DeletingACampaign_ShedsItsDeclarations_AndKeepsTheStoryline()
    {
        var (world, user) = SeedWorldAndUser();
        var storyline = SeedStoryline(world.Id);
        var campaign = SeedCampaign(world.Id, user.Id, "Doomed");
        await new StorylineCampaignRepository(Context)
            .ReplaceForStorylineAsync(storyline.Id, new[] { campaign.Id }, user.Id);

        await new CampaignRepository(Context).DeleteAsync(campaign.Id);

        Assert.That(await Context.Campaigns.AsNoTracking().AnyAsync(c => c.Id == campaign.Id), Is.False);
        Assert.That(await Context.StorylineCampaigns.AsNoTracking().AnyAsync(sc => sc.CampaignId == campaign.Id), Is.False);
        Assert.That(await Context.Artifacts.AsNoTracking().AnyAsync(a => a.Id == storyline.Id), Is.True);
    }
}
