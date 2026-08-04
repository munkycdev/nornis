using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Common;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public interface ILibraryIndexingService
{
    Task<ExtractionOutcome> ProcessIndexingAsync(Guid documentId, Guid worldId, CancellationToken ct);
}

/// <summary>
/// Worker-side pipeline: blob → PDF text → chunks → embeddings → vector rows. Reuses the
/// extraction outcome vocabulary so the queue consumer's complete/abandon semantics match.
/// </summary>
public class LibraryIndexingService : ILibraryIndexingService
{
    private readonly ILibraryDocumentRepository _documentRepository;
    private readonly ILibraryChunkRepository _chunkRepository;
    private readonly IBlobStorageService _blobStorage;
    private readonly IPdfTextExtractor _pdfTextExtractor;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly LibraryOptions _options;
    private readonly ILogger<LibraryIndexingService> _logger;

    public LibraryIndexingService(
        ILibraryDocumentRepository documentRepository,
        ILibraryChunkRepository chunkRepository,
        IBlobStorageService blobStorage,
        IPdfTextExtractor pdfTextExtractor,
        IEmbeddingClient embeddingClient,
        IAiBudgetGuard budgetGuard,
        IAiUsageRecorder usageRecorder,
        IOptions<LibraryOptions> options,
        ILogger<LibraryIndexingService> logger)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _blobStorage = blobStorage;
        _pdfTextExtractor = pdfTextExtractor;
        _embeddingClient = embeddingClient;
        _budgetGuard = budgetGuard;
        _usageRecorder = usageRecorder;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExtractionOutcome> ProcessIndexingAsync(Guid documentId, Guid worldId, CancellationToken ct)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, ct);
        if (document is null || document.WorldId != worldId)
        {
            return ExtractionOutcome.SkippedIdempotent("Document no longer exists.");
        }

        if (document.Status != LibraryDocumentStatus.Indexing)
        {
            return ExtractionOutcome.SkippedIdempotent($"Document is {document.Status}, not Indexing.");
        }

        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            return await FailAsync(document, "budget", "Daily AI budget exceeded — reindex tomorrow or raise the budget.", ct);
        }

        var stopwatch = Stopwatch.StartNew();

        // Outside the try on purpose: embedding runs batch by batch, and a failure part way
        // through means the earlier batches were billed. Redelivery then re-embeds the whole
        // document and pays for them again — so tokens spent before the failure have to be
        // recorded from the catches, which means they cannot be scoped to the happy path.
        var totalTokens = 0;
        try
        {
            IReadOnlyList<TextChunk> chunks;
            int pageCount;
            {
                IReadOnlyList<PdfPageText> pages;
                await using (var stream = await _blobStorage.OpenReadAsync(document.BlobPath, ct))
                {
                    pages = await _pdfTextExtractor.ExtractPagesAsync(stream, ct);
                }

                chunks = LibraryTextChunker.Chunk(pages, _options.MaxChunkChars, _options.OverlapChars);

                // Only the count outlives this block. Held for the whole embedding loop below
                // just to be read once at the end, the page text was a second full copy of the
                // document's text sitting beside the chunks.
                pageCount = pages.Count;
            }

            if (chunks.Count == 0)
            {
                return await FailAsync(document, "no_text",
                    "No extractable text — the PDF may be a pure scan (OCR is not supported yet).", ct);
            }

            var now = DateTimeOffset.UtcNow;
            var writtenChunks = 0;

            // The old shape accumulated every chunk AND its embedding in one list and wrote at
            // the end. A 1536-float vector is ~6 KB, so a large document held hundreds of
            // megabytes of embeddings on top of the text — with an uploaded PDF at the size cap
            // as the input, that was a member with an OOM switch for the worker.
            //
            // Deleting once up front keeps the replace semantics: from here to the status write
            // below, the document has no complete chunk set. Nothing reads it either — SearchAsync
            // only sees chunks whose document is Indexed, which this one is not yet.
            await _chunkRepository.DeleteForDocumentAsync(document.Id, ct);

            foreach (var batch in chunks.Chunk(_options.EmbedBatchSize))
            {
                var result = await _embeddingClient.EmbedAsync(batch.Select(c => c.Text).ToList(), ct);
                totalTokens += result.InputTokens;

                var writes = new List<LibraryChunkWrite>(batch.Length);
                for (var i = 0; i < batch.Length; i++)
                {
                    writes.Add(new LibraryChunkWrite(
                        new LibraryChunk
                        {
                            Id = Guid.NewGuid(),
                            DocumentId = document.Id,
                            WorldId = document.WorldId,
                            Ord = batch[i].Ord,
                            Page = batch[i].Page,
                            Text = batch[i].Text,
                            CreatedAt = now,
                        },
                        result.Embeddings[i]));
                }

                await _chunkRepository.AppendForDocumentAsync(writes, ct);
                writtenChunks += writes.Count;
            }

            // This is the commit point: until the status flips, the chunks written above are
            // invisible to retrieval, so a failure part way through leaves a document that reads
            // as un-indexed rather than as one with half its passages.
            document.Status = LibraryDocumentStatus.Indexed;
            document.ChunkCount = writtenChunks;
            document.PageCount = pageCount;
            document.ErrorMessage = null;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await _documentRepository.UpdateAsync(document, ct);

            await TrackUsageAsync(document, totalTokens, (int)stopwatch.ElapsedMilliseconds, succeeded: true, ct);

            _logger.LogInformation("Indexed library document {DocumentId}: {Pages} pages, {Chunks} chunks, {Tokens} tokens",
                document.Id, pageCount, writtenChunks, totalTokens);

            return ExtractionOutcome.Succeeded(Guid.Empty, writtenChunks);
        }
        catch (Exception ex) when (TransientFailureClassifier.IsTransient(ex))
        {
            _logger.LogWarning(ex, "Transient failure indexing {DocumentId}; message will be redelivered", document.Id);
            // Tokens already billed before the failure. The retry re-embeds from scratch and
            // pays again; recording both is what makes the budget guard see real spend.
            await TrackUsageAsync(document, totalTokens, (int)stopwatch.ElapsedMilliseconds, succeeded: false, ct);
            return ExtractionOutcome.Transient("transient", ex.Message);
        }
        catch (FileNotFoundException)
        {
            return await FailAsync(document, "blob_missing", "The uploaded file is missing from storage.", ct);
        }
        catch (Exception ex)
        {
            // The document may have been deleted mid-run — that's a skip, not a failure.
            if (await _documentRepository.GetByIdAsync(document.Id, ct) is null)
            {
                _logger.LogInformation("Library document {DocumentId} was deleted during indexing; skipping", document.Id);
                return ExtractionOutcome.SkippedIdempotent("Document was deleted during indexing.");
            }

            _logger.LogError(ex, "Indexing failed for library document {DocumentId}", document.Id);
            // Was hard-coded to 0, which discarded every batch embedded before the failure.
            await TrackUsageAsync(document, totalTokens, (int)stopwatch.ElapsedMilliseconds, succeeded: false, ct);
            return await FailAsync(document, "index_error", ex.Message.Truncate(1900), ct);
        }
    }

    private async Task<ExtractionOutcome> FailAsync(LibraryDocument document, string category, string message, CancellationToken ct)
    {
        try
        {
            document.Status = LibraryDocumentStatus.IndexFailed;
            document.ErrorMessage = message;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await _documentRepository.UpdateAsync(document, ct);
        }
        catch (Exception ex)
        {
            // Failing to record the failure (row deleted meanwhile) must not resurrect
            // the message — the outcome below still completes it.
            _logger.LogWarning(ex, "Could not persist IndexFailed for {DocumentId}", document.Id);
        }
        return ExtractionOutcome.NonTransient(category, message);
    }

    private async Task TrackUsageAsync(LibraryDocument document, int inputTokens, int durationMs, bool succeeded, CancellationToken ct)
    {
        try
        {
            var usage = new AiUsage
            {
                Model = _options.EmbeddingDeployment,
                InputTokens = inputTokens,
                OutputTokens = 0,
                TotalTokens = inputTokens,
                DurationMs = durationMs,
            };
            await _usageRecorder.RecordAsync(
                document.WorldId, document.UploadedByUserId, AiOperationType.Embedding,
                usage, succeeded, null, ct: ct);
        }
        catch (Exception ex)
        {
            // Usage tracking must never fail the pipeline.
            _logger.LogError(ex, "Failed to record embedding usage for {DocumentId}", document.Id);
        }
    }

    // Retry classification lives in TransientFailureClassifier — shared with extraction, which
    // previously disagreed with this method about whether a timeout was worth retrying.
}
