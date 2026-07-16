using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Storage;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class LibraryIndexingServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemoryLibraryDocumentRepository _documents = null!;
    private InMemoryLibraryChunkRepository _chunks = null!;
    private FakeBlobStorageService _blobs = null!;
    private FakePdfTextExtractor _pdf = null!;
    private FakeEmbeddingClient _embeddings = null!;
    private InMemoryAiUsageRecordRepository _usage = null!;
    private LibraryIndexingService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        _chunks = new InMemoryLibraryChunkRepository();
        _blobs = new FakeBlobStorageService();
        _pdf = new FakePdfTextExtractor();
        _embeddings = new FakeEmbeddingClient();
        _usage = new InMemoryAiUsageRecordRepository();
        _sut = new LibraryIndexingService(_documents, _chunks, _blobs, _pdf, _embeddings,
            new FakeAiBudgetGuard(), _usage, Options.Create(new LibraryOptions()),
            NullLogger<LibraryIndexingService>.Instance);
    }

    private LibraryDocument SeedIndexingDocument()
    {
        var doc = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Title = "Forbidden Depths",
            FileName = "depths.pdf",
            ContentType = "application/pdf",
            SizeBytes = 100,
            BlobPath = $"worlds/{WorldId}/library/x/depths.pdf",
            Kind = LibraryDocumentKind.Sourcebook,
            Visibility = VisibilityScope.GMOnly,
            Status = LibraryDocumentStatus.Indexing,
            UploadedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _documents.Seed(doc);
        _blobs.Blobs[doc.BlobPath] = (new byte[] { 1, 2, 3 }, "application/pdf");
        return doc;
    }

    [Test]
    public async Task Process_HappyPath_ChunksEmbedsAndMarksIndexed()
    {
        var doc = SeedIndexingDocument();
        _pdf.Pages.AddRange([
            new PdfPageText(1, "At level 8 the characters reach the sunken temple."),
            new PdfPageText(2, "The temple guardian awakens."),
        ]);

        var outcome = await _sut.ProcessIndexingAsync(doc.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        var stored = _documents.Documents.Single();
        Assert.That(stored.Status, Is.EqualTo(LibraryDocumentStatus.Indexed));
        Assert.That(stored.PageCount, Is.EqualTo(2));
        Assert.That(stored.ChunkCount, Is.GreaterThan(0));
        Assert.That(_chunks.WritesByDocument[doc.Id], Has.Count.EqualTo(stored.ChunkCount));
        Assert.That(_usage.Records.Single().OperationType, Is.EqualTo(AiOperationType.Embedding));
        Assert.That(_usage.Records.Single().Model, Is.EqualTo("nornis-embed"));
        Assert.That(_usage.Records.Single().EstimatedCostUsd, Is.GreaterThan(0));
    }

    [Test]
    public async Task Process_DocumentGone_Skips()
    {
        var outcome = await _sut.ProcessIndexingAsync(Guid.NewGuid(), WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Skipped));
    }

    [Test]
    public async Task Process_NoExtractableText_MarksIndexFailed()
    {
        var doc = SeedIndexingDocument();
        _pdf.Pages.Add(new PdfPageText(1, "   "));

        var outcome = await _sut.ProcessIndexingAsync(doc.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(_documents.Documents.Single().Status, Is.EqualTo(LibraryDocumentStatus.IndexFailed));
        Assert.That(_documents.Documents.Single().ErrorMessage, Does.Contain("OCR"));
    }

    [Test]
    public async Task Process_RateLimitFromEmbeddings_IsTransient_DocStaysIndexing()
    {
        var doc = SeedIndexingDocument();
        _pdf.Pages.Add(new PdfPageText(1, "Some content."));
        _embeddings.ThrowOnEmbed = new InvalidOperationException("429 rate limit exceeded");

        var outcome = await _sut.ProcessIndexingAsync(doc.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.TransientFailure));
        Assert.That(_documents.Documents.Single().Status, Is.EqualTo(LibraryDocumentStatus.Indexing),
            "the queue redelivers; the document must not be failed");
    }

    [Test]
    public async Task Process_UnexpectedExtractionError_MarksIndexFailed()
    {
        var doc = SeedIndexingDocument();
        _pdf.ThrowOnExtract = new InvalidDataException("corrupt xref table");

        var outcome = await _sut.ProcessIndexingAsync(doc.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(_documents.Documents.Single().Status, Is.EqualTo(LibraryDocumentStatus.IndexFailed));
        Assert.That(_documents.Documents.Single().ErrorMessage, Does.Contain("corrupt"));
    }

    [Test]
    public async Task Process_BudgetExceeded_MarksIndexFailedWithGuidance()
    {
        var doc = SeedIndexingDocument();
        _sut = new LibraryIndexingService(_documents, _chunks, _blobs, _pdf, _embeddings,
            new FakeAiBudgetGuard { Exceeded = true }, _usage, Options.Create(new LibraryOptions()),
            NullLogger<LibraryIndexingService>.Instance);

        var outcome = await _sut.ProcessIndexingAsync(doc.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(_documents.Documents.Single().ErrorMessage, Does.Contain("budget"));
    }

    [Test]
    public async Task Process_ManyChunks_EmbeddedInBatches()
    {
        var doc = SeedIndexingDocument();
        var options = new LibraryOptions { MaxChunkChars = 50, OverlapChars = 0, EmbedBatchSize = 3 };
        _sut = new LibraryIndexingService(_documents, _chunks, _blobs, _pdf, _embeddings,
            new FakeAiBudgetGuard(), _usage, Options.Create(options),
            NullLogger<LibraryIndexingService>.Instance);
        _pdf.Pages.Add(new PdfPageText(1, string.Join("\n\n", Enumerable.Range(1, 10).Select(i => $"Paragraph {i} with words."))));

        var outcome = await _sut.ProcessIndexingAsync(doc.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_embeddings.Batches.Count, Is.GreaterThan(1), "chunks embed in batches");
        Assert.That(_embeddings.Batches.All(b => b.Count <= 3), Is.True);
    }
}
