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
/// The Image/Upload derivation step: PDF text (PdfPig), text files read verbatim, and a
/// single batched vision read over images — composed into DerivedText, persisted before
/// extraction so redelivery never re-buys it, then combined with the typed body.
/// </summary>
[TestFixture]
[Category("Feature: file-uploads")]
public class ExtractionServiceDerivedTextTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryAiUsageRecordRepository _usageRepo = null!;
    private InMemorySourceAttachmentRepository _attachmentRepo = null!;
    private FakeBlobStorageService _blob = null!;
    private FakePdfTextExtractor _pdf = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private FakeImageReadingClient _imageClient = null!;
    private FakeAiBudgetGuard _budget = null!;
    private ExtractionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _usageRepo = new InMemoryAiUsageRecordRepository();
        _attachmentRepo = new InMemorySourceAttachmentRepository();
        _blob = new FakeBlobStorageService();
        _pdf = new FakePdfTextExtractor();
        _aiClient = new FakeAiExtractionClient();
        _imageClient = new FakeImageReadingClient();
        _budget = new FakeAiBudgetGuard();

        var options = new ExtractionOptions
        {
            AiModel = "nornis-extract",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["nornis-extract"] = new() { InputPerMillionTokensUsd = 2.50m, OutputPerMillionTokensUsd = 15.00m }
            }
        };

        _sut = new ExtractionService(
            _sourceRepo,
            new InMemoryCampaignRepository(),
            _batchRepo,
            new InMemoryReviewProposalRepository(),
            new InMemorySourceReferenceRepository(),
            _usageRepo,
            new InMemoryArtifactRepository(),
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            _attachmentRepo,
            new InMemoryMapPlacemarkRepository(),
            _blob,
            _pdf,
            _aiClient,
            new FakeHandwritingTranscriptionClient(),
            _imageClient,
            new FakeMapExtractionClient(),
            _budget,
            new FakeUnitOfWork(),
            Options.Create(options),
            NullLogger<ExtractionService>.Instance);
    }

    private Source SeedSource(SourceType type, string? body = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = type,
            Title = "Handout",
            Body = body,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _sourceRepo.Seed(source);
        return source;
    }

    private void SeedFile(Source source, SourceAttachmentKind kind, string fileName, string contentType, int ord = 0)
    {
        var blobPath = $"worlds/{WorldId}/sources/{source.Id}/{ord:D3}-{fileName}";
        _blob.Blobs[blobPath] = (System.Text.Encoding.UTF8.GetBytes("file bytes"), contentType);
        _attachmentRepo.Seed(new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            WorldId = WorldId,
            Kind = kind,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = 10,
            BlobPath = blobPath,
            Ord = ord,
            Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private void ConfigureAiEmpty() => _aiClient.SetupSuccess(new AiExtractionResponse
    {
        Proposals = [], InputTokens = 10, OutputTokens = 5, TotalTokens = 15, DurationMs = 100, Model = "nornis-extract"
    });

    [Test]
    public async Task Upload_ComposesPdfTextAndVisionRead_PersistsBeforeExtracting()
    {
        var source = SeedSource(SourceType.Upload, body: "My notes.");
        SeedFile(source, SourceAttachmentKind.Document, "handout.pdf", "application/pdf", ord: 0);
        SeedFile(source, SourceAttachmentKind.Document, "banner.png", "image/png", ord: 1);
        _pdf.Pages.Add(new Nornis.Application.Storage.PdfPageText(1, "The siege of Kastor."));
        _imageClient.MarkdownToReturn = "## banner.png\n\nHouse Voss sigil.";
        ConfigureAiEmpty();

        await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        var stored = (await _sourceRepo.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.DerivedText, Does.Contain("The siege of Kastor."));
        Assert.That(stored.DerivedText, Does.Contain("House Voss sigil."));
        // Body is composed only in memory (the service never calls UpdateBodyAsync); with
        // EF's AsNoTracking the persisted Body stays "My notes." The InMemory fake shares
        // the reference, so we verify the separation via DerivedText + the AI request below.

        // Extraction ran on typed body + derived text composed in memory.
        var request = _aiClient.Requests.Single();
        Assert.That(request.SourceBody, Does.Contain("My notes."));
        Assert.That(request.SourceBody, Does.Contain("The siege of Kastor."));
        Assert.That(request.SourceBody, Does.Contain("House Voss sigil."));
    }

    [Test]
    public async Task Image_VisionReadOnly_Derives()
    {
        var source = SeedSource(SourceType.Image);
        SeedFile(source, SourceAttachmentKind.ImageFile, "map-art.png", "image/png");
        _imageClient.MarkdownToReturn = "## map-art.png\n\nA fortress on a cliff.";
        ConfigureAiEmpty();

        await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(_imageClient.CallCount, Is.EqualTo(1));
        var stored = (await _sourceRepo.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.DerivedText, Does.Contain("A fortress on a cliff."));
    }

    [Test]
    public async Task DerivedTextAlreadySet_SkipsDerivation_OnRedelivery()
    {
        var source = SeedSource(SourceType.Upload, body: "notes");
        source.DerivedText = "Already derived.";
        SeedFile(source, SourceAttachmentKind.Document, "banner.png", "image/png");
        ConfigureAiEmpty();

        await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(_imageClient.CallCount, Is.Zero, "no vision call — derivation is idempotent on redelivery");
        var request = _aiClient.Requests.Single();
        Assert.That(request.SourceBody, Does.Contain("Already derived."));
    }

    [Test]
    public async Task PdfOnly_NeedsNoBudgetGate()
    {
        var source = SeedSource(SourceType.Upload);
        SeedFile(source, SourceAttachmentKind.Document, "handout.pdf", "application/pdf");
        _pdf.Pages.Add(new Nornis.Application.Storage.PdfPageText(1, "Digital text."));
        _budget.Exceeded = true; // would block an AI call, but PDF extraction is not one
        ConfigureAiEmpty();

        var outcome = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        // Budget only blocks at the extraction AI call, not PDF text extraction.
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        var stored = (await _sourceRepo.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.DerivedText, Does.Contain("Digital text."), "PDF text was derived before the budget gate");
    }

    [Test]
    public async Task ImageReadingBudgetBlocked_FailsSource_NoDerivation()
    {
        var source = SeedSource(SourceType.Image);
        SeedFile(source, SourceAttachmentKind.ImageFile, "art.png", "image/png");
        _budget.Exceeded = true;

        var outcome = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(_imageClient.CallCount, Is.Zero);
        var stored = (await _sourceRepo.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed));
        Assert.That(stored.DerivedText, Is.Null);
    }

    [Test]
    public async Task VisionTimeout_IsTransient_SourceBackToQueued()
    {
        var source = SeedSource(SourceType.Image);
        SeedFile(source, SourceAttachmentKind.ImageFile, "art.png", "image/png");
        _imageClient.ExceptionToThrow = new TimeoutException("slow");

        var outcome = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.TransientFailure));
        var stored = (await _sourceRepo.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued), "transient failure returns to Queued for redelivery");
        Assert.That(_usageRepo.Records.Any(r => r.OperationType == AiOperationType.ImageReading && !r.Succeeded), Is.True);
    }

    [Test]
    public async Task Pdf_ExtractionFailure_FailsSource()
    {
        var source = SeedSource(SourceType.Upload);
        SeedFile(source, SourceAttachmentKind.Document, "scan.pdf", "application/pdf");
        _pdf.ThrowOnExtract = new InvalidOperationException("scanned image, no text layer");

        var outcome = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        var stored = (await _sourceRepo.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed));
    }

    [Test]
    public async Task TextFile_ReadVerbatim()
    {
        var source = SeedSource(SourceType.Upload);
        var blobPath = $"worlds/{WorldId}/sources/{source.Id}/000-notes.txt";
        _blob.Blobs[blobPath] = (System.Text.Encoding.UTF8.GetBytes("Plain text lore about Ironhold."), "text/plain");
        _attachmentRepo.Seed(new SourceAttachment
        {
            Id = Guid.NewGuid(), SourceId = source.Id, WorldId = WorldId,
            Kind = SourceAttachmentKind.Document, FileName = "notes.txt", ContentType = "text/plain",
            SizeBytes = 30, BlobPath = blobPath, Ord = 0, Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        ConfigureAiEmpty();

        await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(_imageClient.CallCount, Is.Zero, "text files need no vision call");
        var stored = (await _sourceRepo.GetByIdAsync(source.Id, CancellationToken.None))!;
        Assert.That(stored.DerivedText, Does.Contain("Plain text lore about Ironhold."));
    }

    [Test]
    public async Task ImageReadingUsage_Recorded_OnSuccess()
    {
        var source = SeedSource(SourceType.Image);
        SeedFile(source, SourceAttachmentKind.ImageFile, "art.png", "image/png");
        ConfigureAiEmpty();

        await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(_usageRepo.Records.Any(r => r.OperationType == AiOperationType.ImageReading && r.Succeeded), Is.True);
    }
}
