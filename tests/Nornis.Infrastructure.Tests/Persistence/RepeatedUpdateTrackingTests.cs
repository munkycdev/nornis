using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Updating the same row twice inside one request.
///
/// Repositories read AsNoTracking and write with Update(entity), so each call attaches a
/// fresh instance — and EF kept that instance tracked past SaveChanges, meaning the second
/// call attached a different object with the same key and threw. Deterministic, not a race:
/// a reveal whose FactIds and Corrections name the same fact hit it every time, as did the
/// second of two accepted proposals touching one artifact.
///
/// Only reachable against a real change tracker, which is why this lives here rather than
/// beside the service tests — the in-memory fakes track nothing and cannot fail this way.
/// </summary>
[TestFixture]
public class RepeatedUpdateTrackingTests : IntegrationTestBase
{
    private Guid _worldId;
    private Guid _userId;

    [SetUp]
    public async Task SetUp()
    {
        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");
        _userId = Guid.NewGuid();
        _worldId = Guid.NewGuid();

        Context.Users.Add(new User
        {
            Id = _userId,
            Auth0SubjectId = $"auth0|{tag}",
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = now,
            UpdatedAt = now,
            RowVersion = []
        });
        Context.Worlds.Add(new World
        {
            Id = _worldId,
            Name = "Vespergale",
            CreatedByUserId = _userId,
            CreatedAt = now,
            UpdatedAt = now,
            RowVersion = []
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    private async Task<Artifact> SeedArtifactAsync()
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedByUserId = _userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Context.Artifacts.Add(artifact);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return artifact;
    }

    [Test]
    public async Task UpdatingTheSameArtifactTwice_InOneScope_Succeeds()
    {
        var seeded = await SeedArtifactAsync();
        var repository = new ArtifactRepository(Context);

        // Two independent read-modify-writes, exactly as two proposals in one batch produce.
        var first = (await repository.GetByIdAsync(seeded.Id))!;
        first.Summary = "First edit";
        await repository.UpdateAsync(first);

        var second = (await repository.GetByIdAsync(seeded.Id))!;
        second.Summary = "Second edit";
        Assert.DoesNotThrowAsync(() => repository.UpdateAsync(second));

        var reread = (await repository.GetByIdAsync(seeded.Id))!;
        Assert.That(reread.Summary, Is.EqualTo("Second edit"), "the later write must win");
    }

    [Test]
    public async Task UpdatingTheSameFactTwice_InOneScope_Succeeds()
    {
        // The reveal shape: FactIds reveals it, Corrections then changes its truth state.
        var artifact = await SeedArtifactAsync();
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifact.Id,
            Predicate = "serves",
            Value = "the harbormaster",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.GMOnly,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Context.ArtifactFacts.Add(fact);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var repository = new ArtifactFactRepository(Context);

        var reveal = (await repository.GetByIdAsync(fact.Id))!;
        reveal.Visibility = VisibilityScope.PartyVisible;
        await repository.UpdateAsync(reveal);

        var correction = (await repository.GetByIdAsync(fact.Id))!;
        correction.TruthState = TruthState.False;
        Assert.DoesNotThrowAsync(() => repository.UpdateAsync(correction));

        var reread = (await repository.GetByIdAsync(fact.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(reread.Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
            Assert.That(reread.TruthState, Is.EqualTo(TruthState.False));
        });
    }
}
