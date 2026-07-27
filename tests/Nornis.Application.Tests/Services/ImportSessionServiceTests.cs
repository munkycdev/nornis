using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The campaign backlog walk: one non-terminal session per world, GM-gated throughout,
/// notes held as draft sources until their turn, and an advance that refuses to move while
/// the note on screen still has open proposals. Item state is never stored — every
/// assertion here reads it back off the source, which is the point of the design.
/// </summary>
[TestFixture]
public class ImportSessionServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();
    private static readonly Guid PlayerId = Guid.NewGuid();

    private InMemoryImportSessionRepository _sessions = null!;
    private InMemorySourceRepository _sources = null!;
    private InMemoryReviewBatchRepository _batches = null!;
    private InMemoryReviewProposalRepository _proposals = null!;
    private FakeExtractionQueueClient _queue = null!;
    private InMemorySourceReferenceRepository _references = null!;
    private FakeSourceReprocessService _reprocess = null!;
    private SourceService _sourceService = null!;
    private ImportSessionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sessions = new InMemoryImportSessionRepository();
        _sources = new InMemorySourceRepository();
        _batches = new InMemoryReviewBatchRepository();
        _proposals = new InMemoryReviewProposalRepository(_batches);
        _queue = new FakeExtractionQueueClient();

        _sourceService = new SourceService(
            _sources,
            new InMemoryWorldMemberRepository(),
            new InMemoryCampaignRepository(),
            _queue,
            _batches,
            _proposals,
            new InMemorySourceAttachmentRepository(),
            new FakeBlobStorageService(),
            NullLogger<SourceService>.Instance);

        _references = new InMemorySourceReferenceRepository();
        _reprocess = new FakeSourceReprocessService(_sources);

        _sut = new ImportSessionService(
            _sessions, _sources, _proposals, _references, _sourceService, _reprocess,
            NullLogger<ImportSessionService>.Instance);
    }

    // ------------------------------------------------------------------ helpers --

    private async Task<ImportSessionInfo> NewSessionAsync()
    {
        var result = await _sut.CreateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        return result.Value!;
    }

    private async Task<ImportSessionInfo> AddNoteAsync(
        Guid sessionId, string title, DateTimeOffset? occurredAt = null)
    {
        var result = await _sut.AddItemAsync(new AddImportNoteCommand(
            WorldId, sessionId, GmId, WorldRole.GM, title, $"Body of {title}", occurredAt),
            CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        return result.Value!;
    }

    /// <summary>Runs the worker's half of the deal: the queued source comes back Processed
    /// with the given number of open proposals hanging off a batch.</summary>
    private void CompleteExtraction(Guid sourceId, int openProposals = 0)
    {
        var source = _sources.Sources.First(s => s.Id == sourceId);
        source.ProcessingStatus = SourceProcessingStatus.Processed;

        if (openProposals == 0)
        {
            return;
        }

        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            SourceId = sourceId,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _batches.CreateAsync(batch).GetAwaiter().GetResult();

        for (var i = 0; i < openProposals; i++)
        {
            _proposals.CreateAsync(new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batch.Id,
                ChangeType = ReviewChangeType.CreateArtifact,
                TargetType = ReviewTargetType.Artifact,
                Status = i == 0 ? ReviewProposalStatus.Edited : ReviewProposalStatus.Pending,
                ProposedValueJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            }).GetAwaiter().GetResult();
        }
    }

    private void ResolveAllProposals(Guid sourceId)
    {
        var batchIds = _batches.Batches.Where(b => b.SourceId == sourceId).Select(b => b.Id).ToHashSet();
        foreach (var proposal in _proposals.Proposals.Where(p => batchIds.Contains(p.ReviewBatchId)))
        {
            proposal.Status = ReviewProposalStatus.Accepted;
        }
    }

    // ------------------------------------------------------------------- create --

    [Test]
    public async Task Create_NonGm_Forbidden()
    {
        var result = await _sut.CreateAsync(WorldId, PlayerId, WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        });
    }

    [Test]
    public async Task Create_SecondNonTerminalSession_Conflicts()
    {
        await NewSessionAsync();

        var second = await _sut.CreateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(second.IsSuccess, Is.False);
            Assert.That(second.Error!.StatusCode, Is.EqualTo(409));
            Assert.That(second.Error!.Code, Is.EqualTo("import_session_active"));
        });
    }

    [Test]
    public async Task Create_AfterAbandon_IsAllowedAgain()
    {
        var first = await NewSessionAsync();
        await _sut.AbandonAsync(WorldId, first.Id, GmId, WorldRole.GM, CancellationToken.None);

        var second = await _sut.CreateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(second.IsSuccess, Is.True, second.Error?.Message);
    }

    [Test]
    public async Task GetCurrent_NoSession_NotFound()
    {
        var result = await _sut.GetCurrentAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        });
    }

    // --------------------------------------------------------------------- items --

    [Test]
    public async Task AddItem_CreatesHeldDraftSourceAtTheEnd()
    {
        var session = await NewSessionAsync();

        await AddNoteAsync(session.Id, "Session 1");
        var info = await AddNoteAsync(session.Id, "Session 2");

        Assert.Multiple(() =>
        {
            Assert.That(info.Items.Select(i => i.Title), Is.EqualTo(new[] { "Session 1", "Session 2" }));
            Assert.That(info.Items.Select(i => i.State),
                Is.All.EqualTo(ImportItemState.Waiting));
            // Held, not queued: nothing is read until the GM starts the walk.
            Assert.That(_sources.Sources.Select(s => s.ProcessingStatus),
                Is.All.EqualTo(SourceProcessingStatus.Draft));
            Assert.That(_sources.Sources.Select(s => s.Type), Is.All.EqualTo(SourceType.ImportedNote));
            Assert.That(_queue.SentMessages, Is.Empty);
        });
    }

    [Test]
    public async Task AddItem_NonGm_Forbidden()
    {
        var session = await NewSessionAsync();

        var result = await _sut.AddItemAsync(new AddImportNoteCommand(
            WorldId, session.Id, PlayerId, WorldRole.Player, "Session 1", "Body", null),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        });
    }

    [Test]
    public async Task Reorder_MovesNotYetStartedItems()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var info = await AddNoteAsync(session.Id, "Session 2");

        var reversed = info.Items.Select(i => i.Id).Reverse().ToList();
        var result = await _sut.ReorderAsync(
            WorldId, session.Id, reversed, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.Items.Select(i => i.Title), Is.EqualTo(new[] { "Session 2", "Session 1" }));
    }

    [Test]
    public async Task Reorder_PartialList_Rejected()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var info = await AddNoteAsync(session.Id, "Session 2");

        var result = await _sut.ReorderAsync(
            WorldId, session.Id, [info.Items[0].Id], GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
            Assert.That(result.Error!.Code, Is.EqualTo("invalid_order"));
        });
    }

    [Test]
    public async Task Reorder_InProgress_LeavesStartedItemInPlace()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        await AddNoteAsync(session.Id, "Session 2");
        var info = await AddNoteAsync(session.Id, "Session 3");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        // Session 1 is in flight; only 2 and 3 may move, and 1 keeps the front of the walk.
        var movable = info.Items.Skip(1).Select(i => i.Id).Reverse().ToList();
        var result = await _sut.ReorderAsync(
            WorldId, session.Id, movable, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.Items.Select(i => i.Title),
            Is.EqualTo(new[] { "Session 1", "Session 3", "Session 2" }));
    }

    [Test]
    public async Task Reorder_IncludingStartedItem_Rejected()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var info = await AddNoteAsync(session.Id, "Session 2");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        var all = info.Items.Select(i => i.Id).Reverse().ToList();
        var result = await _sut.ReorderAsync(
            WorldId, session.Id, all, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo("invalid_order"));
        });
    }

    [Test]
    public async Task DeleteItem_RemovesItemAndSource()
    {
        var session = await NewSessionAsync();
        var info = await AddNoteAsync(session.Id, "Session 1");
        var item = info.Items[0];

        var result = await _sut.DeleteItemAsync(
            WorldId, session.Id, item.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Items, Is.Empty);
            Assert.That(_sources.Sources.Any(s => s.Id == item.SourceId), Is.False);
        });
    }

    [Test]
    public async Task DeleteItem_AlreadyStarted_Conflicts()
    {
        var session = await NewSessionAsync();
        var info = await AddNoteAsync(session.Id, "Session 1");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        var result = await _sut.DeleteItemAsync(
            WorldId, session.Id, info.Items[0].Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
            Assert.That(result.Error!.Code, Is.EqualTo("item_started"));
            Assert.That(_sources.Sources, Has.Count.EqualTo(1));
        });
    }

    // --------------------------------------------------------------------- start --

    [Test]
    public async Task Start_QueuesOnlyTheFirstNote()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        await AddNoteAsync(session.Id, "Session 2");

        var result = await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var info = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(info.Status, Is.EqualTo(ImportSessionStatus.InProgress));
            Assert.That(info.Items[0].State, Is.EqualTo(ImportItemState.Extracting));
            Assert.That(info.Items[1].State, Is.EqualTo(ImportItemState.Waiting));
            Assert.That(_queue.SentMessages, Has.Count.EqualTo(1));
            Assert.That(_queue.SentMessages[0].SourceId, Is.EqualTo(info.Items[0].SourceId));
        });
    }

    [Test]
    public async Task Start_EmptyBacklog_Rejected()
    {
        var session = await NewSessionAsync();

        var result = await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo("no_items"));
        });
    }

    [Test]
    public async Task Start_EnqueueFails_SessionFallsBackToDraft()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        _queue.ConfigureToFail();

        var result = await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        var current = await _sut.GetCurrentAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);
        Assert.That(current.Value!.Status, Is.EqualTo(ImportSessionStatus.Draft));
    }

    // ------------------------------------------------------------------- advance --

    [Test]
    public async Task Advance_WithOpenProposals_Refused()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var info = await AddNoteAsync(session.Id, "Session 2");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        // One Pending and one Edited — Edited counts as open just like Pending.
        CompleteExtraction(info.Items[0].SourceId, openProposals: 2);

        var result = await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: false, expectedItemId: null, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
            Assert.That(result.Error!.Code, Is.EqualTo("item_not_ready"));
            Assert.That(_queue.SentMessages, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Advance_WhileExtracting_Refused()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        await AddNoteAsync(session.Id, "Session 2");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        var result = await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: false, expectedItemId: null, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo("item_not_ready"));
        });
    }

    [Test]
    public async Task Advance_AfterProposalsResolved_QueuesTheNextNote()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var info = await AddNoteAsync(session.Id, "Session 2");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        CompleteExtraction(info.Items[0].SourceId, openProposals: 2);
        ResolveAllProposals(info.Items[0].SourceId);

        var result = await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: false, expectedItemId: null, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Items[0].State, Is.EqualTo(ImportItemState.Done));
            Assert.That(result.Value!.Items[1].State, Is.EqualTo(ImportItemState.Extracting));
            Assert.That(_queue.SentMessages, Has.Count.EqualTo(2));
            Assert.That(_queue.SentMessages[1].SourceId, Is.EqualTo(info.Items[1].SourceId));
        });
    }

    [Test]
    public async Task Advance_ProcessedWithNoProposals_IsDoneImmediately()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var info = await AddNoteAsync(session.Id, "Session 2");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        CompleteExtraction(info.Items[0].SourceId);

        var result = await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: false, expectedItemId: null, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.Items[1].State, Is.EqualTo(ImportItemState.Extracting));
    }

    [Test]
    public async Task Advance_SkipCurrent_PassesOverAndQueuesTheNext()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var info = await AddNoteAsync(session.Id, "Session 2");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        CompleteExtraction(info.Items[0].SourceId, openProposals: 1);

        var result = await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: true, expectedItemId: null, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Items[0].State, Is.EqualTo(ImportItemState.Skipped));
            Assert.That(result.Value!.Items[1].State, Is.EqualTo(ImportItemState.Extracting));
            // Skipping does not touch the proposals — they stay open in the review queue.
            Assert.That(_proposals.Proposals.Count(p => p.Status == ReviewProposalStatus.Edited), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Advance_FailedExtraction_SkippedNotWedged()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var info = await AddNoteAsync(session.Id, "Session 2");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        _sources.Sources.First(s => s.Id == info.Items[0].SourceId).ProcessingStatus =
            SourceProcessingStatus.Failed;

        var refused = await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: false, expectedItemId: null, GmId, WorldRole.GM, CancellationToken.None);
        var skipped = await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: true, expectedItemId: null, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(refused.IsSuccess, Is.False);
            Assert.That(refused.Error!.Code, Is.EqualTo("item_not_ready"));
            Assert.That(skipped.IsSuccess, Is.True, skipped.Error?.Message);
            Assert.That(skipped.Value!.Items[0].State, Is.EqualTo(ImportItemState.Skipped));
            Assert.That(skipped.Value!.Items[1].State, Is.EqualTo(ImportItemState.Extracting));
        });
    }

    [Test]
    public async Task FailedNote_RetriedThroughMarkReady_ReturnsToExtracting()
    {
        var session = await NewSessionAsync();
        var info = await AddNoteAsync(session.Id, "Session 1");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        var source = _sources.Sources.First(s => s.Id == info.Items[0].SourceId);
        source.ProcessingStatus = SourceProcessingStatus.Failed;

        // The retry is the ordinary Failed → Ready path, not an import-specific endpoint.
        var retry = await _sourceService.MarkReadyAsync(
            new MarkSourceReadyCommand(source.Id, WorldId, GmId, WorldRole.GM), CancellationToken.None);

        Assert.That(retry.IsSuccess, Is.True, retry.Error?.Message);
        var current = await _sut.GetCurrentAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);
        Assert.That(current.Value!.Items[0].State, Is.EqualTo(ImportItemState.Extracting));
    }

    [Test]
    public async Task Advance_PastTheLastNote_CompletesTheSession()
    {
        var session = await NewSessionAsync();
        var info = await AddNoteAsync(session.Id, "Session 1");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        CompleteExtraction(info.Items[0].SourceId);

        var result = await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: false, expectedItemId: null, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Status, Is.EqualTo(ImportSessionStatus.Completed));
            Assert.That(result.Value!.CurrentItemId, Is.Null);
            Assert.That(result.Value!.SettledCount, Is.EqualTo(1));
        });

        // A completed session no longer answers "current" — the world is free to import again.
        var current = await _sut.GetCurrentAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);
        Assert.That(current.IsSuccess, Is.False);
    }

    [Test]
    public async Task Start_AfterAFailedEnqueue_RequeuesTheStrandedNote()
    {
        var session = await NewSessionAsync();
        var info = await AddNoteAsync(session.Id, "Session 1");

        _queue.ConfigureToFail();
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        // Mark-ready leaves an un-enqueued source at Ready. It is NOT in the worker's hands,
        // so the walk must show it as waiting and be able to send it again.
        var stranded = await _sut.GetCurrentAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);
        Assert.That(stranded.Value!.Items[0].State, Is.EqualTo(ImportItemState.Waiting));

        _queue.ConfigureToFail(false);
        var retry = await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(retry.IsSuccess, Is.True, retry.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(retry.Value!.Items[0].State, Is.EqualTo(ImportItemState.Extracting));
            Assert.That(_queue.SentMessages, Has.Count.EqualTo(1));
            Assert.That(_queue.SentMessages[0].SourceId, Is.EqualTo(info.Items[0].SourceId));
        });
    }

    [Test]
    public async Task Advance_WhenTheWalkHasMovedOn_RefusesRatherThanSkippingAnUnseenNote()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var info = await AddNoteAsync(session.Id, "Session 2");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        CompleteExtraction(info.Items[0].SourceId);
        await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: false, expectedItemId: null, GmId, WorldRole.GM,
            CancellationToken.None);

        // A second tab still believes note 1 is current and asks to skip it. Skipping note 2 —
        // which the user has not even seen — would be silent data loss to the walk.
        var stale = await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: true, expectedItemId: info.Items[0].Id, GmId, WorldRole.GM,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(stale.IsSuccess, Is.False);
            Assert.That(stale.Error!.StatusCode, Is.EqualTo(409));
            Assert.That(stale.Error!.Code, Is.EqualTo("item_moved"));
            Assert.That(_sessions.Items.Any(i => i.Skipped), Is.False);
        });
    }

    [Test]
    public async Task Advance_BeforeStart_Conflicts()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");

        var result = await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: false, expectedItemId: null, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        });
    }

    [Test]
    public async Task Advance_NonGm_Forbidden()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        var result = await _sut.AdvanceAsync(
            WorldId, session.Id, skipCurrent: false, expectedItemId: null, PlayerId, WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        });
    }

    // ------------------------------------------------------------------- abandon --

    [Test]
    public async Task Abandon_DeletesNothing()
    {
        var session = await NewSessionAsync();
        await AddNoteAsync(session.Id, "Session 1");
        var info = await AddNoteAsync(session.Id, "Session 2");
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);
        CompleteExtraction(info.Items[0].SourceId);

        var result = await _sut.AbandonAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Status, Is.EqualTo(ImportSessionStatus.Abandoned));
            Assert.That(_sources.Sources, Has.Count.EqualTo(2));
            Assert.That(_sessions.Items, Has.Count.EqualTo(2));
            // The note never reached keeps its ordinary draft life.
            Assert.That(
                _sources.Sources.First(s => s.Id == info.Items[1].SourceId).ProcessingStatus,
                Is.EqualTo(SourceProcessingStatus.Draft));
        });
    }

    [Test]
    public async Task Abandon_Twice_Conflicts()
    {
        var session = await NewSessionAsync();
        await _sut.AbandonAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        var again = await _sut.AbandonAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(again.IsSuccess, Is.False);
            Assert.That(again.Error!.StatusCode, Is.EqualTo(409));
        });
    }

    [Test]
    public async Task Session_FromAnotherWorld_NotFound()
    {
        var session = await NewSessionAsync();

        var result = await _sut.AddItemAsync(new AddImportNoteCommand(
            Guid.NewGuid(), session.Id, GmId, WorldRole.GM, "Session 1", null, null),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        });
    }

    // ------------------------------------------------- Staging existing sources --

    /// <summary>A source the world already holds: extracted, reviewed, and sitting Processed.</summary>
    private Source SeedExistingSource(
        string title, DateTimeOffset? occurredAt = null, SourceType type = SourceType.GMNote,
        bool extractionEnabled = true)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = type,
            Title = title,
            Body = $"Body of {title}",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            ExtractionEnabled = extractionEnabled,
            OccurredAt = occurredAt,
            CreatedAt = occurredAt ?? DateTimeOffset.UtcNow,
            CreatedByUserId = GmId
        };
        _sources.Seed(source);
        return source;
    }

    private async Task<ImportSessionInfo> AddExistingAsync(Guid sessionId, params Guid[] sourceIds)
    {
        var result = await _sut.AddExistingSourcesAsync(
            WorldId, sessionId, sourceIds, GmId, WorldRole.GM, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        return result.Value!;
    }

    [Test]
    public async Task StagedExistingSources_AreWaiting_NotDoneBeforeTheWalkReachesThem()
    {
        // The trap this guards: an existing source enters the run already Processed with no
        // open proposals. State derived from status alone would call it Done, and the walk
        // would report the whole backlog finished without extracting anything.
        var session = await NewSessionAsync();
        var older = SeedExistingSource("Kastor prep", new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = SeedExistingSource("Yndaros prep", new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));

        var view = await AddExistingAsync(session.Id, newer.Id, older.Id);

        Assert.Multiple(() =>
        {
            Assert.That(view.Items.Select(i => i.Title), Is.EqualTo(new[] { "Kastor prep", "Yndaros prep" }),
                "staged sources are appended in story order, whatever order they were chosen in");
            Assert.That(view.Items.Select(i => i.State),
                Is.EqualTo(new[] { ImportItemState.Waiting, ImportItemState.Waiting }));
            Assert.That(view.SettledCount, Is.EqualTo(0));
            Assert.That(view.CurrentItemId, Is.EqualTo(view.Items[0].Id));
        });
    }

    [Test]
    public async Task StartingARunOfExistingSources_DispatchesThroughReprocess()
    {
        // Processed is terminal for mark-ready, so an existing source can only be re-extracted
        // through the reprocess cascade.
        var session = await NewSessionAsync();
        var existing = SeedExistingSource("Kastor prep");
        await AddExistingAsync(session.Id, existing.Id);

        var started = await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(started.IsSuccess, Is.True, started.Error?.Message);
            Assert.That(_reprocess.Commands.Select(c => c.SourceId), Is.EqualTo(new[] { existing.Id }),
                "the existing source went through reprocess, not mark-ready");
            Assert.That(started.Value!.Items[0].State, Is.EqualTo(ImportItemState.Extracting));
        });
    }

    [Test]
    public async Task AWalkOfExistingSources_AdvancesOneNoteAtATime()
    {
        var session = await NewSessionAsync();
        var first = SeedExistingSource("Arc 1 prep", new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var second = SeedExistingSource("Arc 2 prep", new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        await AddExistingAsync(session.Id, first.Id, second.Id);

        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);
        CompleteExtraction(first.Id, openProposals: 2);

        // The first note still has proposals open, so the walk refuses to move on.
        var tooSoon = await _sut.AdvanceAsync(
            WorldId, session.Id, false, null, GmId, WorldRole.GM, CancellationToken.None);
        Assert.That(tooSoon.IsSuccess, Is.False, "advance must wait for the note on screen");
        Assert.That(tooSoon.Error!.Code, Is.EqualTo("item_not_ready"));

        ResolveAllProposals(first.Id);
        var advanced = await _sut.AdvanceAsync(
            WorldId, session.Id, false, null, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(advanced.IsSuccess, Is.True, advanced.Error?.Message);
            Assert.That(_reprocess.Commands.Select(c => c.SourceId), Is.EqualTo(new[] { first.Id, second.Id }));
            Assert.That(advanced.Value!.Items[0].State, Is.EqualTo(ImportItemState.Done));
            Assert.That(advanced.Value!.Items[1].State, Is.EqualTo(ImportItemState.Extracting));
        });
    }

    [Test]
    public async Task RemovingAStagedSource_LeavesTheSourceAlone()
    {
        var session = await NewSessionAsync();
        var existing = SeedExistingSource("Kastor prep");
        var view = await AddExistingAsync(session.Id, existing.Id);

        var removed = await _sut.RemoveItemAsync(
            WorldId, session.Id, view.Items[0].Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(removed.IsSuccess, Is.True, removed.Error?.Message);
            Assert.That(removed.Value!.Items, Is.Empty, "the note left the run");
            Assert.That(_sources.Sources.Any(s => s.Id == existing.Id), Is.True,
                "excluding a note from the run must never delete the GM's source");
            Assert.That(_sources.Sources.First(s => s.Id == existing.Id).ProcessingStatus,
                Is.EqualTo(SourceProcessingStatus.Processed), "nor change it");
        });
    }

    [Test]
    public async Task RemovingWorks_EvenOnceTheNoteHasBeenDispatched()
    {
        var session = await NewSessionAsync();
        var existing = SeedExistingSource("Kastor prep");
        var view = await AddExistingAsync(session.Id, existing.Id);
        await _sut.StartAsync(WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        var removed = await _sut.RemoveItemAsync(
            WorldId, session.Id, view.Items[0].Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(removed.IsSuccess, Is.True, removed.Error?.Message);
        Assert.That(_sources.Sources.Any(s => s.Id == existing.Id), Is.True);
    }

    [Test]
    public async Task DeletingTheNote_IsRefusedForASourceTheImportDidNotCreate()
    {
        var session = await NewSessionAsync();
        var existing = SeedExistingSource("Kastor prep");
        var view = await AddExistingAsync(session.Id, existing.Id);

        var deleted = await _sut.DeleteItemAsync(
            WorldId, session.Id, view.Items[0].Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(deleted.IsSuccess, Is.False);
            Assert.That(deleted.Error!.Code, Is.EqualTo("not_import_owned"));
            Assert.That(_sources.Sources.Any(s => s.Id == existing.Id), Is.True);
        });
    }

    [Test]
    public async Task DeletingTheNote_StillWorksForOneTypedIntoTheImport()
    {
        var session = await NewSessionAsync();
        var view = await AddNoteAsync(session.Id, "Mistyped note");
        var sourceId = view.Items[0].SourceId;

        var deleted = await _sut.DeleteItemAsync(
            WorldId, session.Id, view.Items[0].Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(deleted.IsSuccess, Is.True, deleted.Error?.Message);
            Assert.That(deleted.Value!.Items, Is.Empty);
            Assert.That(_sources.Sources.Any(s => s.Id == sourceId), Is.False,
                "a note typed into the import has no life outside it");
        });
    }

    [Test]
    public async Task StagingIsIdempotent_AndRefusesSourcesStoredWithoutExtraction()
    {
        var session = await NewSessionAsync();
        var existing = SeedExistingSource("Kastor prep");
        var optedOut = SeedExistingSource("Reference sheet", extractionEnabled: false);

        await AddExistingAsync(session.Id, existing.Id);
        var again = await AddExistingAsync(session.Id, existing.Id);

        Assert.That(again.Items, Has.Count.EqualTo(1), "staging the same source twice adds it once");

        var refused = await _sut.AddExistingSourcesAsync(
            WorldId, session.Id, [optedOut.Id], GmId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(refused.IsSuccess, Is.False);
            Assert.That(refused.Error!.Code, Is.EqualTo("not_extractable"));
        });
    }

    [Test]
    public async Task Candidates_ReportCanonContribution_NotProcessingStatus()
    {
        // Both sources read Processed. Only the reference count distinguishes one that has
        // contributed canon from one whose knowledge was wiped — which is the whole reason
        // this signal exists rather than showing ProcessingStatus.
        var session = await NewSessionAsync();
        var contributed = SeedExistingSource("Has canon");
        var wiped = SeedExistingSource("Wiped clean");

        _references.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = contributed.Id,
            TargetType = SourceReferenceTargetType.Artifact,
            TargetId = Guid.NewGuid(),
            Quote = "A quote",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await _sut.ListCandidatesAsync(
            WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var byId = result.Value!.ToDictionary(c => c.SourceId);

        Assert.Multiple(() =>
        {
            Assert.That(byId[contributed.Id].ExistingReferenceCount, Is.EqualTo(1));
            Assert.That(byId[wiped.Id].ExistingReferenceCount, Is.EqualTo(0));
            Assert.That(byId[contributed.Id].ProcessingStatus,
                Is.EqualTo(byId[wiped.Id].ProcessingStatus),
                "status cannot tell these apart, which is why the count is what the UI shows");
        });
    }

    [Test]
    public async Task Candidates_ExcludeSourcesAlreadyStagedAndThoseStoredWithoutExtraction()
    {
        var session = await NewSessionAsync();
        var staged = SeedExistingSource("Already in the run");
        var free = SeedExistingSource("Not yet staged");
        SeedExistingSource("Never extract me", extractionEnabled: false);

        await AddExistingAsync(session.Id, staged.Id);

        var result = await _sut.ListCandidatesAsync(
            WorldId, session.Id, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var candidates = result.Value!;

        Assert.Multiple(() =>
        {
            Assert.That(candidates.Select(c => c.Title), Does.Not.Contain("Never extract me"),
                "a source opted out of extraction is not stageable at all");
            Assert.That(candidates.First(c => c.SourceId == staged.Id).AlreadyStaged, Is.True);
            Assert.That(candidates.First(c => c.SourceId == free.Id).AlreadyStaged, Is.False);
        });
    }
}
