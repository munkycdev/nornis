using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Bibliotaph;

/// <summary>Players read shared books (feature 31, Requirement 4).</summary>
[TestFixture]
public class BibliotaphReadingServiceTests
{
    private const string Origin = "https://nornis.example";
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();

    private InMemoryLibraryDocumentRepository _documents = null!;
    private IShelfLinkService _shelf = null!;
    private IBibliotaphClient _client = null!;
    private ILibraryReadingRecordRepository _records = null!;
    private BibliotaphReadingService _sut = null!;
    private readonly User _player = new() { Id = Guid.NewGuid(), Username = "Mira" };

    [SetUp]
    public void SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        var library = new LibraryService(_documents, new InMemoryLibraryChunkRepository(), new FakeBlobStorageService(),
            new FakeLibraryIndexingQueueClient(), new InMemoryAdventureRepository(), TestBooklets.Printer(TestPrintDetails.None),
            Options.Create(new LibraryOptions()), NullLogger<LibraryService>.Instance);
        _shelf = Substitute.For<IShelfLinkService>();
        var owners = Substitute.For<IBibliotaphConnectionResolver>();
        owners.ForWorldAsync(WorldId, Arg.Any<CancellationToken>()).Returns(new BibliotaphWorldOwner(
            WorldId, Creator, "Dave", new BibliotaphConnection { UserId = Creator }));
        _client = Substitute.For<IBibliotaphClient>();
        _client.MintReadingTicketAsync(default, default, default!, default).ReturnsForAnyArgs(
            BibliotaphResult<BibliotaphTicket>.Success(new BibliotaphTicket("https://bibliotaph.example/table/abc", DateTimeOffset.UtcNow)));
        _records = Substitute.For<ILibraryReadingRecordRepository>();
        _sut = new BibliotaphReadingService(library, _shelf, owners, _client, _records,
            Options.Create(new BibliotaphOptions { WebOrigin = Origin }), TimeProvider.System,
            NullLogger<BibliotaphReadingService>.Instance);
    }

    [Test]
    public async Task A_player_opens_a_shared_book_through_a_recorded_ticket_that_renews_here()
    {
        var book = Book(VisibilityScope.PartyVisible);
        _documents.Seed(book);
        BibliotaphTicketRequest? sent = null;
        _client.MintReadingTicketAsync(Creator, WorldId, Arg.Do<BibliotaphTicketRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(BibliotaphResult<BibliotaphTicket>.Success(new BibliotaphTicket("https://bibliotaph.example/table/abc", DateTimeOffset.UtcNow)));

        var result = await _sut.OpenForMemberAsync(WorldId, book.Id, _player, WorldRole.Player, "Mira", 41, CancellationToken.None);

        Assert.That(result.Value, Is.EqualTo(new LibraryReadLink("https://bibliotaph.example/table/abc", LibraryReadLink.Reader)));
        Assert.That(sent, Is.EqualTo(new BibliotaphTicketRequest("doc-1", 41, "Mira", $"{Origin}/library/players-guide/read")));
        await _records.Received(1).AddAsync(
            Arg.Is<LibraryReadingRecord>(r => r.UserId == _player.Id && r.PlayerId == null && r.LibraryDocumentId == book.Id && r.ViewerName == "Mira"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task A_player_cannot_open_a_GMOnly_or_removed_book()
    {
        var secret = Book(VisibilityScope.GMOnly);
        var removed = Book(VisibilityScope.PartyVisible);
        removed.RemovedAt = DateTimeOffset.UtcNow;
        _documents.Seed(secret, removed);

        var a = await _sut.OpenForMemberAsync(WorldId, secret.Id, _player, WorldRole.Player, "Mira", null, CancellationToken.None);
        var b = await _sut.OpenForMemberAsync(WorldId, removed.Id, _player, WorldRole.Player, "Mira", null, CancellationToken.None);

        Assert.That(new[] { a.Error!.StatusCode, b.Error!.StatusCode }, Is.All.EqualTo(404));
        await _client.DidNotReceiveWithAnyArgs().MintReadingTicketAsync(default, default, default!, default);
    }

    [Test]
    public async Task A_shelf_link_opens_its_shared_book_and_records_the_player()
    {
        var book = Book(VisibilityScope.PartyVisible);
        var playerId = Guid.NewGuid();
        _shelf.GetDocumentAsync("code-1", book.Id, Arg.Any<CancellationToken>())
            .Returns(AppResult<ShelfDocument>.Success(new ShelfDocument(WorldId, playerId, "Tam", book)));
        BibliotaphTicketRequest? sent = null;
        _client.MintReadingTicketAsync(Creator, WorldId, Arg.Do<BibliotaphTicketRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(BibliotaphResult<BibliotaphTicket>.Success(new BibliotaphTicket("https://bibliotaph.example/table/x", DateTimeOffset.UtcNow)));

        var result = await _sut.OpenForShelfAsync("code-1", book.Id, null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(sent!.Renew, Is.EqualTo($"{Origin}/shelf/code-1/read/{book.Id}"));
        Assert.That(sent.ViewerName, Is.EqualTo("Tam"));
        await _records.Received(1).AddAsync(
            Arg.Is<LibraryReadingRecord>(r => r.PlayerId == playerId && r.UserId == null), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task A_shelf_link_cannot_open_what_the_shelf_does_not_share()
    {
        _shelf.GetDocumentAsync("code-1", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<ShelfDocument>.Fail(new AppError(404, "not_found", "Library document not found.")));

        var result = await _sut.OpenForShelfAsync("code-1", Guid.NewGuid(), null, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        await _records.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Test]
    public async Task An_uncatalogued_upload_is_a_download()
    {
        var upload = Book(VisibilityScope.PartyVisible);
        upload.Origin = LibraryDocumentOrigin.Upload;
        upload.BibliotaphEntryId = null;
        upload.BibliotaphDocumentId = null;
        upload.BlobPath = "worlds/x/library/y/book.pdf";
        _documents.Seed(upload);

        var result = await _sut.OpenForMemberAsync(WorldId, upload.Id, _player, WorldRole.Player, "Mira", null, CancellationToken.None);

        Assert.That(result.Value!.Kind, Is.EqualTo(LibraryReadLink.Download));
        await _client.DidNotReceiveWithAnyArgs().MintReadingTicketAsync(default, default, default!, default);
    }

    [Test]
    public async Task An_opening_library_is_a_503_and_nothing_is_recorded()
    {
        var book = Book(VisibilityScope.PartyVisible);
        _documents.Seed(book);
        _client.MintReadingTicketAsync(default, default, default!, default).ReturnsForAnyArgs(
            BibliotaphResult<BibliotaphTicket>.Fail(BibliotaphFailure.Opening));

        var result = await _sut.OpenForMemberAsync(WorldId, book.Id, _player, WorldRole.Player, "Mira", null, CancellationToken.None);

        Assert.That((result.Error!.StatusCode, result.Error.Code), Is.EqualTo((503, "bibliotaph_opening")));
        await _records.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Test]
    public async Task Only_a_GM_sees_who_opened_a_book()
    {
        var book = Book(VisibilityScope.PartyVisible);
        _documents.Seed(book);
        _records.ListRecentAsync(book.Id, BibliotaphReadingService.OpensShown, Arg.Any<CancellationToken>()).Returns(
            [new LibraryReadingRecord { ViewerName = "Tam", PlayerId = Guid.NewGuid(), OpenedAt = DateTimeOffset.UtcNow }]);

        var gm = await _sut.ListOpensAsync(WorldId, book.Id, WorldRole.GM, CancellationToken.None);
        var player = await _sut.ListOpensAsync(WorldId, book.Id, WorldRole.Player, CancellationToken.None);

        Assert.That(gm.Value!.Single().FromShelfLink, Is.True);
        Assert.That(player.Error!.StatusCode, Is.EqualTo(403));
    }

    private static LibraryDocument Book(VisibilityScope visibility) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Slug = "players-guide",
        Origin = LibraryDocumentOrigin.Bibliotaph,
        BibliotaphEntryId = "entry-1",
        BibliotaphDocumentId = "doc-1",
        Title = "Player's Guide",
        Kind = LibraryDocumentKind.Sourcebook,
        Status = LibraryDocumentStatus.Stored,
        Visibility = visibility,
        ContentType = "application/pdf",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
