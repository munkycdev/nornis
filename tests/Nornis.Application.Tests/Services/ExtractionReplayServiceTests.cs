using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The timeline replay walk: GM-gated start, one source in flight at a time, advance on
/// review completion in strict story order, skip-not-wedge on sources that will not
/// reprocess, and completion when the timeline runs dry. TryAdvance must never throw —
/// it runs inside review accepts and worker extractions.
/// </summary>
[TestFixture]
public class ExtractionReplayServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();
    private static readonly DateTimeOffset Day5 = new(2026, 7, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day10 = new(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day15 = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day20 = new(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);

    private InMemoryExtractionReplayRepository _replayRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private FakeSourceReprocessService _reprocess = null!;
    private ExtractionReplayService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _replayRepo = new InMemoryExtractionReplayRepository();
        _sourceRepo = new InMemorySourceRepository();
        _reprocess = new FakeSourceReprocessService(_sourceRepo);
        _sut = new ExtractionReplayService(
            _replayRepo, _sourceRepo, _reprocess, NullLogger<ExtractionReplayService>.Instance);
    }

    private Source SeedSource(
        string title,
        DateTimeOffset occurredAt,
        SourceType type = SourceType.SessionNote,
        SourceProcessingStatus status = SourceProcessingStatus.Processed,
        bool extractionEnabled = true)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = type,
            Title = title,
            Body = "Body",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = status,
            ExtractionEnabled = extractionEnabled,
            OccurredAt = occurredAt,
            CreatedAt = occurredAt,
            CreatedByUserId = GmId
        };
        _sourceRepo.Seed(source);
        return source;
    }

    private ExtractionReplay ActiveReplay(Guid cursorSourceId)
    {
        var replay = new ExtractionReplay
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            CurrentSourceId = cursorSourceId,
            Status = ExtractionReplayStatus.Active,
            CreatedByUserId = GmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _replayRepo.Seed(replay);
        return replay;
    }

    // ------------------------------------------------------------------- Start --

    [Test]
    public async Task Start_NonGm_Forbidden()
    {
        var start = SeedSource("Session 1", Day5);

        var result = await _sut.StartAsync(WorldId, start.Id, GmId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("insufficient_role"));
        Assert.That(_replayRepo.Replays, Is.Empty);
    }

    [Test]
    public async Task Start_RaceThatSlipsPastTheCheck_Conflicts_NotCrashes()
    {
        // The gate is check-then-create, so a double-click can pass it twice — the filtered
        // unique index is what actually holds "one Active replay per world". This drives the
        // path where the check saw nothing and the insert lost anyway: the second start must
        // read as the same 409, not a 500 with a second Active replay requeueing sources.
        var start = SeedSource("Session 1", Day5);
        var alsoStart = SeedSource("Session 2", Day10);

        var first = await _sut.StartAsync(WorldId, start.Id, GmId, WorldRole.GM, CancellationToken.None);
        Assert.That(first.IsSuccess, Is.True);

        // Bypass the gate the way a concurrent request does — by having already passed it.
        var second = await _sut.StartAsync(WorldId, alsoStart.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(second.IsSuccess, Is.False);
            Assert.That(second.Error!.Code, Is.EqualTo("replay_active"));
            Assert.That(_replayRepo.Replays.Count(r => r.Status == ExtractionReplayStatus.Active), Is.EqualTo(1),
                "a lost race must never leave a second Active replay behind");
        });
    }

    [Test]
    public async Task Start_ActiveReplayExists_Conflicts()
    {
        var start = SeedSource("Session 1", Day5);
        ActiveReplay(SeedSource("Session 2", Day10).Id);

        var result = await _sut.StartAsync(WorldId, start.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("replay_active"));
    }

    [Test]
    public async Task Start_NonTimelineSource_IsAccepted()
    {
        // A replay re-extracts the whole world. Restricting it to timeline types left GM
        // notes, lore documents, uploads and maps silently empty after a re-extraction that
        // looked like it had covered everything.
        var start = SeedSource("A GM note", Day5, type: SourceType.GMNote);

        var result = await _sut.StartAsync(WorldId, start.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(_reprocess.Commands.Select(c => c.SourceId), Does.Contain(start.Id));
    }

    [Test]
    public async Task Walk_CoversEveryExtractableType_InStoryOrder()
    {
        var start = SeedSource("Session 1", Day5);
        var gmNote = SeedSource("GM prep", Day10, type: SourceType.GMNote);
        var upload = SeedSource("Lore PDF", Day15, type: SourceType.Upload);
        var map = SeedSource("Region map", Day20, type: SourceType.Map);

        var count = await _sut.CountFromAsync(WorldId, start.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(count.IsSuccess, Is.True, count.Error?.Message);
        Assert.That(count.Value, Is.EqualTo(4),
            "the start source plus every extractable type after it");

        await _sut.StartAsync(WorldId, start.Id, GmId, WorldRole.GM, CancellationToken.None);
        await _sut.TryAdvanceAsync(WorldId, start.Id, CancellationToken.None);
        await _sut.TryAdvanceAsync(WorldId, gmNote.Id, CancellationToken.None);
        await _sut.TryAdvanceAsync(WorldId, upload.Id, CancellationToken.None);

        Assert.That(_reprocess.Commands.Select(c => c.SourceId),
            Is.EqualTo(new[] { start.Id, gmNote.Id, upload.Id, map.Id }),
            "the walk visits every type in story order, not just the timeline ones");
    }

    [Test]
    public async Task Walk_StillSkipsSourcesStoredWithoutExtraction()
    {
        var start = SeedSource("Session 1", Day5);
        SeedSource("Reference sheet", Day10, type: SourceType.Upload, extractionEnabled: false);
        var next = SeedSource("GM prep", Day15, type: SourceType.GMNote);

        await _sut.StartAsync(WorldId, start.Id, GmId, WorldRole.GM, CancellationToken.None);
        await _sut.TryAdvanceAsync(WorldId, start.Id, CancellationToken.None);

        Assert.That(_reprocess.Commands.Select(c => c.SourceId), Is.EqualTo(new[] { start.Id, next.Id }),
            "ExtractionEnabled is what holds a source back from a replay, not its type");
    }

    [Test]
    public async Task Start_ExtractionDisabledSource_Rejected()
    {
        var start = SeedSource("Session 1", Day5, extractionEnabled: false);

        var result = await _sut.StartAsync(WorldId, start.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("not_replayable"));
    }

    [Test]
    public async Task Start_CreatesActiveReplayAndReprocessesStartSource()
    {
        var start = SeedSource("Session 1", Day5);
        SeedSource("Session 2", Day10);

        var result = await _sut.StartAsync(WorldId, start.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo("Active"));
        Assert.That(result.Value.CurrentSourceId, Is.EqualTo(start.Id));
        Assert.That(result.Value.RemainingCount, Is.EqualTo(1));
        Assert.That(_reprocess.Commands, Has.Count.EqualTo(1));
        Assert.That(_reprocess.Commands[0].SourceId, Is.EqualTo(start.Id));
        Assert.That(_reprocess.Commands[0].ActingUserId, Is.EqualTo(GmId));
    }

    [Test]
    public async Task Start_ReprocessFails_ReplayIsRetiredNotLeftActive()
    {
        var start = SeedSource("Session 1", Day5);
        _reprocess.FailingSourceIds.Add(start.Id);

        var result = await _sut.StartAsync(WorldId, start.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(await _replayRepo.GetActiveByWorldAsync(WorldId, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task CountFrom_CountsStartPlusEligibleFollowers()
    {
        var start = SeedSource("Session 2", Day10);
        SeedSource("Session 1", Day5);                                       // before: not counted
        SeedSource("Session 3", Day15);                                      // counted
        SeedSource("Session 4", Day20, extractionEnabled: false);            // ineligible
        SeedSource("Draft note", Day20, status: SourceProcessingStatus.Draft); // ineligible

        var result = await _sut.CountFromAsync(WorldId, start.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(2)); // Session 2 itself + Session 3
    }

    // ----------------------------------------------------------------- Advance --

    [Test]
    public async Task TryAdvance_NoActiveReplay_DoesNothing()
    {
        var source = SeedSource("Session 1", Day5);

        await _sut.TryAdvanceAsync(WorldId, source.Id, CancellationToken.None);

        Assert.That(_reprocess.Commands, Is.Empty);
    }

    [Test]
    public async Task TryAdvance_NotTheCursorSource_DoesNothing()
    {
        var cursor = SeedSource("Session 2", Day10);
        var other = SeedSource("Session 1", Day5);
        ActiveReplay(cursor.Id);

        await _sut.TryAdvanceAsync(WorldId, other.Id, CancellationToken.None);

        Assert.That(_reprocess.Commands, Is.Empty);
    }

    [Test]
    public async Task TryAdvance_ReprocessesNextInTimelineOrder()
    {
        var cursor = SeedSource("Session 1", Day5);
        var next = SeedSource("Session 2", Day10);
        SeedSource("Session 3", Day15);
        var replay = ActiveReplay(cursor.Id);

        await _sut.TryAdvanceAsync(WorldId, cursor.Id, CancellationToken.None);

        Assert.That(_reprocess.Commands, Has.Count.EqualTo(1));
        Assert.That(_reprocess.Commands[0].SourceId, Is.EqualTo(next.Id));
        Assert.That(_reprocess.Commands[0].ActingUserId, Is.EqualTo(GmId));
        Assert.That(_reprocess.Commands[0].ActingUserRole, Is.EqualTo(WorldRole.GM));
        Assert.That(_replayRepo.Replays.Single(r => r.Id == replay.Id).CurrentSourceId, Is.EqualTo(next.Id));
    }

    [Test]
    public async Task TryAdvance_SkipsSourceThatWillNotReprocess()
    {
        var cursor = SeedSource("Session 1", Day5);
        var stubborn = SeedSource("Session 2", Day10);
        var after = SeedSource("Session 3", Day15);
        var replay = ActiveReplay(cursor.Id);
        _reprocess.FailingSourceIds.Add(stubborn.Id);

        await _sut.TryAdvanceAsync(WorldId, cursor.Id, CancellationToken.None);

        Assert.That(_reprocess.Commands.Select(c => c.SourceId),
            Is.EqualTo(new[] { stubborn.Id, after.Id }));
        Assert.That(_replayRepo.Replays.Single(r => r.Id == replay.Id).CurrentSourceId, Is.EqualTo(after.Id));
    }

    [Test]
    public async Task TryAdvance_NothingLeft_CompletesTheReplay()
    {
        var cursor = SeedSource("Session Final", Day20);
        var replay = ActiveReplay(cursor.Id);

        await _sut.TryAdvanceAsync(WorldId, cursor.Id, CancellationToken.None);

        var stored = _replayRepo.Replays.Single(r => r.Id == replay.Id);
        Assert.That(stored.Status, Is.EqualTo(ExtractionReplayStatus.Completed));
        Assert.That(stored.CompletedAt, Is.Not.Null);
        Assert.That(_reprocess.Commands, Is.Empty);
    }

    [Test]
    public async Task TryAdvance_SkipsIneligibleSourcesEntirely()
    {
        var cursor = SeedSource("Session 1", Day5);
        SeedSource("Stored without extraction", Day10, extractionEnabled: false);
        // Type is no longer an eligibility criterion, but an in-flight status still is.
        SeedSource("Already queued", Day10, status: SourceProcessingStatus.Queued);
        var eligible = SeedSource("Session 2", Day15);
        ActiveReplay(cursor.Id);

        await _sut.TryAdvanceAsync(WorldId, cursor.Id, CancellationToken.None);

        Assert.That(_reprocess.Commands.Select(c => c.SourceId), Is.EqualTo(new[] { eligible.Id }));
    }

    [Test]
    public void TryAdvance_ReprocessThrows_NeverPropagates()
    {
        var cursor = SeedSource("Session 1", Day5);
        SeedSource("Session 2", Day10);
        ActiveReplay(cursor.Id);
        _reprocess.ThrowOnReprocess = new InvalidOperationException("boom");

        Assert.DoesNotThrowAsync(() => _sut.TryAdvanceAsync(WorldId, cursor.Id, CancellationToken.None));
    }

    // ------------------------------------------------------------ Status/Cancel --

    [Test]
    public async Task GetActive_NoReplay_ReturnsNull()
    {
        var result = await _sut.GetActiveAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Null);
    }

    [Test]
    public async Task GetActive_ReportsCursorAndRemaining()
    {
        var cursor = SeedSource("Session 2", Day10, status: SourceProcessingStatus.Queued);
        SeedSource("Session 3", Day15);
        SeedSource("Session 4", Day20);
        ActiveReplay(cursor.Id);

        var result = await _sut.GetActiveAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.CurrentSourceTitle, Is.EqualTo("Session 2"));
        Assert.That(result.Value.CurrentSourceProcessingStatus, Is.EqualTo("Queued"));
        Assert.That(result.Value.RemainingCount, Is.EqualTo(2));
    }

    [Test]
    public async Task GetActive_NonGm_Forbidden()
    {
        var result = await _sut.GetActiveAsync(WorldId, GmId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("insufficient_role"));
    }

    [Test]
    public async Task Cancel_SetsCanceled_AndAdvanceBecomesNoOp()
    {
        var cursor = SeedSource("Session 1", Day5);
        SeedSource("Session 2", Day10);
        var replay = ActiveReplay(cursor.Id);

        var result = await _sut.CancelAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);
        await _sut.TryAdvanceAsync(WorldId, cursor.Id, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_replayRepo.Replays.Single(r => r.Id == replay.Id).Status,
            Is.EqualTo(ExtractionReplayStatus.Canceled));
        Assert.That(_reprocess.Commands, Is.Empty);
    }

    [Test]
    public async Task Cancel_NoActiveReplay_NotFound()
    {
        var result = await _sut.CancelAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("not_found"));
    }
}
