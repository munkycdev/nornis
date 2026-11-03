using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The Library's organisation (feature 36): every document's category, which ones sit on the
/// Shelf, and the GM filing an upload under an Adventure or changing what it is.
/// </summary>
[TestFixture]
public class LibraryOrganisationTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OtherWorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();

    private InMemoryLibraryDocumentRepository _documents = null!;
    private InMemoryAdventureRepository _adventures = null!;
    private LibraryService _sut = null!;
    private Adventure _spa = null!;
    private Adventure _elsewhere = null!;

    [SetUp]
    public async Task SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        _adventures = new InMemoryAdventureRepository();
        _sut = new LibraryService(_documents, new InMemoryLibraryChunkRepository(), new FakeBlobStorageService(),
            new FakeLibraryIndexingQueueClient(), _adventures, TestBooklets.Printer(TestPrintDetails.None),
            Options.Create(new LibraryOptions()), NullLogger<LibraryService>.Instance);
        _spa = await _adventures.CreateAsync(Adventure(WorldId, "The Slurping Spa"));
        _elsewhere = await _adventures.CreateAsync(Adventure(OtherWorldId, "Somewhere else"));
    }

    private static Adventure Adventure(Guid worldId, string title) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = worldId,
        Title = title,
        CreatedByUserId = GmId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static LibraryDocument Doc(
        LibraryDocumentKind kind = LibraryDocumentKind.Handout,
        LibraryDocumentOrigin origin = LibraryDocumentOrigin.Upload,
        Guid? printOf = null,
        PrintDetails? printDetails = null,
        Guid? linkedTo = null) => new()
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Title = "A document",
            FileName = "file.png",
            ContentType = "image/png",
            SizeBytes = 100,
            BlobPath = "worlds/x/library/y/file.png",
            Kind = kind,
            Origin = origin,
            AdventureId = printOf,
            PrintDetails = printDetails,
            LinkedAdventureId = linkedTo,
            Visibility = VisibilityScope.GMOnly,
            Status = LibraryDocumentStatus.Stored,
            UploadedByUserId = GmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

    private static PrintDetails Details() => PrintDetails.Resolve("The Slurping Spa", new AdventureDetails(), null, "kelda");

    // --------------------------------------------------------------- Categories --

    [TestCase(LibraryDocumentKind.Sourcebook, LibraryDocumentCategory.Book)]
    [TestCase(LibraryDocumentKind.Map, LibraryDocumentCategory.Map)]
    [TestCase(LibraryDocumentKind.Handout, LibraryDocumentCategory.Handout)]
    [TestCase(LibraryDocumentKind.Other, LibraryDocumentCategory.Other)]
    public void AnUploadsCategory_IsItsKind(LibraryDocumentKind kind, LibraryDocumentCategory category) =>
        Assert.That(LibraryCategories.Of(Doc(kind)), Is.EqualTo(category));

    [Test]
    public void APrint_IsAPrint_WhateverItsKind_EvenAfterItsAdventureIsGone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LibraryCategories.Of(Doc(LibraryDocumentKind.Handout, printOf: Guid.NewGuid())), Is.EqualTo(LibraryDocumentCategory.Print));
            Assert.That(LibraryCategories.Of(Doc(LibraryDocumentKind.Other, printDetails: Details())), Is.EqualTo(LibraryDocumentCategory.Print),
                "a deleted Adventure lets go of its prints, which keep their snapshot");
        });
    }

    [Test]
    public void ABibliotaphRow_IsABook() =>
        Assert.That(LibraryCategories.Of(Doc(LibraryDocumentKind.Other, LibraryDocumentOrigin.Bibliotaph)), Is.EqualTo(LibraryDocumentCategory.Book));

    [Test]
    public void TheShelf_HoldsEverythingButUploadsThatBelongToAnAdventure()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LibraryCategories.IsOnShelf(Doc()), Is.True);
            Assert.That(LibraryCategories.IsOnShelf(Doc(linkedTo: Guid.NewGuid())), Is.False);
            Assert.That(LibraryCategories.IsOnShelf(Doc(printOf: Guid.NewGuid())), Is.True, "prints are the world's publications");
        });
    }

    // ------------------------------------------------------------------- Linking --

    [Test]
    public async Task TheGm_FilesAnUploadUnderAnAdventure_AndBackOnTheShelf()
    {
        var doc = Doc();
        _documents.Seed(doc);

        var linked = (await _sut.SetAdventureLinkAsync(doc.Id, WorldId, WorldRole.GM, _spa.Id, CancellationToken.None)).Value!.LinkedAdventureId;
        var stored = _documents.Documents.Single().LinkedAdventureId;
        var unlinked = await _sut.SetAdventureLinkAsync(doc.Id, WorldId, WorldRole.GM, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(linked, Is.EqualTo(_spa.Id));
            Assert.That(stored, Is.EqualTo(_spa.Id));
            Assert.That(unlinked.Value!.LinkedAdventureId, Is.Null);
            Assert.That(LibraryCategories.IsOnShelf(_documents.Documents.Single()), Is.True);
        });
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    [Category("Authorization")]
    public async Task OnlyTheGm_Links(WorldRole role)
    {
        var doc = Doc();
        doc.Visibility = VisibilityScope.PartyVisible;
        _documents.Seed(doc);

        var result = await _sut.SetAdventureLinkAsync(doc.Id, WorldId, role, _spa.Id, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_documents.Documents.Single().LinkedAdventureId, Is.Null);
    }

    [Test]
    public async Task APrint_OrABibliotaphBook_IsNotTheGmsToFile()
    {
        var print = Doc(printOf: _spa.Id);
        var book = Doc(origin: LibraryDocumentOrigin.Bibliotaph);
        _documents.Seed(print);
        _documents.Seed(book);

        var printResult = await _sut.SetAdventureLinkAsync(print.Id, WorldId, WorldRole.GM, _spa.Id, CancellationToken.None);
        var bookResult = await _sut.SetAdventureLinkAsync(book.Id, WorldId, WorldRole.GM, _spa.Id, CancellationToken.None);
        var printKind = await _sut.SetKindAsync(print.Id, WorldId, WorldRole.GM, LibraryDocumentKind.Map, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(printResult.Error!.Code, Is.EqualTo("prints_belong_to_their_adventure"));
            Assert.That(bookResult.Error!.Code, Is.EqualTo("books_stay_on_the_shelf"));
            Assert.That(printKind.Error!.Code, Is.EqualTo("prints_belong_to_their_adventure"));
            Assert.That(_documents.Documents.Select(d => d.LinkedAdventureId), Is.All.Null);
        });
    }

    [Test]
    public async Task AnAdventureInAnotherWorld_OrNoneAtAll_IsNotFound()
    {
        var doc = Doc();
        _documents.Seed(doc);

        var other = await _sut.SetAdventureLinkAsync(doc.Id, WorldId, WorldRole.GM, _elsewhere.Id, CancellationToken.None);
        var none = await _sut.SetAdventureLinkAsync(doc.Id, WorldId, WorldRole.GM, Guid.NewGuid(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(other.Error!.Code, Is.EqualTo("adventure_not_found"));
            Assert.That(none.Error!.Code, Is.EqualTo("adventure_not_found"));
            Assert.That(_documents.Documents.Single().LinkedAdventureId, Is.Null);
        });
    }

    [Test]
    public async Task LinkingToWhereItAlreadyIs_ChangesNothing()
    {
        var doc = Doc(linkedTo: _spa.Id);
        var before = doc.UpdatedAt;
        _documents.Seed(doc);

        await _sut.SetAdventureLinkAsync(doc.Id, WorldId, WorldRole.GM, _spa.Id, CancellationToken.None);

        Assert.That(_documents.Documents.Single().UpdatedAt, Is.EqualTo(before));
    }

    [Test]
    public async Task AnUpload_CanBelongToAnAdventureFromTheStart_ButOnlyAGmsAndOnlyInItsWorld()
    {
        RequestLibraryUploadCommand Upload(WorldRole role, Guid adventureId) =>
            new(WorldId, GmId, role, "Spa map", "map.png", "image/png", 1024, LibraryDocumentKind.Map,
                VisibilityScope.GMOnly, LinkedAdventureId: adventureId);

        var gm = await _sut.RequestUploadAsync(Upload(WorldRole.GM, _spa.Id), CancellationToken.None);
        var player = await _sut.RequestUploadAsync(Upload(WorldRole.Player, _spa.Id), CancellationToken.None);
        var elsewhere = await _sut.RequestUploadAsync(Upload(WorldRole.GM, _elsewhere.Id), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(gm.Value!.Document.LinkedAdventureId, Is.EqualTo(_spa.Id));
            Assert.That(player.Error!.StatusCode, Is.EqualTo(403));
            Assert.That(elsewhere.Error!.Code, Is.EqualTo("adventure_not_found"));
            Assert.That(_documents.Documents, Has.Count.EqualTo(1));
        });
    }

    // ---------------------------------------------------------------------- Kind --

    [Test]
    public async Task TheGm_ChangesWhatAnUploadIs()
    {
        var doc = Doc(LibraryDocumentKind.Other);
        _documents.Seed(doc);

        var result = await _sut.SetKindAsync(doc.Id, WorldId, WorldRole.GM, LibraryDocumentKind.Map, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Kind, Is.EqualTo(LibraryDocumentKind.Map));
            Assert.That(LibraryCategories.Of(_documents.Documents.Single()), Is.EqualTo(LibraryDocumentCategory.Map));
        });
    }

    [Test]
    public async Task AKindThatIsntOne_IsRefused()
    {
        var doc = Doc();
        _documents.Seed(doc);

        var result = await _sut.SetKindAsync(doc.Id, WorldId, WorldRole.GM, (LibraryDocumentKind)42, CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("invalid_kind"));
    }

    [Test]
    [Category("Authorization")]
    public async Task OnlyTheGm_ChangesAKind()
    {
        var doc = Doc();
        doc.Visibility = VisibilityScope.PartyVisible;
        _documents.Seed(doc);

        var result = await _sut.SetKindAsync(doc.Id, WorldId, WorldRole.Player, LibraryDocumentKind.Map, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }
}
