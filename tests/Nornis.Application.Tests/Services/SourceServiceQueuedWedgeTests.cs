using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The Queued wedge: when an extraction message dead-letters, the source is left Queued with
/// nothing coming for it, and until 2026-08-02 no user-reachable path out — update, delete,
/// mark-ready and reprocess all refused it.
///
/// The reason it stayed open for a month is the reason these tests exist: the obvious fix is
/// worse than the bug. Allowing Queued → Ready outright lets a GM re-ready a source the worker
/// is genuinely mid-way through, producing a second extraction and a second paid AI call. So
/// the exit is gated on the source having been stuck longer than the queue could possibly hold
/// the message.
/// </summary>
[TestFixture]
public class SourceServiceQueuedWedgeTests
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
            new InMemoryReviewBatchRepository(), new InMemoryReviewProposalRepository(),
            new InMemorySourceAttachmentRepository(),
            new FakeBlobStorageService(), NullLogger<SourceService>.Instance);
    }

    private Source SeedQueuedSource(DateTimeOffset? statusChangedAt)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "Session 5",
            Body = "We questioned Captain Voss.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Queued,
            StatusChangedAt = statusChangedAt,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = UserId
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private Task<AppResult<Source>> MarkReadyAsync(Source source) =>
        _sut.MarkReadyAsync(
            new MarkSourceReadyCommand(source.Id, WorldId, UserId, WorldRole.GM),
            CancellationToken.None);

    [Test]
    public async Task RecentlyQueued_IsRefused()
    {
        var source = SeedQueuedSource(DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await MarkReadyAsync(source);

        // Five minutes in, the worker may simply be busy or scaling from zero. Re-readying
        // here is the double-spend this gate exists to prevent.
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
            Assert.That(result.Error.Code, Is.EqualTo("still_queued"));
            Assert.That(_queueClient.SentMessages, Is.Empty, "nothing may be re-enqueued");
        });
    }

    [Test]
    public async Task QueuedPastTheThreshold_CanBeRetried()
    {
        var source = SeedQueuedSource(DateTimeOffset.UtcNow - SourceService.StaleQueuedThreshold);

        var result = await MarkReadyAsync(source);

        // Past an hour the message has dead-lettered or is gone, so re-enqueueing cannot race
        // a live delivery. This is the path out of the wedge.
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));
            Assert.That(_queueClient.SentMessages, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task QueuedWithNoTimestamp_CanBeRetried()
    {
        // Rows that predate the column and have not moved since. Whatever their real age, it
        // is older than any threshold — and these are exactly the sources already wedged when
        // this shipped, which would otherwise stay stuck forever.
        var source = SeedQueuedSource(statusChangedAt: null);

        var result = await MarkReadyAsync(source);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task ProcessingSource_IsStillRefused()
    {
        // The gate opens Queued only. Processing means a worker has claimed it and is spending
        // money right now; nothing about the wedge makes that safe to duplicate.
        var source = SeedQueuedSource(DateTimeOffset.UtcNow.AddDays(-7));
        source.ProcessingStatus = SourceProcessingStatus.Processing;

        var result = await MarkReadyAsync(source);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo("invalid_transition"));
        });
    }
}
