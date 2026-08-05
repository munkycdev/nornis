using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
/// Pins the contract the carve created: derivation writes source CONTENT (the
/// persist-before-continue that keeps redelivery from re-buying vision calls) but never
/// source STATUS — a returned outcome is a verdict for the orchestrator's one mapping.
/// Full transcription/derivation behavior is covered end-to-end in
/// ExtractionServiceTranscriptionTests and ExtractionServiceDerivedTextTests.
/// </summary>
[TestFixture]
public class SourceTextDerivationTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemorySourceAttachmentRepository _attachmentRepository = null!;
    private FakeBlobStorageService _blobStorage = null!;
    private FakeHandwritingTranscriptionClient _transcriptionClient = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private SourceTextDerivation _derivation = null!;

    private static readonly Guid WorldId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _attachmentRepository = new InMemorySourceAttachmentRepository();
        _blobStorage = new FakeBlobStorageService();
        _transcriptionClient = new FakeHandwritingTranscriptionClient();
        _budgetGuard = new FakeAiBudgetGuard();
        _derivation = new SourceTextDerivation(
            _sourceRepository,
            _attachmentRepository,
            _blobStorage,
            new FakePdfTextExtractor(),
            _transcriptionClient,
            new FakeImageReadingClient(),
            _budgetGuard,
            TestUsageRecorder.Wrap(new InMemoryAiUsageRecordRepository()),
            Options.Create(new ExtractionOptions
            {
                AiModel = "gpt-4o",
                AiEndpoint = "https://test.openai.azure.com/"
            }),
            NullLogger<SourceTextDerivation>.Instance);
    }

    private Source SeedHandwrittenSource()
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.HandwrittenNotes,
            Title = "Field notes",
            Body = null,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private SourceAttachment SeedStoredPage(Source source, bool withBlob)
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

        return attachment;
    }

    [Test]
    public async Task TranscriptionBudgetBlock_IsAVerdict_NotAStatusTransition()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source, withBlob: true);
        _budgetGuard.Exceeded = true;

        var outcome = await _derivation.TranscribeHandwrittenAsync(source, WorldId, CancellationToken.None);

        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(outcome.ErrorCategory, Is.EqualTo("BudgetExceeded"));
        Assert.That(_transcriptionClient.CallCount, Is.Zero, "blocked before the paid call");
        Assert.That(_sourceRepository.StatusTransitions, Is.Empty,
            "status transitions belong to the orchestrator's mapping, not to derivation");
    }

    [Test]
    public async Task Transcription_PersistsTheBodyBeforeContinuing_SoRedeliveryNeverRebuysIt()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source, withBlob: true);
        _transcriptionClient.MarkdownToReturn = "# Transcribed\n\nCaptain Voss was here.";

        var outcome = await _derivation.TranscribeHandwrittenAsync(source, WorldId, CancellationToken.None);

        Assert.That(outcome, Is.Null, "success continues the pipeline");
        var stored = (await _sourceRepository.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.Body, Is.EqualTo("# Transcribed\n\nCaptain Voss was here."));
        Assert.That(_sourceRepository.StatusTransitions, Is.Empty,
            "a content write is not a status write");
    }

    [Test]
    public async Task MissingPageBlob_IsAVerdict_NotAStatusTransition()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source, withBlob: false);

        var outcome = await _derivation.TranscribeHandwrittenAsync(source, WorldId, CancellationToken.None);

        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(outcome.ErrorCategory, Is.EqualTo(ErrorCategories.ValidationFailure));
        Assert.That(_sourceRepository.StatusTransitions, Is.Empty);
    }

    [Test]
    public async Task NothingToDerive_ContinuesThePipeline()
    {
        var source = SeedHandwrittenSource();
        source.Type = SourceType.Upload;

        var outcome = await _derivation.DeriveAttachmentTextAsync(source, WorldId, CancellationToken.None);

        Assert.That(outcome, Is.Null, "no stored files — the typed body or the empty-body path decides");
        Assert.That(source.DerivedText, Is.Null);
    }
}
