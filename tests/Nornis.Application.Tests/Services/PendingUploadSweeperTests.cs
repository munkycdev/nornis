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
/// The sweep that removes upload rows whose blob never arrived. Both upload paths are a
/// two-step handshake and nothing ever completed the server's half when the browser abandoned
/// its own — so the rows accumulated forever, invisible to every listing, with their partial
/// blobs billed for beside them.
/// </summary>
[TestFixture]
public class PendingUploadSweeperTests
{
    private InMemoryLibraryDocumentRepository _documents = null!;
    private InMemorySourceAttachmentRepository _attachments = null!;
    private FakeBlobStorageService _blobs = null!;

    [SetUp]
    public void SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        _attachments = new InMemorySourceAttachmentRepository();
        _blobs = new FakeBlobStorageService();
    }

    private PendingUploadSweeper MakeSweeper(int abandonedAfterHours = 24, int maxPerSweep = 200) =>
        new(_documents, _attachments, _blobs,
            Options.Create(new UploadSweepOptions
            {
                AbandonedAfterHours = abandonedAfterHours,
                MaxPerSweep = maxPerSweep,
            }),
            NullLogger<PendingUploadSweeper>.Instance);

    private LibraryDocument SeedDocument(LibraryDocumentStatus status, TimeSpan age)
    {
        var document = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = Guid.NewGuid(),
            Title = "Player's Handbook",
            FileName = "phb.pdf",
            BlobPath = $"library/{Guid.NewGuid()}.pdf",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow - age,
        };
        _documents.CreateAsync(document).GetAwaiter().GetResult();
        _blobs.Blobs[document.BlobPath] = ([1, 2, 3], "application/pdf");
        return document;
    }

    private SourceAttachment SeedAttachment(SourceAttachmentStatus status, TimeSpan age)
    {
        var attachment = new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = Guid.NewGuid(),
            BlobPath = $"sources/{Guid.NewGuid()}.png",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow - age,
        };
        _attachments.CreateAsync(attachment).GetAwaiter().GetResult();
        _blobs.Blobs[attachment.BlobPath] = ([1, 2, 3], "image/png");
        return attachment;
    }

    [Test]
    public async Task AnAbandonedUpload_LosesBothItsRowAndItsBlob()
    {
        var document = SeedDocument(LibraryDocumentStatus.PendingUpload, TimeSpan.FromDays(3));
        var attachment = SeedAttachment(SourceAttachmentStatus.PendingUpload, TimeSpan.FromDays(3));

        var result = await MakeSweeper().SweepAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Documents, Is.EqualTo(1));
            Assert.That(result.Attachments, Is.EqualTo(1));
            Assert.That(_blobs.DeletedPaths, Is.EquivalentTo([document.BlobPath, attachment.BlobPath]));
        });
    }

    [Test]
    public async Task AnUploadStillInFlight_IsLeftAlone()
    {
        // An upload in progress is indistinguishable from an abandoned one except by age, which
        // is the whole reason the threshold exists. Sweeping too eagerly deletes live work.
        SeedDocument(LibraryDocumentStatus.PendingUpload, TimeSpan.FromMinutes(5));
        SeedAttachment(SourceAttachmentStatus.PendingUpload, TimeSpan.FromMinutes(5));

        var result = await MakeSweeper().SweepAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Total, Is.Zero);
            Assert.That(_blobs.DeletedPaths, Is.Empty);
        });
    }

    [Test]
    public async Task AConfirmedUpload_IsNeverTouchedHoweverOld()
    {
        // The status, not the age, is what makes a row sweepable. A Stored document is somebody's
        // library; an Indexed one is somebody's library that Ask is already reading.
        SeedDocument(LibraryDocumentStatus.Stored, TimeSpan.FromDays(400));
        SeedDocument(LibraryDocumentStatus.Indexed, TimeSpan.FromDays(400));
        SeedDocument(LibraryDocumentStatus.IndexFailed, TimeSpan.FromDays(400));
        SeedAttachment(SourceAttachmentStatus.Stored, TimeSpan.FromDays(400));

        var result = await MakeSweeper().SweepAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Total, Is.Zero);
            Assert.That(_blobs.DeletedPaths, Is.Empty);
        });
    }

    [Test]
    public async Task WhenTheBlobWillNotDelete_TheRowSurvivesToBeSweptAgain()
    {
        var document = SeedDocument(LibraryDocumentStatus.PendingUpload, TimeSpan.FromDays(3));
        _blobs.FailDeletes = true;

        var result = await MakeSweeper().SweepAsync(CancellationToken.None);

        // Dropping the row first would strand the blob permanently: nothing else records its
        // path, so an orphan in storage is billed for with no way left to find it.
        Assert.That(result.Total, Is.Zero);
        Assert.That(await _documents.GetByIdAsync(document.Id), Is.Not.Null);

        _blobs.FailDeletes = false;
        var retry = await MakeSweeper().SweepAsync(CancellationToken.None);

        Assert.That(retry.Documents, Is.EqualTo(1));
        Assert.That(await _documents.GetByIdAsync(document.Id), Is.Null);
    }

    [Test]
    public async Task ASweepIsBounded_AndTheRestWaitsForTheNextTick()
    {
        for (var i = 0; i < 5; i++)
        {
            SeedDocument(LibraryDocumentStatus.PendingUpload, TimeSpan.FromDays(3));
        }

        var sweeper = MakeSweeper(maxPerSweep: 2);

        // Oldest-first plus a cap means the backlog drains over ticks instead of turning one
        // tick into a long storage-delete loop.
        Assert.That((await sweeper.SweepAsync(CancellationToken.None)).Documents, Is.EqualTo(2));
        Assert.That((await sweeper.SweepAsync(CancellationToken.None)).Documents, Is.EqualTo(2));
        Assert.That((await sweeper.SweepAsync(CancellationToken.None)).Documents, Is.EqualTo(1));
        Assert.That((await sweeper.SweepAsync(CancellationToken.None)).Documents, Is.Zero);
    }
}
