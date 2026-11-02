using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>What Bibliotaph's Nornis storage provider can list and read (feature 31, Requirement 5).</summary>
[TestFixture]
public class BibliotaphProviderRepositoryTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private BibliotaphProviderRepository _sut = null!;
    private User _dave = null!;
    private User _other = null!;

    [SetUp]
    public async Task SetUp()
    {
        _sut = new BibliotaphProviderRepository(Context);
        _dave = NewUser("dave");
        _other = NewUser("other");
        Context.Users.AddRange(_dave, _other);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    [Test]
    public async Task Worlds_are_the_users_own_never_a_demo_or_template_and_page_by_id()
    {
        var own = Enumerable.Range(0, 5).Select(i => NewWorld($"World {i}", _dave.Id)).ToList();
        Context.Worlds.AddRange(own);
        Context.Worlds.AddRange(
            NewWorld("Tutorial", _dave.Id, demo: true),
            NewWorld("Template", _dave.Id, template: true),
            NewWorld("Someone else's", _other.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var first = await _sut.ListWorldsAsync(_dave.Id, null, 3);
        var second = await _sut.ListWorldsAsync(_dave.Id, first[^1].Id, 3);

        Assert.That(first, Has.Count.EqualTo(3));
        Assert.That(second, Has.Count.EqualTo(2));
        Assert.That(first.Concat(second).Select(w => w.Id), Is.EquivalentTo(own.Select(w => w.Id)));
        Assert.That(await _sut.GetWorldAsync(_dave.Id, own[0].Id), Is.Not.Null);
        Assert.That(await _sut.GetWorldAsync(_other.Id, own[0].Id), Is.Null);
    }

    [Test]
    public async Task Files_are_stored_sourcebooks_others_and_prints_never_handouts_maps_pending_or_bibliotaph_rows()
    {
        var world = NewWorld("Gribbits", _dave.Id);
        Context.Worlds.Add(world);
        var adventure = new Adventure
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Title = "Prep",
            CreatedByUserId = _dave.Id,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        Context.Adventures.Add(adventure);
        var sourcebook = NewDocument(world.Id, LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.Indexed);
        var other = NewDocument(world.Id, LibraryDocumentKind.Other, LibraryDocumentStatus.Stored);
        var print = NewDocument(world.Id, LibraryDocumentKind.Handout, LibraryDocumentStatus.Stored);
        print.AdventureId = adventure.Id;
        var hidden = new[]
        {
            NewDocument(world.Id, LibraryDocumentKind.Handout, LibraryDocumentStatus.Stored),
            NewDocument(world.Id, LibraryDocumentKind.Map, LibraryDocumentStatus.Stored),
            NewDocument(world.Id, LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.PendingUpload),
            NewDocument(world.Id, LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.Stored, LibraryDocumentOrigin.Bibliotaph),
        };
        Context.LibraryDocuments.AddRange([sourcebook, other, print, .. hidden]);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var files = await _sut.ListFilesAsync(world.Id, null, 100);

        Assert.That(files.Select(f => f.Id), Is.EquivalentTo([sourcebook.Id, other.Id, print.Id]));
        Assert.That(await _sut.GetFileAsync(_dave.Id, sourcebook.Id), Is.Not.Null);
        Assert.That(await _sut.GetFileAsync(_dave.Id, hidden[1].Id), Is.Null, "a map is never readable");
        Assert.That(await _sut.GetFileAsync(_other.Id, sourcebook.Id), Is.Null, "another user's world is not theirs to read");
    }

    private LibraryDocument NewDocument(
        Guid worldId, LibraryDocumentKind kind, LibraryDocumentStatus status,
        LibraryDocumentOrigin origin = LibraryDocumentOrigin.Upload) => new()
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Title = $"{kind} file",
            FileName = $"{kind}.pdf",
            ContentType = "application/pdf",
            BlobPath = $"worlds/{worldId}/library/{Guid.NewGuid()}.pdf",
            Kind = kind,
            Visibility = VisibilityScope.GMOnly,
            Status = status,
            Origin = origin,
            BibliotaphEntryId = origin == LibraryDocumentOrigin.Bibliotaph ? Guid.NewGuid().ToString("N") : null,
            UploadedByUserId = _dave.Id,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private static User NewUser(string name) => new()
    {
        Id = Guid.NewGuid(),
        Auth0SubjectId = $"auth0|{name}-{Guid.NewGuid():N}",
        Username = name,
        Email = $"{name}@example.com",
        CreatedAt = Now,
        UpdatedAt = Now,
        RowVersion = [],
    };

    private static World NewWorld(string name, Guid creator, bool demo = false, bool template = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        CreatedAt = Now,
        UpdatedAt = Now,
        CreatedByUserId = creator,
        IsDemo = demo,
        IsTemplate = template,
        RowVersion = [],
    };
}
