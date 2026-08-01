using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

[TestFixture]
public class ExtractionReplayRepositoryTests : IntegrationTestBase
{
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

    private static ExtractionReplay MakeReplay(Guid worldId, Guid userId, ExtractionReplayStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            CurrentSourceId = Guid.NewGuid(),
            Status = status,
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = []
        };

    [Test]
    public async Task GetActiveByWorld_ReturnsOnlyTheActiveRun()
    {
        var (world, user) = SeedWorldAndUser();
        var repo = new ExtractionReplayRepository(Context);
        await repo.CreateAsync(MakeReplay(world.Id, user.Id, ExtractionReplayStatus.Completed));
        await repo.CreateAsync(MakeReplay(world.Id, user.Id, ExtractionReplayStatus.Canceled));
        var active = await repo.CreateAsync(MakeReplay(world.Id, user.Id, ExtractionReplayStatus.Active));

        var found = await repo.GetActiveByWorldAsync(world.Id);

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Id, Is.EqualTo(active!.Id));
    }

    [Test]
    public async Task GetActiveByWorld_OtherWorld_ReturnsNull()
    {
        var (world, user) = SeedWorldAndUser();
        var repo = new ExtractionReplayRepository(Context);
        await repo.CreateAsync(MakeReplay(world.Id, user.Id, ExtractionReplayStatus.Active));

        var found = await repo.GetActiveByWorldAsync(Guid.NewGuid());

        Assert.That(found, Is.Null);
    }

    [Test]
    public async Task Update_PersistsCursorAndStatus()
    {
        var (world, user) = SeedWorldAndUser();
        var repo = new ExtractionReplayRepository(Context);
        var replay = (await repo.CreateAsync(MakeReplay(world.Id, user.Id, ExtractionReplayStatus.Active)))!;

        var nextCursor = Guid.NewGuid();
        replay.CurrentSourceId = nextCursor;
        replay.Status = ExtractionReplayStatus.Completed;
        replay.CompletedAt = DateTimeOffset.UtcNow;
        await repo.UpdateAsync(replay);

        var reloaded = await CreateNewContext().ExtractionReplays.FindAsync(replay.Id);
        Assert.That(reloaded!.CurrentSourceId, Is.EqualTo(nextCursor));
        Assert.That(reloaded.Status, Is.EqualTo(ExtractionReplayStatus.Completed));
        Assert.That(reloaded.CompletedAt, Is.Not.Null);
    }
}
