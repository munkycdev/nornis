using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class LibraryServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();
    private static readonly Guid PlayerId = Guid.NewGuid();

    private InMemoryLibraryDocumentRepository _documents = null!;
    private InMemoryLibraryChunkRepository _chunks = null!;
    private FakeBlobStorageService _blobs = null!;
    private FakeLibraryIndexingQueueClient _queue = null!;
    private LibraryService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        _chunks = new InMemoryLibraryChunkRepository();
        _blobs = new FakeBlobStorageService();
        _queue = new FakeLibraryIndexingQueueClient();
        _sut = new LibraryService(_documents, _chunks, _blobs, _queue,
            Options.Create(new LibraryOptions()), NullLogger<LibraryService>.Instance);
    }

    private static RequestLibraryUploadCommand Command(
        WorldRole role = WorldRole.GM,
        string fileName = "book.pdf",
        string contentType = "application/pdf",
        long size = 1024,
        VisibilityScope visibility = VisibilityScope.GMOnly,
        string title = "Sourcebook") =>
        new(WorldId, role == WorldRole.GM ? GmId : PlayerId, role, title, fileName, contentType, size,
            LibraryDocumentKind.Sourcebook, visibility);

    [Test]
    public async Task RequestUpload_Gm_CreatesPendingRowAndTicket()
    {
        var result = await _sut.RequestUploadAsync(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var ticket = result.Value!;
        Assert.That(ticket.Document.Status, Is.EqualTo(LibraryDocumentStatus.PendingUpload));
        Assert.That(ticket.Document.BlobPath, Does.Contain(ticket.Document.Id.ToString()));
        Assert.That(ticket.UploadUrl, Does.Contain("sas=upload"));
    }

    [Test]
    public async Task RequestUpload_Observer_Returns403()
    {
        var result = await _sut.RequestUploadAsync(Command(role: WorldRole.Observer), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [TestCase("book.exe")]
    [TestCase("noextension")]
    public async Task RequestUpload_DisallowedExtension_Returns400(string fileName)
    {
        var result = await _sut.RequestUploadAsync(Command(fileName: fileName), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("unsupported_file_type"));
    }

    [Test]
    public async Task RequestUpload_OversizedFile_Returns400()
    {
        var result = await _sut.RequestUploadAsync(Command(size: 300L * 1024 * 1024), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task RequestUpload_PlayerRequestingGmOnly_IsClampedToPartyVisible()
    {
        var result = await _sut.RequestUploadAsync(
            Command(role: WorldRole.Player, visibility: VisibilityScope.GMOnly), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Document.Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
    }

    [Test]
    public async Task ConfirmUpload_PdfArrived_QueuesIndexing()
    {
        var ticket = (await _sut.RequestUploadAsync(Command(), CancellationToken.None)).Value!;
        _blobs.Blobs[ticket.Document.BlobPath] = (new byte[2048], "application/pdf");

        var result = await _sut.ConfirmUploadAsync(ticket.Document.Id, WorldId, GmId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(LibraryDocumentStatus.Indexing));
        Assert.That(result.Value.SizeBytes, Is.EqualTo(2048), "size comes from the blob, not the client claim");
        Assert.That(_queue.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ConfirmUpload_ImageArrived_StoredWithoutIndexing()
    {
        var ticket = (await _sut.RequestUploadAsync(Command(fileName: "map.png", contentType: "image/png"), CancellationToken.None)).Value!;
        _blobs.Blobs[ticket.Document.BlobPath] = (new byte[64], "image/png");

        var result = await _sut.ConfirmUploadAsync(ticket.Document.Id, WorldId, GmId, CancellationToken.None);

        Assert.That(result.Value!.Status, Is.EqualTo(LibraryDocumentStatus.Stored));
        Assert.That(_queue.Sent, Is.Empty);
    }

    [Test]
    public async Task ConfirmUpload_BlobNeverArrived_Returns400()
    {
        var ticket = (await _sut.RequestUploadAsync(Command(), CancellationToken.None)).Value!;

        var result = await _sut.ConfirmUploadAsync(ticket.Document.Id, WorldId, GmId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("upload_not_found"));
    }

    [Test]
    public async Task ConfirmUpload_EnqueueFails_FallsBackToStoredWith502()
    {
        var ticket = (await _sut.RequestUploadAsync(Command(), CancellationToken.None)).Value!;
        _blobs.Blobs[ticket.Document.BlobPath] = (new byte[10], "application/pdf");
        _queue.ThrowOnSend = new InvalidOperationException("bus down");

        var result = await _sut.ConfirmUploadAsync(ticket.Document.Id, WorldId, GmId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("enqueue_failed"));
        Assert.That(_documents.Documents.Single().Status, Is.EqualTo(LibraryDocumentStatus.Stored));
    }

    [Test]
    public async Task List_Player_ExcludesGmOnlyAndPending()
    {
        _documents.Seed(
            Doc(VisibilityScope.PartyVisible, LibraryDocumentStatus.Indexed, "Party book"),
            Doc(VisibilityScope.GMOnly, LibraryDocumentStatus.Indexed, "GM module"),
            Doc(VisibilityScope.PartyVisible, LibraryDocumentStatus.PendingUpload, "Half-finished dialog"));

        var result = await _sut.ListAsync(WorldId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.Value!.Select(d => d.Title), Is.EquivalentTo(new[] { "Party book" }));
    }

    [Test]
    public async Task GetById_GmOnlyDoc_AsPlayer_Returns404()
    {
        var doc = Doc(VisibilityScope.GMOnly, LibraryDocumentStatus.Indexed, "GM module");
        _documents.Seed(doc);

        var result = await _sut.GetByIdAsync(doc.Id, WorldId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Delete_PlayerWhoIsNotUploader_Returns403()
    {
        var doc = Doc(VisibilityScope.PartyVisible, LibraryDocumentStatus.Indexed, "Party book", uploadedBy: GmId);
        _documents.Seed(doc);

        var result = await _sut.DeleteAsync(doc.Id, WorldId, PlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Delete_Uploader_RemovesBlobAndRow()
    {
        var doc = Doc(VisibilityScope.PartyVisible, LibraryDocumentStatus.Indexed, "My handout", uploadedBy: PlayerId);
        _documents.Seed(doc);

        var result = await _sut.DeleteAsync(doc.Id, WorldId, PlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_blobs.DeletedPaths, Does.Contain(doc.BlobPath));
        Assert.That(_documents.Documents, Is.Empty);
    }

    [Test]
    public async Task Delete_BlobFailureIsSwallowed_RowStillRemoved()
    {
        var doc = Doc(VisibilityScope.PartyVisible, LibraryDocumentStatus.Indexed, "Party book", uploadedBy: GmId);
        _documents.Seed(doc);
        _blobs.FailDeletes = true;

        var result = await _sut.DeleteAsync(doc.Id, WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_documents.Documents, Is.Empty);
    }

    [Test]
    public async Task Reindex_NonPdf_Returns400()
    {
        var doc = Doc(VisibilityScope.PartyVisible, LibraryDocumentStatus.Stored, "Map", contentType: "image/png");
        _documents.Seed(doc);

        var result = await _sut.ReindexAsync(doc.Id, WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("not_indexable"));
    }

    [Test]
    public async Task Reindex_FailedPdf_QueuesAndResetsError()
    {
        var doc = Doc(VisibilityScope.PartyVisible, LibraryDocumentStatus.IndexFailed, "Book");
        doc.ErrorMessage = "boom";
        _documents.Seed(doc);

        var result = await _sut.ReindexAsync(doc.Id, WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Status, Is.EqualTo(LibraryDocumentStatus.Indexing));
        Assert.That(result.Value.ErrorMessage, Is.Null);
        Assert.That(_queue.Sent, Has.Count.EqualTo(1));
    }

    private static LibraryDocument Doc(
        VisibilityScope visibility,
        LibraryDocumentStatus status,
        string title,
        Guid? uploadedBy = null,
        string contentType = "application/pdf") =>
        new()
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Title = title,
            FileName = "file" + (contentType == "application/pdf" ? ".pdf" : ".png"),
            ContentType = contentType,
            SizeBytes = 100,
            BlobPath = $"worlds/{WorldId}/library/{Guid.NewGuid()}/file",
            Kind = LibraryDocumentKind.Sourcebook,
            Visibility = visibility,
            Status = status,
            UploadedByUserId = uploadedBy ?? GmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
}
