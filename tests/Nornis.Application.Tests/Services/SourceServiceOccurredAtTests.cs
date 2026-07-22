using Nornis.Application.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// OccurredAt behavior of <see cref="SourceService.UpdateAsync"/>: the update is
/// partial, so a null OccurredAt means "no change" and clearing the date must be
/// asked for explicitly via ClearOccurredAt.
/// </summary>
[TestFixture]
public class SourceServiceOccurredAtTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset SessionDate = new(2026, 5, 20, 0, 0, 0, TimeSpan.Zero);

    private InMemorySourceRepository _sourceRepository = null!;
    private SourceService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _sut = new SourceService(_sourceRepository, new InMemoryWorldMemberRepository(), new InMemoryCampaignRepository(),
            new FakeExtractionQueueClient(), new InMemoryReviewBatchRepository(), new InMemorySourceAttachmentRepository(),
            new FakeBlobStorageService(), NullLogger<SourceService>.Instance);
    }

    private async Task<Guid> CreateSourceAsync(DateTimeOffset? occurredAt)
    {
        var created = await _sut.CreateAsync(new CreateSourceCommand(
            WorldId, "Session 1", SourceType.SessionNote, VisibilityScope.PartyVisible,
            UserId, WorldRole.GM, Body: "We met Captain Voss.", OccurredAt: occurredAt), CancellationToken.None);
        return created.Value!.Id;
    }

    [Test]
    public async Task UpdateAsync_SetsOccurredAt()
    {
        var id = await CreateSourceAsync(null);

        var result = await _sut.UpdateAsync(
            new UpdateSourceCommand(id, WorldId, UserId, WorldRole.GM, OccurredAt: SessionDate), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.OccurredAt, Is.EqualTo(SessionDate));
    }

    [Test]
    public async Task UpdateAsync_NullOccurredAt_LeavesDateUnchanged()
    {
        var id = await CreateSourceAsync(SessionDate);

        var result = await _sut.UpdateAsync(
            new UpdateSourceCommand(id, WorldId, UserId, WorldRole.GM, Title: "Renamed"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.OccurredAt, Is.EqualTo(SessionDate));
    }

    [Test]
    public async Task UpdateAsync_ClearOccurredAt_RemovesDate()
    {
        var id = await CreateSourceAsync(SessionDate);

        var result = await _sut.UpdateAsync(
            new UpdateSourceCommand(id, WorldId, UserId, WorldRole.GM, ClearOccurredAt: true), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.OccurredAt, Is.Null);
    }

    [Test]
    public async Task UpdateAsync_OccurredAtWins_WhenBothSetAndClearProvided()
    {
        var id = await CreateSourceAsync(null);

        var result = await _sut.UpdateAsync(
            new UpdateSourceCommand(id, WorldId, UserId, WorldRole.GM, OccurredAt: SessionDate, ClearOccurredAt: true),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.OccurredAt, Is.EqualTo(SessionDate));
    }
}
