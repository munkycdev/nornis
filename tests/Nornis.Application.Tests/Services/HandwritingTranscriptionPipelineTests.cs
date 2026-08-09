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
/// Pins two contracts. The one inherited from the carve: this writes source CONTENT but
/// never source STATUS, so a returned failure is a verdict for the orchestrator's one
/// mapping. And the one that made it its own class: a source that already has a body is
/// never re-read, which is what lets a GM correct the reading on the capture page and send
/// the corrected text for extraction instead of the guess.
/// </summary>
[TestFixture]
public class HandwritingTranscriptionPipelineTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemorySourceAttachmentRepository _attachmentRepository = null!;
    private FakeBlobStorageService _blobStorage = null!;
    private FakeHandwritingTranscriptionClient _transcriptionClient = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private InMemoryAiUsageRecordRepository _usageRecords = null!;
    private HandwritingTranscriptionPipeline _pipeline = null!;

    private static readonly Guid WorldId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _attachmentRepository = new InMemorySourceAttachmentRepository();
        _blobStorage = new FakeBlobStorageService();
        _transcriptionClient = new FakeHandwritingTranscriptionClient();
        _budgetGuard = new FakeAiBudgetGuard();
        _usageRecords = new InMemoryAiUsageRecordRepository();
        _pipeline = new HandwritingTranscriptionPipeline(
            _sourceRepository,
            _attachmentRepository,
            _blobStorage,
            _transcriptionClient,
            _budgetGuard,
            TestUsageRecorder.Wrap(_usageRecords),
            new HandwritingTranscriptionSettings("nornis-extract", 60),
            NullLogger<HandwritingTranscriptionPipeline>.Instance);
    }

    private Source SeedHandwrittenSource(string? body = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.HandwrittenNotes,
            Title = "Field notes",
            Body = body,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private void SeedStoredPage(Source source, bool withBlob)
    {
        var attachment = new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            WorldId = WorldId,
            Kind = SourceAttachmentKind.PageImage,
            FileName = "page-1.png",
            ContentType = "image/png",
            SizeBytes = 3,
            BlobPath = $"worlds/{WorldId}/sources/{source.Id}/page-1.png",
            Ord = 0,
            Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _attachmentRepository.Seed(attachment);
        if (withBlob)
        {
            _blobStorage.Blobs[attachment.BlobPath] = ([1, 2, 3], "image/png");
        }
    }

    #region The guard that protects a correction

    [Test]
    public async Task ASourceWithABody_IsNotReadAgain_SoCorrectionsSurvive()
    {
        // The GM fixed "Captain Vass" to "Captain Voss" on the capture page; extraction must
        // run on what they approved, and must not spend a second vision call to undo it.
        var source = SeedHandwrittenSource(body: "Captain Voss was seen at the docks.");
        SeedStoredPage(source, withBlob: true);

        var result = await _pipeline.TranscribeAsync(source, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.Null);
        Assert.That(result.Markdown, Is.EqualTo("Captain Voss was seen at the docks."));
        Assert.That(_transcriptionClient.CallCount, Is.Zero, "no second paid read");
        var stored = (await _sourceRepository.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.Body, Is.EqualTo("Captain Voss was seen at the docks."),
            "the correction is still the body");
    }

    [Test]
    public async Task AWhitespaceBody_IsTreatedAsNoBody()
    {
        // "Empty" has to mean empty here: an editor the user cleared leaves whitespace, and a
        // source whose only content is a newline has nothing for extraction to read.
        var source = SeedHandwrittenSource(body: "   \n  ");
        SeedStoredPage(source, withBlob: true);
        _transcriptionClient.MarkdownToReturn = "# Session 4\n\nThe caravan never arrived.";

        var result = await _pipeline.TranscribeAsync(source, WorldId, CancellationToken.None);

        Assert.That(_transcriptionClient.CallCount, Is.EqualTo(1));
        Assert.That(result.Markdown, Is.EqualTo("# Session 4\n\nThe caravan never arrived."));
    }

    #endregion

    #region Verdicts, not status transitions

    [Test]
    public async Task BudgetBlock_IsAVerdict_NotAStatusTransition()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source, withBlob: true);
        _budgetGuard.Exceeded = true;

        var result = await _pipeline.TranscribeAsync(source, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(result.Failure!.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(result.Failure.ErrorCategory, Is.EqualTo("BudgetExceeded"));
        Assert.That(_transcriptionClient.CallCount, Is.Zero, "blocked before the paid call");
        Assert.That(_sourceRepository.StatusTransitions, Is.Empty,
            "status transitions belong to the orchestrator's mapping, not to this pipeline");
    }

    [Test]
    public async Task MissingPageBlob_IsAVerdict_NotAStatusTransition()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source, withBlob: false);

        var result = await _pipeline.TranscribeAsync(source, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(result.Failure!.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(result.Failure.ErrorCategory, Is.EqualTo(ErrorCategories.ValidationFailure));
        Assert.That(_sourceRepository.StatusTransitions, Is.Empty);
    }

    [Test]
    public async Task ATimedOutVisionCall_IsTransient_SoTheCallerCanRetry()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source, withBlob: true);
        _transcriptionClient.ExceptionToThrow = new TimeoutException("vision call timed out");

        var result = await _pipeline.TranscribeAsync(source, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(result.Failure!.Type, Is.EqualTo(OutcomeType.TransientFailure));
        Assert.That(result.Failure.ErrorCategory, Is.EqualTo(ErrorCategories.Timeout));
        Assert.That(_usageRecords.Records, Has.Count.EqualTo(1),
            "a failed paid attempt is still recorded");
        Assert.That(_usageRecords.Records[0].Succeeded, Is.False);
    }

    #endregion

    #region Nothing to read

    [Test]
    public async Task NoStoredPages_IsNeitherSuccessNorFailure()
    {
        // The two callers want different things from this: the queue continues to its
        // empty-body path, the API tells the user to add a photo. Both need it distinguishable
        // from pages that read as blank.
        var source = SeedHandwrittenSource();

        var result = await _pipeline.TranscribeAsync(source, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.Null);
        Assert.That(result.Markdown, Is.Null);
        Assert.That(_transcriptionClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task PagesThatReadAsBlank_AreASuccessWithNoText()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source, withBlob: true);
        _transcriptionClient.MarkdownToReturn = "   ";

        var result = await _pipeline.TranscribeAsync(source, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.Null);
        Assert.That(result.Markdown, Is.Empty, "read, but there was nothing on the page");
        var stored = (await _sourceRepository.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.Body, Is.Null, "nothing worth storing as a body");
    }

    #endregion

    [Test]
    public async Task ASuccessfulRead_PersistsTheBodyBeforeReturning()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source, withBlob: true);
        _transcriptionClient.MarkdownToReturn = "# Transcribed\n\nCaptain Voss was here.";

        var result = await _pipeline.TranscribeAsync(source, WorldId, CancellationToken.None);

        Assert.That(result.Failure, Is.Null);
        Assert.That(result.Markdown, Is.EqualTo("# Transcribed\n\nCaptain Voss was here."));
        var stored = (await _sourceRepository.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.Body, Is.EqualTo("# Transcribed\n\nCaptain Voss was here."),
            "persisted, so a redelivered message never re-buys the read");
        Assert.That(_sourceRepository.StatusTransitions, Is.Empty,
            "a content write is not a status write");
    }

    [Test]
    public async Task TheUsageRecord_NamesTheDeploymentTheHostActuallyCalled()
    {
        // A model name with no pricing entry records the call at $0, which would leave the
        // daily budget guard reading a spend that never rises. The settings exist to carry
        // the host's real deployment name this far.
        var source = SeedHandwrittenSource();
        SeedStoredPage(source, withBlob: true);

        await _pipeline.TranscribeAsync(source, WorldId, CancellationToken.None);

        Assert.That(_transcriptionClient.LastRequest!.Model, Is.EqualTo("nornis-extract"));
        Assert.That(_usageRecords.Records[0].Model, Is.EqualTo("nornis-extract"));
        Assert.That(_usageRecords.Records[0].OperationType,
            Is.EqualTo(AiOperationType.HandwritingTranscription));
    }
}
