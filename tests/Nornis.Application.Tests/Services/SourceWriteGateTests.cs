using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The one rule for "may this caller still change this source", shared by attachment writes
/// and by on-demand transcription. It was private to the attachment service until
/// transcription needed the same four checks; a second copy is how two write paths come to
/// disagree about who owns a draft.
/// </summary>
[TestFixture]
public class SourceWriteGateTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    private static Source Draft(
        Guid? worldId = null,
        Guid? createdBy = null,
        SourceProcessingStatus status = SourceProcessingStatus.Draft) => new()
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Type = SourceType.HandwrittenNotes,
            Title = "Field notes",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = status,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdBy ?? OwnerId
        };

    [Test]
    public void TheOwnerMayWriteTheirOwnDraft()
    {
        Assert.That(SourceWriteGate.Check(Draft(), WorldId, OwnerId, WorldRole.Player), Is.Null);
    }

    [Test]
    public void AGmMayWriteSomebodyElsesDraft()
    {
        Assert.That(SourceWriteGate.Check(Draft(), WorldId, OtherUserId, WorldRole.GM), Is.Null);
    }

    [Test]
    public void AnObserverMayNotWrite_EvenTheirOwn()
    {
        var error = SourceWriteGate.Check(Draft(createdBy: OtherUserId), WorldId, OtherUserId, WorldRole.Observer);

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.StatusCode, Is.EqualTo(403));
        Assert.That(error.Code, Is.EqualTo("insufficient_role"));
    }

    [Test]
    public void APlayerMayNotWriteAnotherPlayersDraft()
    {
        var error = SourceWriteGate.Check(Draft(), WorldId, OtherUserId, WorldRole.Player);

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public void AMissingSourceAndAnotherWorldsSource_LookTheSame()
    {
        // Deliberate: distinguishing them would tell an unauthorized caller that the id exists.
        var missing = SourceWriteGate.Check(null, WorldId, OwnerId, WorldRole.GM);
        var elsewhere = SourceWriteGate.Check(Draft(worldId: Guid.NewGuid()), WorldId, OwnerId, WorldRole.GM);

        Assert.That(missing!.StatusCode, Is.EqualTo(404));
        Assert.That(elsewhere!.StatusCode, Is.EqualTo(404));
        Assert.That(elsewhere.Message, Is.EqualTo(missing.Message));
    }

    [TestCase(SourceProcessingStatus.Draft, true)]
    [TestCase(SourceProcessingStatus.Ready, true)]
    [TestCase(SourceProcessingStatus.Failed, true)]
    [TestCase(SourceProcessingStatus.Queued, false)]
    [TestCase(SourceProcessingStatus.Processing, false)]
    [TestCase(SourceProcessingStatus.Processed, false)]
    public void OnlyASourceThePipelineHasNotClaimed_MayChange(
        SourceProcessingStatus status, bool writable)
    {
        var error = SourceWriteGate.Check(Draft(status: status), WorldId, OwnerId, WorldRole.GM);

        if (writable)
        {
            Assert.That(error, Is.Null);
            return;
        }

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.StatusCode, Is.EqualTo(409));
        Assert.That(error.Code, Is.EqualTo("invalid_status"));
    }

    [Test]
    public void TheRoleCheckRunsBeforeTheSourceIsEvenLookedAt()
    {
        // An observer gets 403 for a source that does not exist, rather than a 404 that would
        // let them probe which ids are real.
        var error = SourceWriteGate.Check(null, WorldId, OtherUserId, WorldRole.Observer);

        Assert.That(error!.StatusCode, Is.EqualTo(403));
    }
}
