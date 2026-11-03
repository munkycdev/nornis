using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>Books from Bibliotaph on the Library (feature 31): read there, never downloaded or deleted here.</summary>
[TestFixture]
public class LibraryServiceBibliotaphTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemoryLibraryDocumentRepository _documents = null!;
    private LibraryService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        _sut = new LibraryService(_documents, new InMemoryLibraryChunkRepository(), new FakeBlobStorageService(),
            new FakeLibraryIndexingQueueClient(), new InMemoryAdventureRepository(), TestPrintDetails.None,
            Options.Create(new LibraryOptions()), NullLogger<LibraryService>.Instance);
    }

    [Test]
    public async Task A_removed_book_is_neither_listed_nor_found()
    {
        var removed = Book();
        removed.RemovedAt = DateTimeOffset.UtcNow;
        var live = Book();
        _documents.Seed(removed, live);

        var list = await _sut.ListAsync(WorldId, WorldRole.GM, CancellationToken.None);
        var found = await _sut.GetByIdAsync(removed.Id, WorldId, WorldRole.GM, CancellationToken.None);

        Assert.That(list.Value!.Select(d => d.Id), Is.EqualTo([live.Id]));
        Assert.That(found.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task A_Bibliotaph_book_is_not_downloaded_deleted_or_reindexed_here()
    {
        var book = Book();
        _documents.Seed(book);

        var download = await _sut.GetDownloadAsync(book.Id, WorldId, WorldRole.GM, CancellationToken.None);
        var delete = await _sut.DeleteAsync(book.Id, WorldId, WorldRole.GM, CancellationToken.None);
        var reindex = await _sut.ReindexAsync(book.Id, WorldId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);

        Assert.That(new[] { download.Error!.Code, delete.Error!.Code, reindex.Error!.Code }, Is.All.EqualTo("in_bibliotaph"));
        Assert.That(_documents.Documents, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_GM_can_still_share_and_retitle_a_Bibliotaph_book()
    {
        var book = Book();
        _documents.Seed(book);

        var shared = await _sut.SetVisibilityAsync(book.Id, WorldId, WorldRole.GM, VisibilityScope.PartyVisible, CancellationToken.None);
        var renamed = await _sut.RenameAsync(book.Id, WorldId, WorldRole.GM, "Our guide", CancellationToken.None);

        Assert.That(shared.Value!.Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
        Assert.That(renamed.Value!.Title, Is.EqualTo("Our guide"));
    }

    private static LibraryDocument Book() => new()
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Origin = LibraryDocumentOrigin.Bibliotaph,
        BibliotaphEntryId = Guid.NewGuid().ToString("N"),
        Title = "Player's Guide",
        RemoteTitle = "Player's Guide",
        Kind = LibraryDocumentKind.Sourcebook,
        Status = LibraryDocumentStatus.Stored,
        Visibility = VisibilityScope.GMOnly,
        ContentType = string.Empty,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
