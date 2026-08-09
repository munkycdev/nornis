using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The synchronous read the capture page performs so a GM sees the transcription before
/// extraction does. Two things belong to this class rather than to the pipeline it wraps:
/// who is allowed to ask, and what the answer looks like to somebody waiting on a phone
/// instead of to a queue deciding whether to redeliver.
/// </summary>
[TestFixture]
public class SourceTranscriptionServiceTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemorySourceAttachmentRepository _attachmentRepository = null!;
    private FakeBlobStorageService _blobStorage = null!;
    private FakeHandwritingTranscriptionClient _transcriptionClient = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private SourceTranscriptionService _service = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _attachmentRepository = new InMemorySourceAttachmentRepository();
        _blobStorage = new FakeBlobStorageService();
        _transcriptionClient = new FakeHandwritingTranscriptionClient();
        _budgetGuard = new FakeAiBudgetGuard();

        var pipeline = new HandwritingTranscriptionPipeline(
            _sourceRepository,
            _attachmentRepository,
            _blobStorage,
            _transcriptionClient,
            _budgetGuard,
            TestUsageRecorder.Wrap(new InMemoryAiUsageRecordRepository()),
            new HandwritingTranscriptionSettings("nornis-ask", 90),
            NullLogger<HandwritingTranscriptionPipeline>.Instance);

        _service = new SourceTranscriptionService(
            _sourceRepository, pipeline, NullLogger<SourceTranscriptionService>.Instance);
    }

    private Source Seed(
        SourceType type = SourceType.HandwrittenNotes,
        SourceProcessingStatus status = SourceProcessingStatus.Draft,
        Guid? worldId = null,
        string? body = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Type = type,
            Title = "Session 4 notes",
            Body = body,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = status,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = OwnerId
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private void SeedStoredPage(Source source)
    {
        var attachment = new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            WorldId = source.WorldId,
            Kind = SourceAttachmentKind.PageImage,
            FileName = "page-1.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 3,
            BlobPath = $"worlds/{source.WorldId}/sources/{source.Id}/page-1.jpg",
            Ord = 0,
            Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _attachmentRepository.Seed(attachment);
        _blobStorage.Blobs[attachment.BlobPath] = ([1, 2, 3], "image/jpeg");
    }

    private Task<Nornis.Application.Errors.AppResult<SourceTranscriptionResult>> TranscribeAsync(
        Source source, Guid? actingUserId = null, WorldRole role = WorldRole.GM) =>
        _service.TranscribeAsync(
            new TranscribeSourceCommand(source.Id, WorldId, actingUserId ?? OwnerId, role),
            CancellationToken.None);

    #region Who may ask

    [Test]
    public async Task AnObserver_IsRefused_BeforeAnythingIsRead()
    {
        var source = Seed();
        SeedStoredPage(source);

        var result = await TranscribeAsync(source, OtherUserId, WorldRole.Observer);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_transcriptionClient.CallCount, Is.Zero, "an unauthorized read is not a paid read");
    }

    [Test]
    public async Task APlayerWhoDoesNotOwnTheSource_IsRefused()
    {
        var source = Seed();
        SeedStoredPage(source);

        var result = await TranscribeAsync(source, OtherUserId, WorldRole.Player);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_transcriptionClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task ASourceInAnotherWorld_IsNotFound()
    {
        var source = Seed(worldId: Guid.NewGuid());
        SeedStoredPage(source);

        var result = await TranscribeAsync(source);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_transcriptionClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task ASourceAlreadyInThePipeline_IsRefused()
    {
        // Reading it now would rewrite the body that extraction is mid-flight on.
        var source = Seed(status: SourceProcessingStatus.Queued);
        SeedStoredPage(source);

        var result = await TranscribeAsync(source);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_status"));
    }

    #endregion

    #region What can be read

    [Test]
    public async Task ATypeThatIsNotHandwriting_IsRefused()
    {
        var source = Seed(type: SourceType.Upload);
        SeedStoredPage(source);

        var result = await TranscribeAsync(source);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_source_type"));
        Assert.That(_transcriptionClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task NoPhotosYet_AsksForOne_RatherThanReportingAFailedRead()
    {
        var source = Seed();

        var result = await TranscribeAsync(source);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("no_page_images"));
    }

    #endregion

    #region What the caller gets back

    [Test]
    public async Task ASuccessfulRead_ReturnsTheTextAndStoresIt()
    {
        var source = Seed();
        SeedStoredPage(source);
        _transcriptionClient.MarkdownToReturn = "# Session 4\n\nCaptain Voss at Black Harbor.";

        var result = await TranscribeAsync(source);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Markdown, Is.EqualTo("# Session 4\n\nCaptain Voss at Black Harbor."));
        var stored = (await _sourceRepository.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.Body, Is.EqualTo("# Session 4\n\nCaptain Voss at Black Harbor."),
            "the page reloads against the stored body, so it has to be there");
        Assert.That(stored.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Draft),
            "reading is not sending — extraction still waits for the GM");
    }

    [Test]
    public async Task PagesThatReadAsBlank_SucceedWithEmptyText()
    {
        // Not an error: retrying will produce the same nothing, and the page needs to say so
        // rather than offer a retry that cannot help.
        var source = Seed();
        SeedStoredPage(source);
        _transcriptionClient.MarkdownToReturn = "  ";

        var result = await TranscribeAsync(source);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Markdown, Is.Empty);
    }

    [Test]
    public async Task AskingTwice_ReturnsTheStoredTextWithoutPayingAgain()
    {
        var source = Seed(body: "Captain Voss was seen at the docks.");
        SeedStoredPage(source);

        var result = await TranscribeAsync(source);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Markdown, Is.EqualTo("Captain Voss was seen at the docks."));
        Assert.That(_transcriptionClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task ASpentBudget_KeepsTheGuardsOwnCodeAndStatus()
    {
        // One condition, one code, whether it surfaces here or out of Ask — the page can show
        // the same message either way.
        var source = Seed();
        SeedStoredPage(source);
        _budgetGuard.Exceeded = true;

        var result = await TranscribeAsync(source);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(429));
        Assert.That(result.Error.Code, Is.EqualTo("ai_budget_exceeded"));
    }

    [Test]
    public async Task ATransientAiFailure_IsRetryable()
    {
        var source = Seed();
        SeedStoredPage(source);
        _transcriptionClient.ExceptionToThrow = new TimeoutException("vision call timed out");

        var result = await TranscribeAsync(source);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(503));
        Assert.That(result.Error.Code, Is.EqualTo("ai_unavailable"));
    }

    [Test]
    public async Task AMissingPageBlob_IsTheCallersProblemToFix()
    {
        var source = Seed();
        var attachment = new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            WorldId = WorldId,
            Kind = SourceAttachmentKind.PageImage,
            FileName = "page-1.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 3,
            BlobPath = "worlds/missing/page-1.jpg",
            Ord = 0,
            Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _attachmentRepository.Seed(attachment);

        var result = await TranscribeAsync(source);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("validation_error"));
    }

    #endregion
}
