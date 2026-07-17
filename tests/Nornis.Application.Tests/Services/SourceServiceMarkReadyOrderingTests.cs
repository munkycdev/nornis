using Nornis.Application.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Regression: the Queued status must be committed BEFORE the extraction message is
/// sent. A warm worker can receive the message faster than a post-enqueue status write
/// lands; it then skips the message as "not Queued" and the source wedges at Queued
/// forever. Observed in production during bulk import (2026-07-09).
/// </summary>
[TestFixture]
public class SourceServiceMarkReadyOrderingTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepository = null!;
    private FakeExtractionQueueClient _queueClient = null!;
    private SourceService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _queueClient = new FakeExtractionQueueClient();
        _sut = new SourceService(_sourceRepository, new InMemoryWorldMemberRepository(),
            new InMemoryCampaignRepository(), _queueClient,
            new InMemoryReviewBatchRepository(), new InMemorySourceAttachmentRepository(),
            new FakeBlobStorageService(), NullLogger<SourceService>.Instance);
    }

    private Source SeedDraftSource()
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "Session 1",
            Body = "We met Captain Voss.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = UserId
        };
        _sourceRepository.Seed(source);
        return source;
    }

    [Test]
    public async Task MarkReadyAsync_SourceIsQueuedBeforeMessageIsSent()
    {
        var source = SeedDraftSource();
        SourceProcessingStatus? statusAtSend = null;
        _queueClient.OnSend = (sourceId, _) =>
            statusAtSend = _sourceRepository.GetByIdAsync(sourceId).Result!.ProcessingStatus;

        var result = await _sut.MarkReadyAsync(
            new MarkSourceReadyCommand(source.Id, WorldId, UserId, WorldRole.GM), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(statusAtSend, Is.EqualTo(SourceProcessingStatus.Queued),
            "the worker validates Queued on receipt; committing it after the send races a warm worker");
        Assert.That(result.Value!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));
    }

    [Test]
    public async Task MarkReadyAsync_EnqueueFails_SourceRevertsToReady()
    {
        var source = SeedDraftSource();
        _queueClient.ConfigureToFail();

        var result = await _sut.MarkReadyAsync(
            new MarkSourceReadyCommand(source.Id, WorldId, UserId, WorldRole.GM), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("enqueue_failed"));
        var stored = await _sourceRepository.GetByIdAsync(source.Id);
        Assert.That(stored!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Ready),
            "a failed enqueue must leave the source retryable at Ready");
    }
}
