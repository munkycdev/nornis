using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class SourceAttachmentServiceTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemorySourceAttachmentRepository _attachmentRepository = null!;
    private FakeBlobStorageService _blobStorage = null!;
    private SourceAttachmentService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _attachmentRepository = new InMemorySourceAttachmentRepository();
        _blobStorage = new FakeBlobStorageService();
        _sut = new SourceAttachmentService(
            _sourceRepository,
            _attachmentRepository,
            _blobStorage,
            NullLogger<SourceAttachmentService>.Instance);
    }

    private Source SeedSource(SourceProcessingStatus status = SourceProcessingStatus.Draft)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.HandwrittenNotes,
            Title = "Session sketchbook",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = status,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = OwnerId
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private RequestSourceAttachmentUploadCommand PageCommand(
        Guid sourceId, string fileName = "page-1.jpg", long size = 5000,
        WorldRole role = WorldRole.GM, Guid? userId = null, int ord = 0) =>
        new(WorldId, sourceId, userId ?? OwnerId, role, fileName, "image/jpeg", size, SourceAttachmentKind.PageImage, ord);

    [Test]
    public async Task RequestUpload_HappyPath_CreatesPendingRowWithSas()
    {
        var source = SeedSource();

        var result = await _sut.RequestUploadAsync(PageCommand(source.Id, ord: 2), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var ticket = result.Value!;
        Assert.That(ticket.Attachment.Status, Is.EqualTo(SourceAttachmentStatus.PendingUpload));
        Assert.That(ticket.Attachment.Ord, Is.EqualTo(2));
        Assert.That(ticket.Attachment.BlobPath, Does.Contain($"sources/{source.Id}/002-page-1.jpg"));
        Assert.That(ticket.UploadUrl, Does.Contain("sas=upload"));
    }

    [Test]
    public async Task RequestUpload_UnsupportedExtension_400()
    {
        var source = SeedSource();

        var result = await _sut.RequestUploadAsync(PageCommand(source.Id, fileName: "notes.pdf"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("unsupported_file_type"));
    }

    [Test]
    public async Task RequestUpload_OversizeFile_400()
    {
        var source = SeedSource();

        var result = await _sut.RequestUploadAsync(
            PageCommand(source.Id, size: SourceAttachmentService.MaxAttachmentSizeBytes + 1), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task RequestUpload_QueuedSource_409()
    {
        var source = SeedSource(SourceProcessingStatus.Queued);

        var result = await _sut.RequestUploadAsync(PageCommand(source.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
    }

    [Test]
    public async Task RequestUpload_NonOwnerPlayer_403()
    {
        var source = SeedSource();

        var result = await _sut.RequestUploadAsync(
            PageCommand(source.Id, role: WorldRole.Player, userId: Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task RequestUpload_InkDocument_ReusesTheSingleRow()
    {
        var source = SeedSource();
        var command = new RequestSourceAttachmentUploadCommand(
            WorldId, source.Id, OwnerId, WorldRole.GM, null, "application/json", 100, SourceAttachmentKind.InkDocument, 0);

        var first = await _sut.RequestUploadAsync(command, CancellationToken.None);
        var second = await _sut.RequestUploadAsync(command, CancellationToken.None);

        Assert.That(first.IsSuccess && second.IsSuccess, Is.True);
        Assert.That(second.Value!.Attachment.Id, Is.EqualTo(first.Value!.Attachment.Id),
            "autosave re-requests must reuse the single ink row");
        Assert.That(_attachmentRepository.Attachments.Count(a => a.Kind == SourceAttachmentKind.InkDocument), Is.EqualTo(1));
    }

    [Test]
    public async Task Confirm_BlobMissing_400()
    {
        var source = SeedSource();
        var ticket = (await _sut.RequestUploadAsync(PageCommand(source.Id), CancellationToken.None)).Value!;

        var result = await _sut.ConfirmUploadAsync(ticket.Attachment.Id, source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("upload_not_found"));
    }

    [Test]
    public async Task Confirm_StampsSizeAndStores()
    {
        var source = SeedSource();
        var ticket = (await _sut.RequestUploadAsync(PageCommand(source.Id), CancellationToken.None)).Value!;
        _blobStorage.Blobs[ticket.Attachment.BlobPath] = (new byte[1234], "image/jpeg");

        var result = await _sut.ConfirmUploadAsync(ticket.Attachment.Id, source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(SourceAttachmentStatus.Stored));
        Assert.That(result.Value.SizeBytes, Is.EqualTo(1234));
    }

    [Test]
    public async Task Confirm_PageImageTwice_409_ButInkReconfirms()
    {
        var source = SeedSource();

        var pageTicket = (await _sut.RequestUploadAsync(PageCommand(source.Id), CancellationToken.None)).Value!;
        _blobStorage.Blobs[pageTicket.Attachment.BlobPath] = (new byte[10], "image/jpeg");
        await _sut.ConfirmUploadAsync(pageTicket.Attachment.Id, source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);
        var again = await _sut.ConfirmUploadAsync(pageTicket.Attachment.Id, source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);
        Assert.That(again.Error!.StatusCode, Is.EqualTo(409));

        var inkCommand = new RequestSourceAttachmentUploadCommand(
            WorldId, source.Id, OwnerId, WorldRole.GM, null, "application/json", 100, SourceAttachmentKind.InkDocument, 0);
        var inkTicket = (await _sut.RequestUploadAsync(inkCommand, CancellationToken.None)).Value!;
        _blobStorage.Blobs[inkTicket.Attachment.BlobPath] = (new byte[50], "application/json");
        await _sut.ConfirmUploadAsync(inkTicket.Attachment.Id, source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);
        _blobStorage.Blobs[inkTicket.Attachment.BlobPath] = (new byte[75], "application/json");
        var reconfirm = await _sut.ConfirmUploadAsync(inkTicket.Attachment.Id, source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(reconfirm.IsSuccess, Is.True, "ink autosave re-confirms to re-stamp size");
        Assert.That(reconfirm.Value!.SizeBytes, Is.EqualTo(75));
    }

    [Test]
    public async Task List_ReturnsOnlyStored_WithReadUrls()
    {
        var source = SeedSource();
        var stored = (await _sut.RequestUploadAsync(PageCommand(source.Id), CancellationToken.None)).Value!;
        _blobStorage.Blobs[stored.Attachment.BlobPath] = (new byte[10], "image/jpeg");
        await _sut.ConfirmUploadAsync(stored.Attachment.Id, source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);
        await _sut.RequestUploadAsync(PageCommand(source.Id, fileName: "page-2.jpg", ord: 1), CancellationToken.None); // never confirmed

        var result = await _sut.ListAsync(source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Url, Does.Contain("sas=download"));
    }

    [Test]
    public async Task Delete_RemovesBlobAndRow_EvenWhenBlobDeleteFails()
    {
        var source = SeedSource();
        var ticket = (await _sut.RequestUploadAsync(PageCommand(source.Id), CancellationToken.None)).Value!;
        _blobStorage.FailDeletes = true;

        var result = await _sut.DeleteAsync(ticket.Attachment.Id, source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, "blob delete failures are swallowed — the row still goes");
        Assert.That(_attachmentRepository.Attachments, Is.Empty);
    }

    #region New attachment kinds (Image / Upload / Map)

    private Source SeedTypedSource(SourceType type, SourceProcessingStatus status = SourceProcessingStatus.Draft)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = type, Title = "Source",
            Visibility = VisibilityScope.PartyVisible, ProcessingStatus = status,
            CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = OwnerId
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private RequestSourceAttachmentUploadCommand Cmd(Guid sourceId, SourceAttachmentKind kind,
        string fileName, string contentType, long size = 5000) =>
        new(WorldId, sourceId, OwnerId, WorldRole.GM, fileName, contentType, size, kind, 0);

    [Test]
    public async Task Document_AcceptsPdf()
    {
        var source = SeedTypedSource(SourceType.Upload);

        var result = await _sut.RequestUploadAsync(
            Cmd(source.Id, SourceAttachmentKind.Document, "handout.pdf", "application/pdf"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Attachment.ContentType, Is.EqualTo("application/pdf"));
    }

    [Test]
    public async Task Document_AcceptsTextAndMarkdown()
    {
        var source = SeedTypedSource(SourceType.Upload);

        var txt = await _sut.RequestUploadAsync(
            Cmd(source.Id, SourceAttachmentKind.Document, "notes.txt", "text/plain"), CancellationToken.None);
        var md = await _sut.RequestUploadAsync(
            Cmd(source.Id, SourceAttachmentKind.Document, "lore.md", "text/markdown"), CancellationToken.None);

        Assert.That(txt.IsSuccess, Is.True);
        Assert.That(md.IsSuccess, Is.True);
    }

    [Test]
    public async Task ImageFile_RejectsPdf()
    {
        var source = SeedTypedSource(SourceType.Image);

        var result = await _sut.RequestUploadAsync(
            Cmd(source.Id, SourceAttachmentKind.ImageFile, "doc.pdf", "application/pdf"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("unsupported_file_type"));
    }

    [Test]
    public async Task MapImage_OnNonMapSource_Rejected()
    {
        var source = SeedTypedSource(SourceType.Image);

        var result = await _sut.RequestUploadAsync(
            Cmd(source.Id, SourceAttachmentKind.MapImage, "map.png", "image/png"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_kind"));
    }

    [Test]
    public async Task MapImage_SecondUpload_Rejected()
    {
        var source = SeedTypedSource(SourceType.Map);

        var first = await _sut.RequestUploadAsync(
            Cmd(source.Id, SourceAttachmentKind.MapImage, "map.png", "image/png"), CancellationToken.None);
        var second = await _sut.RequestUploadAsync(
            Cmd(source.Id, SourceAttachmentKind.MapImage, "map2.png", "image/png"), CancellationToken.None);

        Assert.That(first.IsSuccess, Is.True);
        Assert.That(second.IsSuccess, Is.False);
        Assert.That(second.Error!.Code, Is.EqualTo("duplicate_map_image"));
    }

    [Test]
    public async Task Confirm_ImageFile_ClearsDerivedText()
    {
        var source = SeedTypedSource(SourceType.Image);
        source.DerivedText = "stale derived text";

        var ticket = await _sut.RequestUploadAsync(
            Cmd(source.Id, SourceAttachmentKind.ImageFile, "art.png", "image/png"), CancellationToken.None);
        _blobStorage.Blobs[ticket.Value!.Attachment.BlobPath] = (new byte[10], "image/png");

        await _sut.ConfirmUploadAsync(ticket.Value.Attachment.Id, source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        var stored = (await _sourceRepository.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.DerivedText, Is.Null, "a changed derivation input invalidates the derived text");
    }

    #endregion
}
