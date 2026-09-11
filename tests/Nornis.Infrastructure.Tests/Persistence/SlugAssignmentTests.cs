using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Slugs are assigned by the DbContext rather than by callers, because eight different writers
/// create page-bearing rows and the next one would not know to ask. Tested against the real
/// change tracker for the same reason the status stamp is: the mechanism is the tracker.
/// </summary>
[TestFixture]
public class SlugAssignmentTests : IntegrationTestBase
{
    private User _user = null!;
    private World _world = null!;

    [SetUp]
    public void Seed()
    {
        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");
        _user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|{tag}",
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = now,
            UpdatedAt = now,
            RowVersion = []
        };
        _world = NewWorld();
        Context.Users.Add(_user);
        Context.Worlds.Add(_world);
        Context.SaveChanges();
    }

    private World NewWorld() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Vespergale",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = _user.Id,
        RowVersion = []
    };

    private Artifact NewArtifact(string name, Guid? worldId = null) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = worldId ?? _world.Id,
        Type = ArtifactType.Character,
        Name = name,
        Visibility = VisibilityScope.PartyVisible,
        Status = ArtifactStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        RowVersion = []
    };

    private Source NewSource(string title) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = _world.Id,
        Type = SourceType.SessionNote,
        Title = title,
        Visibility = VisibilityScope.PartyVisible,
        ProcessingStatus = SourceProcessingStatus.Ready,
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = _user.Id
    };

    [Test]
    public async Task AddedRows_GetASlugFromTheirName()
    {
        var artifact = NewArtifact("The Ashen King");
        var source = NewSource("Session 12: The Long Night");
        Context.Artifacts.Add(artifact);
        Context.Sources.Add(source);
        await Context.SaveChangesAsync();

        Assert.That(artifact.Slug, Is.EqualTo("the-ashen-king"));
        Assert.That(source.Slug, Is.EqualTo("session-12-the-long-night"));
    }

    [Test]
    public async Task EveryKind_IsCovered()
    {
        var player = new Player { Id = Guid.NewGuid(), WorldId = _world.Id, Name = "Henry", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var campaign = new Campaign { Id = Guid.NewGuid(), WorldId = _world.Id, Name = "Ash and Salt", Status = CampaignStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, CreatedByUserId = _user.Id };
        var character = new Character { Id = Guid.NewGuid(), WorldId = _world.Id, PlayerId = player.Id, Name = "Mira Voss", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var document = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Title = "Player's Handbook",
            FileName = "phb.pdf",
            ContentType = "application/pdf",
            BlobPath = "x",
            Kind = LibraryDocumentKind.Sourcebook,
            Visibility = VisibilityScope.PartyVisible,
            Status = LibraryDocumentStatus.Indexed,
            UploadedByUserId = _user.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Context.AddRange(player, campaign, character, document);
        await Context.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(campaign.Slug, Is.EqualTo("ash-and-salt"));
            Assert.That(character.Slug, Is.EqualTo("mira-voss"));
            Assert.That(document.Slug, Is.EqualTo("players-handbook"));
        });
    }

    [Test]
    public async Task SameName_TakesTheNextSuffix_AcrossSavesAndWithinOne()
    {
        var first = NewArtifact("Bob");
        Context.Artifacts.Add(first);
        await Context.SaveChangesAsync();

        var second = NewArtifact("Bob");
        var third = NewArtifact("bob!");
        Context.Artifacts.AddRange(second, third);
        await Context.SaveChangesAsync();

        Assert.That(new[] { first.Slug, second.Slug, third.Slug },
            Is.EquivalentTo(["bob", "bob-2", "bob-3"]));
    }

    [Test]
    public async Task Uniqueness_IsPerWorldAndPerKind()
    {
        var other = NewWorld();
        Context.Worlds.Add(other);
        var here = NewArtifact("Bob");
        var there = NewArtifact("Bob", other.Id);
        var sourceNamedBob = NewSource("Bob");
        Context.Artifacts.AddRange(here, there);
        Context.Sources.Add(sourceNamedBob);
        await Context.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(here.Slug, Is.EqualTo("bob"));
            Assert.That(there.Slug, Is.EqualTo("bob"));
            Assert.That(sourceNamedBob.Slug, Is.EqualTo("bob"));
        });
    }

    [Test]
    public async Task ARename_KeepsTheSlug()
    {
        var artifact = NewArtifact("Unknown Figure");
        Context.Artifacts.Add(artifact);
        await Context.SaveChangesAsync();

        artifact.Name = "The Ashen King";
        await Context.SaveChangesAsync();

        Assert.That(artifact.Slug, Is.EqualTo("unknown-figure"));
    }

    [Test]
    public async Task NothingUsableInTheName_FallsBackToTheKind()
    {
        var artifact = NewArtifact("???");
        Context.Artifacts.Add(artifact);
        await Context.SaveChangesAsync();

        Assert.That(artifact.Slug, Is.EqualTo("artifact"));
    }

    [Test]
    public async Task Backfill_AssignsOnlyTheSluglessRows()
    {
        // Rows written before slugs existed: insert around the assigner by clearing afterwards.
        var old = NewArtifact("Old Bob");
        var oldSource = NewSource("Old Session");
        Context.Artifacts.Add(old);
        Context.Sources.Add(oldSource);
        await Context.SaveChangesAsync();
        await Context.Artifacts.Where(a => a.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Slug, (string?)null));
        await Context.Sources.Where(a => a.Id == oldSource.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Slug, (string?)null));
        Context.ChangeTracker.Clear();

        var kept = NewArtifact("Kept");
        Context.Artifacts.Add(kept);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        using var fresh = CreateNewContext();
        var assigned = await new SlugBackfiller(fresh).BackfillAsync();
        var again = await new SlugBackfiller(fresh).BackfillAsync();

        var slugs = await fresh.Artifacts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Slug);
        Assert.Multiple(() =>
        {
            Assert.That(assigned, Is.EqualTo(2));
            Assert.That(again, Is.EqualTo(0), "a second run finds nothing to do");
            Assert.That(slugs[old.Id], Is.EqualTo("old-bob"));
            Assert.That(slugs[kept.Id], Is.EqualTo("kept"));
            Assert.That(fresh.Sources.Single(s => s.Id == oldSource.Id).Slug, Is.EqualTo("old-session"));
        });
    }

    [Test]
    public async Task Resolver_AcceptsASlugOrAnId_ScopedToTheWorld()
    {
        var other = NewWorld();
        Context.Worlds.Add(other);
        var artifact = NewArtifact("The Ashen King");
        var elsewhere = NewArtifact("Elsewhere", other.Id);
        Context.Artifacts.AddRange(artifact, elsewhere);
        await Context.SaveChangesAsync();

        var resolver = new SlugResolver(Context);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await resolver.ResolveAsync<Artifact>(_world.Id, "the-ashen-king"), Is.EqualTo(artifact.Id));
            Assert.That(await resolver.ResolveAsync<Artifact>(_world.Id, "The-Ashen-King"), Is.EqualTo(artifact.Id), "pasted links survive a capital");
            Assert.That(await resolver.ResolveAsync<Artifact>(_world.Id, artifact.Id.ToString()), Is.EqualTo(artifact.Id), "old GUID links keep working");
            Assert.That(await resolver.ResolveAsync<Artifact>(_world.Id, "elsewhere"), Is.Null, "another world's slug is not ours");
            Assert.That(await resolver.ResolveAsync<Source>(_world.Id, "the-ashen-king"), Is.Null, "a slug names one kind");
            Assert.That(await resolver.ResolveAsync<Artifact>(_world.Id, "nobody"), Is.Null);
            Assert.That(await resolver.ResolveAsync<Artifact>(_world.Id, new string('x', 200)), Is.Null);
        });
    }
}
