using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The key-art writes (feature 42) on a relational database, where the month cap is one
/// conditional UPDATE (correctness property 4). SQLite runs the same ExecuteUpdate SQL shape as
/// SQL Server; two callers truly at once can't be staged on one in-memory connection, so this
/// pins the WHERE and the SET that make a concurrent pass impossible.
/// </summary>
[TestFixture]
public class WorldKeyArtRepositoryTests : IntegrationTestBase
{
    private WorldKeyArtRepository _sut = null!;
    private Guid _worldId;

    [SetUp]
    public void SetUp()
    {
        _sut = new WorldKeyArtRepository(Context);
        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = Guid.NewGuid(),
            SignIns = { new UserSignIn { Subject = $"auth0|{tag}" } },
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = now,
            UpdatedAt = now,
            RowVersion = [],
        };
        var world = new World { Id = Guid.NewGuid(), Name = "Black Harbor", CreatedAt = now, UpdatedAt = now, CreatedByUserId = user.Id, RowVersion = [] };
        Context.Users.Add(user);
        Context.Worlds.Add(world);
        Context.SaveChanges();
        Context.ChangeTracker.Clear();
        _worldId = world.Id;
    }

    private World Stored() => Context.Worlds.AsNoTracking().Single(w => w.Id == _worldId);

    [Test]
    public async Task TheCount_StopsAtTheCap_AndANewMonthStartsAgain()
    {
        var first = await _sut.TryCountKeyArtImagesAsync(_worldId, "2026-11", 2, 5);
        var second = await _sut.TryCountKeyArtImagesAsync(_worldId, "2026-11", 2, 5);
        var over = await _sut.TryCountKeyArtImagesAsync(_worldId, "2026-11", 2, 5);
        var last = await _sut.TryCountKeyArtImagesAsync(_worldId, "2026-11", 1, 5);
        var november = Stored();
        var december = await _sut.TryCountKeyArtImagesAsync(_worldId, "2026-12", 2, 5);

        Assert.Multiple(() =>
        {
            Assert.That((bool[])[first, second, over, last], Is.EqualTo((bool[])[true, true, false, true]));
            Assert.That(november.KeyArtImagesGenerated, Is.EqualTo(5));
            Assert.That(december, Is.True);
            Assert.That(Stored().KeyArtImagesMonth, Is.EqualTo("2026-12"));
            Assert.That(Stored().KeyArtImagesGenerated, Is.EqualTo(2), "a new month is counted from zero, not on top");
        });
    }

    [Test]
    public async Task AGiveBack_OnlyTouchesTheMonthItWasCountedIn()
    {
        await _sut.TryCountKeyArtImagesAsync(_worldId, "2026-11", 2, 8);
        await _sut.ReleaseKeyArtImagesAsync(_worldId, "2026-11", 1);
        var afterRelease = Stored().KeyArtImagesGenerated;
        await _sut.TryCountKeyArtImagesAsync(_worldId, "2026-12", 2, 8);
        await _sut.ReleaseKeyArtImagesAsync(_worldId, "2026-11", 2);

        Assert.Multiple(() =>
        {
            Assert.That(afterRelease, Is.EqualTo(1));
            Assert.That(Stored().KeyArtImagesGenerated, Is.EqualTo(2), "November's give-back can't come off December");
        });
    }

    [Test]
    public async Task TheArtAndTheWork_RoundTrip()
    {
        var options = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var art = new WorldKeyArt
        {
            BlobPath = "worlds/w/key-art/options/a.png",
            ContentType = "image/png",
            Source = CoverSource.Generated,
            Brief = "A coast.",
            Anchor = KeyArtAnchor.Bottom,
            UpdatedAt = DateTimeOffset.Parse("2026-11-07T20:00:00Z"),
        };

        await _sut.SaveKeyArtWorkAsync(_worldId, "A coast.", options);
        await _sut.SetKeyArtAsync(_worldId, art);
        var stored = Stored();
        await _sut.SetKeyArtAsync(_worldId, null);

        Assert.Multiple(() =>
        {
            Assert.That(stored.KeyArt, Is.EqualTo(art));
            Assert.That(stored.KeyArtBrief, Is.EqualTo("A coast."));
            Assert.That(stored.KeyArtOptionIds, Is.EqualTo(options));
            Assert.That(Stored().KeyArt, Is.Null);
        });
    }
}
