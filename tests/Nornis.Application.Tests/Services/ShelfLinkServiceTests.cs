using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Shelf links: who may mint and revoke, for whom, and what a code opens. The property worth
/// the most is that a link opens exactly the party shelf — the GM-shelf test is the one that
/// goes red if the service ever acts as anything but a Player.
/// </summary>
[TestFixture]
public class ShelfLinkServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OtherWorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();

    private InMemoryCharacterRepository _characters = null!;
    private InMemoryWorldMemberRepository _members = null!;
    private InMemoryPlayerRepository _players = null!;
    private InMemoryWorldRepository _worlds = null!;
    private InMemoryLibraryDocumentRepository _documents = null!;
    private InMemoryShelfLinkRepository _links = null!;
    private ShelfLinkService _sut = null!;

    private Player _henry = null!;
    private Player _linkedPlayer = null!;
    private LibraryDocument _partyGuide = null!;
    private LibraryDocument _gmBook = null!;

    [SetUp]
    public async Task SetUp()
    {
        _characters = new InMemoryCharacterRepository();
        _members = new InMemoryWorldMemberRepository();
        _players = new InMemoryPlayerRepository(_characters);
        _worlds = new InMemoryWorldRepository(_members);
        _documents = new InMemoryLibraryDocumentRepository();
        _links = new InMemoryShelfLinkRepository(_players, _worlds);

        var library = new LibraryService(
            _documents, new InMemoryLibraryChunkRepository(), new FakeBlobStorageService(),
            new FakeLibraryIndexingQueueClient(), Options.Create(new LibraryOptions()),
            NullLogger<LibraryService>.Instance);
        _sut = new ShelfLinkService(_links, _players, library, new StubInviteCodeGenerator());

        var now = DateTimeOffset.UtcNow;
        await _worlds.CreateAsync(new World { Id = WorldId, Name = "Black Harbor", CreatedByUserId = GmUserId, CreatedAt = now, UpdatedAt = now });
        await _worlds.CreateAsync(new World { Id = OtherWorldId, Name = "Elsewhere", CreatedByUserId = GmUserId, CreatedAt = now, UpdatedAt = now });

        var member = WorldMembership.Create(WorldId, Guid.NewGuid(), WorldRole.Player, now, "Sam");
        await _members.CreateAsync(member);
        _linkedPlayer = await _players.CreateAsync(member.Player!);

        _henry = await _players.CreateAsync(new Player { Id = Guid.NewGuid(), WorldId = WorldId, Name = "Henry", CreatedAt = now, UpdatedAt = now });

        _partyGuide = SeedDocument("Player's Guide", VisibilityScope.PartyVisible, LibraryDocumentStatus.Indexed);
        _gmBook = SeedDocument("Gamemaster's Guide", VisibilityScope.GMOnly, LibraryDocumentStatus.Indexed);
        SeedDocument("Half-uploaded map", VisibilityScope.PartyVisible, LibraryDocumentStatus.PendingUpload);
    }

    private LibraryDocument SeedDocument(string title, VisibilityScope visibility, LibraryDocumentStatus status, Guid? worldId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var document = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Title = title,
            FileName = "book.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            BlobPath = $"worlds/{worldId ?? WorldId}/library/{title}.pdf",
            Kind = LibraryDocumentKind.Sourcebook,
            Visibility = visibility,
            Status = status,
            UploadedByUserId = GmUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _documents.Seed(document);
        return document;
    }

    private async Task<ShelfLink> MintForHenry()
    {
        var result = await _sut.CreateAsync(WorldId, _henry.Id, GmUserId, WorldRole.GM, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        return result.Value!;
    }

    #region Minting

    [Test]
    [Category("Authorization")]
    public async Task Create_AsPlayerOrObserver_Returns403([Values(WorldRole.Player, WorldRole.Observer)] WorldRole role)
    {
        var result = await _sut.CreateAsync(WorldId, _henry.Id, GmUserId, role, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(403));
        Assert.That(_links.Links, Is.Empty);
    }

    [Test]
    public async Task Create_ForLinkedPlayer_Returns409()
    {
        var result = await _sut.CreateAsync(WorldId, _linkedPlayer.Id, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error?.Code, Is.EqualTo("player_linked"));
    }

    [Test]
    public async Task Create_ForUnknownPlayer_Returns404()
    {
        var result = await _sut.CreateAsync(WorldId, Guid.NewGuid(), GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(404));
    }

    [Test]
    [Category("Authorization")]
    public async Task Create_ForAnotherWorldsPlayer_Returns404()
    {
        var result = await _sut.CreateAsync(OtherWorldId, _henry.Id, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(404));
        Assert.That(_links.Links, Is.Empty);
    }

    [Test]
    public async Task Create_RecordsPlayerCodeAndMinter()
    {
        var link = await MintForHenry();

        Assert.Multiple(() =>
        {
            Assert.That(link.PlayerId, Is.EqualTo(_henry.Id));
            Assert.That(link.Code, Is.EqualTo("code-1"));
            Assert.That(link.CreatedByUserId, Is.EqualTo(GmUserId));
            Assert.That(link.IsActive, Is.True);
            Assert.That(link.LastUsedAt, Is.Null);
        });
    }

    [Test]
    public async Task Create_Twice_RotatesTheStandingLink()
    {
        var first = await MintForHenry();
        var second = await MintForHenry();

        var standing = await _sut.ListActiveAsync(WorldId, WorldRole.GM, CancellationToken.None);
        var oldCode = await _sut.OpenAsync(first.Code, CancellationToken.None);
        var newCode = await _sut.OpenAsync(second.Code, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(standing.Value!, Has.Count.EqualTo(1), "one standing link");
            Assert.That(standing.Value![0].Id, Is.EqualTo(second.Id));
            Assert.That(oldCode.IsSuccess, Is.False, "the first link stops opening");
            Assert.That(newCode.IsSuccess, Is.True);
        });
    }

    #endregion

    #region Listing and revoking

    [Test]
    [Category("Authorization")]
    public async Task ListActive_AsPlayer_Returns403()
    {
        await MintForHenry();

        var result = await _sut.ListActiveAsync(WorldId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Revoke_StopsTheLinkOpening_AndIsIdempotent()
    {
        var link = await MintForHenry();

        var first = await _sut.RevokeAsync(WorldId, link.Id, WorldRole.GM, CancellationToken.None);
        var second = await _sut.RevokeAsync(WorldId, link.Id, WorldRole.GM, CancellationToken.None);
        var opened = await _sut.OpenAsync(link.Code, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(opened.Error?.StatusCode, Is.EqualTo(404));
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task Revoke_AsPlayer_Returns403_AndLinkStillOpens()
    {
        var link = await MintForHenry();

        var result = await _sut.RevokeAsync(WorldId, link.Id, WorldRole.Player, CancellationToken.None);
        var opened = await _sut.OpenAsync(link.Code, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(403));
        Assert.That(opened.IsSuccess, Is.True);
    }

    [Test]
    [Category("Authorization")]
    public async Task Revoke_FromAnotherWorld_Returns404_AndLinkStillOpens()
    {
        var link = await MintForHenry();

        var result = await _sut.RevokeAsync(OtherWorldId, link.Id, WorldRole.GM, CancellationToken.None);
        var opened = await _sut.OpenAsync(link.Code, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(404));
        Assert.That(opened.IsSuccess, Is.True);
    }

    #endregion

    #region Opening

    [Test]
    public async Task Open_UnknownAndRevoked_FailIdentically()
    {
        var link = await MintForHenry();
        await _sut.RevokeAsync(WorldId, link.Id, WorldRole.GM, CancellationToken.None);

        var unknown = await _sut.OpenAsync("no-such-code", CancellationToken.None);
        var revoked = await _sut.OpenAsync(link.Code, CancellationToken.None);
        var blank = await _sut.OpenAsync(" ", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(unknown.Error?.StatusCode, Is.EqualTo(404));
            Assert.That(revoked.Error?.StatusCode, Is.EqualTo(404));
            Assert.That(blank.Error?.StatusCode, Is.EqualTo(404));
            Assert.That(revoked.Error?.Code, Is.EqualTo(unknown.Error?.Code));
            Assert.That(revoked.Error?.Message, Is.EqualTo(unknown.Error?.Message));
        });
    }

    [Test]
    public async Task Open_NamesTheWorldAndThePlayer()
    {
        var link = await MintForHenry();

        var shelf = (await _sut.OpenAsync(link.Code, CancellationToken.None)).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(shelf.WorldId, Is.EqualTo(WorldId));
            Assert.That(shelf.WorldName, Is.EqualTo("Black Harbor"));
            Assert.That(shelf.PlayerName, Is.EqualTo("Henry"));
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task Open_ListsThePartyShelf_NotTheGmShelf_NotPendingUploads()
    {
        var link = await MintForHenry();

        var shelf = (await _sut.OpenAsync(link.Code, CancellationToken.None)).Value!;

        Assert.That(shelf.Documents, Has.Count.EqualTo(1), "the party shelf, and only it");
        Assert.That(shelf.Documents[0].Id, Is.EqualTo(_partyGuide.Id));
    }

    [Test]
    public async Task Open_StampsLastUsed()
    {
        var link = await MintForHenry();
        var before = DateTimeOffset.UtcNow;

        await _sut.OpenAsync(link.Code, CancellationToken.None);

        var stored = _links.Links.Single(l => l.Id == link.Id);
        Assert.That(stored.LastUsedAt, Is.Not.Null.And.GreaterThanOrEqualTo(before));
    }

    #endregion

    #region Downloading

    [Test]
    public async Task Download_PartyDocument_ReturnsReadUrl()
    {
        var link = await MintForHenry();

        var result = await _sut.DownloadAsync(link.Code, _partyGuide.Id, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.DownloadUrl, Does.Contain("sas=download"));
        Assert.That(result.Value.FileName, Is.EqualTo("book.pdf"));
    }

    [Test]
    [Category("Authorization")]
    public async Task Download_GmShelfDocument_Returns404()
    {
        var link = await MintForHenry();

        var result = await _sut.DownloadAsync(link.Code, _gmBook.Id, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(404));
    }

    [Test]
    [Category("Authorization")]
    public async Task Download_AnotherWorldsPartyDocument_Returns404()
    {
        var link = await MintForHenry();
        var elsewhere = SeedDocument("Elsewhere's Guide", VisibilityScope.PartyVisible, LibraryDocumentStatus.Indexed, OtherWorldId);

        var result = await _sut.DownloadAsync(link.Code, elsewhere.Id, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Download_RevokedLink_Returns404()
    {
        var link = await MintForHenry();
        await _sut.RevokeAsync(WorldId, link.Id, WorldRole.GM, CancellationToken.None);

        var result = await _sut.DownloadAsync(link.Code, _partyGuide.Id, CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(404));
    }

    #endregion
}
