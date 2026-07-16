using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Messaging;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class RelationshipBackfillQueueServiceTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryReviewBatchRepository _reviewBatchRepository = null!;
    private FakeExtractionQueueClient _queueClient = null!;
    private RelationshipBackfillQueueService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _reviewBatchRepository = new InMemoryReviewBatchRepository();
        _queueClient = new FakeExtractionQueueClient();
        _sut = new RelationshipBackfillQueueService(
            _sourceRepository,
            _reviewBatchRepository,
            _queueClient,
            NullLogger<RelationshipBackfillQueueService>.Instance);
    }

    private Source SeedSource(SourceProcessingStatus status, string? body = "Something happened.")
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "A source",
            Body = body,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = status,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepository.Seed(source);
        return source;
    }

    [Test]
    public async Task NonGm_Forbidden()
    {
        var result = await _sut.QueueBackfillAsync(WorldId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_queueClient.SentMessages, Is.Empty);
    }

    [Test]
    public async Task QueuesOnlyUnsweptProcessedSourcesWithBodies()
    {
        var fresh = SeedSource(SourceProcessingStatus.Processed);
        var swept = SeedSource(SourceProcessingStatus.Processed);
        SeedSource(SourceProcessingStatus.Draft);            // not processed → not eligible
        SeedSource(SourceProcessingStatus.Processed, body: " "); // empty body → not eligible

        await _reviewBatchRepository.CreateAsync(new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            SourceId = swept.Id,
            Kind = RelationshipBackfillService.BatchKind,
            Status = ReviewBatchStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await _sut.QueueBackfillAsync(WorldId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.QueuedCount, Is.EqualTo(1));
        Assert.That(result.Value.AlreadySweptCount, Is.EqualTo(1));
        Assert.That(result.Value.TotalEligible, Is.EqualTo(2));

        var message = _queueClient.SentMessages.Single();
        Assert.That(message.SourceId, Is.EqualTo(fresh.Id));
        Assert.That(message.Kind, Is.EqualTo(ExtractionKind.RelationshipBackfill));
    }

    [Test]
    public async Task ExtractionBatch_DoesNotCountAsSwept()
    {
        var source = SeedSource(SourceProcessingStatus.Processed);
        await _reviewBatchRepository.CreateAsync(new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Completed, // Kind == null: the original extraction
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await _sut.QueueBackfillAsync(WorldId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.QueuedCount, Is.EqualTo(1));
        Assert.That(result.Value.AlreadySweptCount, Is.Zero);
    }
}
