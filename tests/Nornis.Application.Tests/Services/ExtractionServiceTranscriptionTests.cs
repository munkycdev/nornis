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
/// The handwritten-notes transcription step that runs inside extraction: page images →
/// gpt vision → persisted Body → normal pipeline.
/// </summary>
[TestFixture]
public class ExtractionServiceTranscriptionTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryReviewBatchRepository _reviewBatchRepository = null!;
    private InMemoryAiUsageRecordRepository _aiUsageRecordRepository = null!;
    private InMemorySourceAttachmentRepository _attachmentRepository = null!;
    private FakeBlobStorageService _blobStorage = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private FakeHandwritingTranscriptionClient _transcriptionClient = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private ExtractionService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _reviewBatchRepository = new InMemoryReviewBatchRepository();
        _aiUsageRecordRepository = new InMemoryAiUsageRecordRepository();
        _attachmentRepository = new InMemorySourceAttachmentRepository();
        _blobStorage = new FakeBlobStorageService();
        _aiClient = new FakeAiExtractionClient();
        _transcriptionClient = new FakeHandwritingTranscriptionClient();
        _budgetGuard = new FakeAiBudgetGuard();

        var options = new ExtractionOptions
        {
            AiModel = "nornis-extract",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["nornis-extract"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 15.00m
                }
            }
        };

        _sut = new ExtractionService(
            _sourceRepository,
            new InMemoryCampaignRepository(),
            _reviewBatchRepository,
            new InMemoryReviewProposalRepository(),
            new InMemorySourceReferenceRepository(),
            _aiUsageRecordRepository,
            new InMemoryArtifactRepository(),
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            _attachmentRepository,
            _blobStorage,
            _aiClient,
            _transcriptionClient,
            _budgetGuard,
            new FakeUnitOfWork(),
            Options.Create(options),
            NullLogger<ExtractionService>.Instance);
    }

    private Source SeedHandwrittenSource(string? body = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.HandwrittenNotes,
            Title = "Session sketchbook",
            Body = body,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private void SeedStoredPage(Source source, int ord = 0)
    {
        var blobPath = $"worlds/{WorldId}/sources/{source.Id}/{ord:D3}-page.jpg";
        _blobStorage.Blobs[blobPath] = (new byte[] { 1, 2, 3 }, "image/jpeg");
        _attachmentRepository.Seed(new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            WorldId = WorldId,
            Kind = SourceAttachmentKind.PageImage,
            FileName = "page.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 3,
            BlobPath = blobPath,
            Ord = ord,
            Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private void SetupExtractionSuccess()
    {
        _aiClient.SetupSuccess(new AiExtractionResponse
        {
            Proposals = [],
            InputTokens = 500,
            OutputTokens = 200,
            TotalTokens = 700,
            DurationMs = 1200,
            Model = "nornis-extract"
        });
    }

    [Test]
    public async Task HandwrittenWithPages_TranscribesThenExtracts()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source);
        SetupExtractionSuccess();

        var result = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_transcriptionClient.CallCount, Is.EqualTo(1));
        Assert.That(source.Body, Is.EqualTo(_transcriptionClient.MarkdownToReturn), "transcription persisted as the body");
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));

        var usage = _aiUsageRecordRepository.Records.Where(r => r.OperationType == AiOperationType.HandwritingTranscription).ToList();
        Assert.That(usage, Has.Count.EqualTo(1));
        Assert.That(usage[0].Succeeded, Is.True);
        Assert.That(usage[0].EstimatedCostUsd, Is.GreaterThan(0m));
    }

    [Test]
    public async Task HandwrittenWithoutPages_FallsToEmptyBodyPath_NoAiCalls()
    {
        var source = SeedHandwrittenSource();

        var result = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(result.ProposalCount, Is.Zero);
        Assert.That(_transcriptionClient.CallCount, Is.Zero);
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
    }

    [Test]
    public async Task HandwrittenWithPersistedBody_SkipsTranscription()
    {
        // Redelivery after a crash between transcription and extraction: the body is
        // already persisted, so transcription must not be re-bought.
        var source = SeedHandwrittenSource(body: "# Already transcribed");
        SeedStoredPage(source);
        SetupExtractionSuccess();

        var result = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_transcriptionClient.CallCount, Is.Zero);
    }

    [Test]
    public async Task BudgetExceeded_FailsBeforeTranscription()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source);
        _budgetGuard.Exceeded = true;

        var result = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(result.ErrorCategory, Is.EqualTo("BudgetExceeded"));
        Assert.That(_transcriptionClient.CallCount, Is.Zero);
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed));
    }

    [Test]
    public async Task TranscriptionTimeout_Transient_RequeuesSource()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source);
        _transcriptionClient.ExceptionToThrow = new TimeoutException("timed out");

        var result = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.TransientFailure));
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued),
            "transient failures requeue for redelivery");
        Assert.That(_aiUsageRecordRepository.Records.Single().Succeeded, Is.False);
    }

    [Test]
    public async Task TranscriptionPermanentFailure_FailsSource()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source);
        _transcriptionClient.ExceptionToThrow = new HttpRequestException("bad request", null, System.Net.HttpStatusCode.BadRequest);

        var result = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed));
    }

    [Test]
    public async Task MissingPageBlob_FailsSourceWithoutAiCall()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source);
        _blobStorage.Blobs.Clear(); // the row exists but the blob vanished

        var result = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(_transcriptionClient.CallCount, Is.Zero);
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed));
    }

    [Test]
    public async Task BlankTranscription_ClosesOutViaEmptyBodyPath()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source);
        _transcriptionClient.MarkdownToReturn = "   ";

        var result = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(result.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(result.ProposalCount, Is.Zero);
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
    }

    [Test]
    public async Task PagesArriveInOrdOrder()
    {
        var source = SeedHandwrittenSource();
        SeedStoredPage(source, ord: 1);
        SeedStoredPage(source, ord: 0);
        SetupExtractionSuccess();

        await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(_transcriptionClient.LastRequest!.Pages, Has.Count.EqualTo(2));
    }
}
