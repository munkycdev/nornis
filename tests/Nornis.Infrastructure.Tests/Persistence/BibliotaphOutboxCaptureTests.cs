using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// What reaches the Bibliotaph outbox (feature 31, Requirement 2), through the repositories'
/// real save paths: detached updates, the confirm, and the two ExecuteDelete paths.
/// </summary>
[TestFixture]
public class BibliotaphOutboxCaptureTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private User _connected = null!;
    private User _unconnected = null!;

    [SetUp]
    public async Task SetUp()
    {
        Context.BibliotaphWorldEvents.RemoveRange(await Context.BibliotaphWorldEvents.ToListAsync());
        await Context.SaveChangesAsync();

        _connected = NewUser("dave");
        _unconnected = NewUser("other");
        Context.Users.AddRange(_connected, _unconnected);
        Context.BibliotaphConnections.Add(new BibliotaphConnection
        {
            Id = Guid.NewGuid(),
            UserId = _connected.Id,
            ProtectedRefreshToken = "protected",
            Status = BibliotaphConnectionStatus.Connected,
            ConnectedAt = Now,
            StatusChangedAt = Now,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    [Test]
    public async Task Creating_a_world_queues_created_in_the_same_save()
    {
        var world = await new WorldRepository(Context).CreateAsync(NewWorld("Gribbits", _connected.Id));

        var e = (await EventsAsync()).Single();
        Assert.That((e.Kind, e.WorldId, e.WorldName, e.UserId),
            Is.EqualTo((BibliotaphWorldEventKind.Created, world.Id, "Gribbits", _connected.Id)));
    }

    [Test]
    public async Task Demo_template_and_unconnected_worlds_queue_nothing()
    {
        var worlds = new WorldRepository(Context);
        await worlds.CreateAsync(NewWorld("Tutorial", _connected.Id, demo: true));
        await worlds.CreateAsync(NewWorld("Template", _connected.Id, template: true));
        await worlds.CreateAsync(NewWorld("Not connected", _unconnected.Id));

        Assert.That(await EventsAsync(), Is.Empty);
    }

    [Test]
    public async Task Renaming_a_detached_world_queues_renamed_but_another_setting_does_not()
    {
        var world = await SeedWorldAsync("Gribbits");

        // Settings save a detached entity, so the tracker marks every property modified.
        world.Description = "Detectives";
        await new WorldRepository(Context).UpdateAsync(world);
        Assert.That(await EventsAsync(), Is.Empty);

        world.Name = "Gribbits Detective Agency";
        await new WorldRepository(Context).UpdateAsync(world);

        var e = (await EventsAsync()).Single();
        Assert.That((e.Kind, e.WorldName), Is.EqualTo((BibliotaphWorldEventKind.Renamed, "Gribbits Detective Agency")));
    }

    [Test]
    public async Task Deleting_a_world_queues_deleted_and_the_event_outlives_the_world()
    {
        var world = await SeedWorldAsync("Gribbits");

        await new WorldRepository(Context).DeleteAsync(world.Id);

        Assert.That(await Context.Worlds.AnyAsync(w => w.Id == world.Id), Is.False);
        var e = (await EventsAsync()).Single();
        Assert.That((e.Kind, e.WorldId, e.WorldName), Is.EqualTo((BibliotaphWorldEventKind.Deleted, world.Id, "Gribbits")));
    }

    [Test]
    public async Task Confirming_an_exposed_upload_queues_files_changed_once_and_indexing_queues_nothing()
    {
        var world = await SeedWorldAsync("Gribbits");
        var documents = new LibraryDocumentRepository(Context);
        var upload = NewDocument(world.Id, LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.PendingUpload);
        await documents.CreateAsync(upload);
        Context.ChangeTracker.Clear();
        Assert.That(await EventsAsync(), Is.Empty, "a pending upload isn't listed yet");

        upload.Status = LibraryDocumentStatus.Stored;
        await documents.UpdateAsync(upload);
        upload.Status = LibraryDocumentStatus.Indexed;
        await documents.UpdateAsync(upload);

        var e = (await EventsAsync()).Single();
        Assert.That((e.Kind, e.WorldId), Is.EqualTo((BibliotaphWorldEventKind.FilesChanged, world.Id)));
    }

    [Test]
    public async Task A_filed_print_queues_files_changed()
    {
        var world = await SeedWorldAsync("Gribbits");
        var adventure = await SeedAdventureAsync(world.Id);

        var print = NewDocument(world.Id, LibraryDocumentKind.Handout, LibraryDocumentStatus.Stored);
        print.AdventureId = adventure.Id;
        await new LibraryDocumentRepository(Context).CreateAsync(print);

        Assert.That((await EventsAsync()).Single().Kind, Is.EqualTo(BibliotaphWorldEventKind.FilesChanged));
    }

    [Test]
    public async Task Maps_and_handouts_queue_nothing()
    {
        var world = await SeedWorldAsync("Gribbits");
        var documents = new LibraryDocumentRepository(Context);

        await documents.CreateAsync(NewDocument(world.Id, LibraryDocumentKind.Map, LibraryDocumentStatus.Stored));
        await documents.CreateAsync(NewDocument(world.Id, LibraryDocumentKind.Handout, LibraryDocumentStatus.Stored));

        Assert.That(await EventsAsync(), Is.Empty);
    }

    [Test]
    public async Task Deleting_an_exposed_upload_queues_files_changed_and_a_map_does_not()
    {
        var world = await SeedWorldAsync("Gribbits");
        var book = NewDocument(world.Id, LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.Indexed);
        var map = NewDocument(world.Id, LibraryDocumentKind.Map, LibraryDocumentStatus.Stored);
        Context.LibraryDocuments.AddRange(book, map);
        await SaveSeedAsync();

        var documents = new LibraryDocumentRepository(Context);
        await documents.DeleteAsync(map.Id);
        Assert.That(await EventsAsync(), Is.Empty);

        await documents.DeleteAsync(book.Id);
        Assert.That((await EventsAsync()).Single().Kind, Is.EqualTo(BibliotaphWorldEventKind.FilesChanged));
    }

    [Test]
    public async Task Deleting_an_adventure_with_a_print_queues_files_changed()
    {
        var world = await SeedWorldAsync("Gribbits");
        var adventure = await SeedAdventureAsync(world.Id);
        var print = NewDocument(world.Id, LibraryDocumentKind.Handout, LibraryDocumentStatus.Stored);
        print.AdventureId = adventure.Id;
        Context.LibraryDocuments.Add(print);
        await SaveSeedAsync();

        await new AdventureRepository(Context).DeleteAsync(adventure.Id);

        Assert.That((await EventsAsync()).Single().Kind, Is.EqualTo(BibliotaphWorldEventKind.FilesChanged));
    }

    private async Task<List<BibliotaphWorldEvent>> EventsAsync() =>
        await Context.BibliotaphWorldEvents.AsNoTracking().ToListAsync();

    /// <summary>Saves seed rows and then clears whatever the capture queued for them, so a test sees only its own step.</summary>
    private async Task SaveSeedAsync()
    {
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        Context.BibliotaphWorldEvents.RemoveRange(await Context.BibliotaphWorldEvents.ToListAsync());
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    private async Task<World> SeedWorldAsync(string name)
    {
        var world = NewWorld(name, _connected.Id);
        Context.Worlds.Add(world);
        await SaveSeedAsync();
        return world;
    }

    private async Task<Adventure> SeedAdventureAsync(Guid worldId)
    {
        var adventure = new Adventure
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Title = "The Missing Gribbit",
            CreatedByUserId = _connected.Id,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        Context.Adventures.Add(adventure);
        await SaveSeedAsync();
        return adventure;
    }

    private LibraryDocument NewDocument(Guid worldId, LibraryDocumentKind kind, LibraryDocumentStatus status) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = worldId,
        Title = $"{kind} file",
        FileName = $"{kind}.pdf",
        ContentType = "application/pdf",
        BlobPath = $"worlds/{worldId}/library/{kind}.pdf",
        Kind = kind,
        Visibility = VisibilityScope.GMOnly,
        Status = status,
        UploadedByUserId = _connected.Id,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static User NewUser(string name) => new()
    {
        Id = Guid.NewGuid(),
        SignIns = { new UserSignIn { Subject = $"auth0|{name}-{Guid.NewGuid():N}" } },
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
