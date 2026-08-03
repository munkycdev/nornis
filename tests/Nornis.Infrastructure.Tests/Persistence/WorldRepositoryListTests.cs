using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The world switcher renders <see cref="WorldRepository.ListByUserAsync"/> in order, so the
/// query has to impose one — without an OrderBy the list is whatever the server happens to
/// return, and the sidebar can silently reshuffle between requests.
/// </summary>
[TestFixture]
public class WorldRepositoryListTests : IntegrationTestBase
{
    private Guid SeedUser()
    {
        var tag = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|{tag}",
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        Context.Users.Add(user);
        return user.Id;
    }

    private void SeedWorld(Guid userId, string name, bool isTemplate = false)
    {
        var now = DateTimeOffset.UtcNow;
        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
            IsTemplate = isTemplate,
        };
        Context.Worlds.Add(world);
        Context.WorldMembers.Add(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            UserId = userId,
            Role = WorldRole.GM,
            JoinedAt = now,
        });
    }

    [Test]
    public async Task ListByUserAsync_OrdersByName()
    {
        var userId = SeedUser();
        // Inserted deliberately out of order; the query, not the insert order, decides.
        SeedWorld(userId, "Vespergale Reach");
        SeedWorld(userId, "Aldenmoor");
        SeedWorld(userId, "Brackwater Deep");
        await Context.SaveChangesAsync();

        var repository = new WorldRepository(Context);

        var worlds = await repository.ListByUserAsync(userId);

        Assert.That(worlds.Select(w => w.Name),
            Is.EqualTo(["Aldenmoor", "Brackwater Deep", "Vespergale Reach"]));
    }

    [Test]
    public async Task ListByUserAsync_StillReturnsTemplateWorlds()
    {
        // Template masters are grouped in the UI, never filtered out of the query: a world
        // missing from this list is unreachable in the app, and re-exporting the template
        // runs through the normal world UI.
        var userId = SeedUser();
        SeedWorld(userId, "Aldenmoor");
        SeedWorld(userId, "Vespergale Reach (template master)", isTemplate: true);
        await Context.SaveChangesAsync();

        var repository = new WorldRepository(Context);

        var worlds = await repository.ListByUserAsync(userId);

        Assert.That(worlds, Has.Count.EqualTo(2));
        Assert.That(worlds.Single(w => w.IsTemplate).Name,
            Is.EqualTo("Vespergale Reach (template master)"));
    }
}
