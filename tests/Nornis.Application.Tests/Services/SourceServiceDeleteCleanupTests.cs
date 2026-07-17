using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Deleting a processed source must clear its review batches (the batch→source FK is
/// Restrict, so leaving them made every processed-source delete 500) and its attachment
/// blobs. Regression for the FK_ReviewBatches_Sources_SourceId conflict.
/// </summary>
[TestFixture]
public class SourceServiceDeleteCleanupTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryReviewBatchRepository _reviewBatchRepository = null!;
    private InMemorySourceAttachmentRepository _attachmentRepository = null!;
    private FakeBlobStorageService _blobStorage = null!;
    private SourceService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _reviewBatchRepository = new InMemoryReviewBatchRepository();
        _attachmentRepository = new InMemorySourceAttachmentRepository();
        _blobStorage = new FakeBlobStorageService();
        _sut = new SourceService(
            _sourceRepository,
            new InMemoryWorldMemberRepository(),
            new InMemoryCampaignRepository(),
            new FakeExtractionQueueClient(),
            _reviewBatchRepository,
            _attachmentRepository,
            _blobStorage,
            NullLogger<SourceService>.Instance);
    }

    [Test]
    public async Task Delete_ProcessedSource_RemovesBatchesAndAttachmentBlobs()
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.HandwrittenNotes,
            Title = "Old session",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = OwnerId
        };
        _sourceRepository.Seed(source);

        await _reviewBatchRepository.CreateAsync(new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var blobPath = $"worlds/{WorldId}/sources/{source.Id}/ink.json";
        _blobStorage.Blobs[blobPath] = (new byte[10], "application/json");
        _attachmentRepository.Seed(new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            WorldId = WorldId,
            Kind = SourceAttachmentKind.InkDocument,
            FileName = "ink.json",
            ContentType = "application/json",
            BlobPath = blobPath,
            Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var result = await _sut.DeleteAsync(source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_reviewBatchRepository.Batches, Is.Empty, "batches must go before the source row");
        Assert.That(_blobStorage.Blobs.ContainsKey(blobPath), Is.False, "attachment blobs are cleaned up");
    }

    [Test]
    public async Task Delete_SurvivesBlobDeleteFailure()
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "Old session",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = OwnerId
        };
        _sourceRepository.Seed(source);
        _attachmentRepository.Seed(new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            WorldId = WorldId,
            Kind = SourceAttachmentKind.PageImage,
            FileName = "page.jpg",
            ContentType = "image/jpeg",
            BlobPath = "worlds/x/sources/y/page.jpg",
            Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        _blobStorage.FailDeletes = true;

        var result = await _sut.DeleteAsync(source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, "blob failures are swallowed — the delete still completes");
    }
}
