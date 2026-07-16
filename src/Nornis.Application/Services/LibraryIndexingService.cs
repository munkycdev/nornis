using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
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
    private readonly IAiUsageRecordRepository _usageRepository;
    private readonly LibraryOptions _options;
    private readonly ILogger<LibraryIndexingService> _logger;

    public LibraryIndexingService(
        ILibraryDocumentRepository documentRepository,
        ILibraryChunkRepository chunkRepository,
        IBlobStorageService blobStorage,
        IPdfTextExtractor pdfTextExtractor,
        IEmbeddingClient embeddingClient,
        IAiBudgetGuard budgetGuard,
        IAiUsageRecordRepository usageRepository,
        IOptions<LibraryOptions> options,
        ILogger<LibraryIndexingService> logger)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _blobStorage = blobStorage;
        _pdfTextExtractor = pdfTextExtractor;
        _embeddingClient = embeddingClient;
        _budgetGuard = budgetGuard;
        _usageRepository = usageRepository;
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
        try
        {
            IReadOnlyList<PdfPageText> pages;
            await using (var stream = await _blobStorage.OpenReadAsync(document.BlobPath, ct))
            {
                pages = await _pdfTextExtractor.ExtractPagesAsync(stream, ct);
            }

            var chunks = LibraryTextChunker.Chunk(pages, _options.MaxChunkChars, _options.OverlapChars);
            if (chunks.Count == 0)
            {
                return await FailAsync(document, "no_text",
                    "No extractable text — the PDF may be a pure scan (OCR is not supported yet).", ct);
            }

            var now = DateTimeOffset.UtcNow;
            var writes = new List<LibraryChunkWrite>(chunks.Count);
            var totalTokens = 0;

            foreach (var batch in chunks.Chunk(_options.EmbedBatchSize))
            {
                var result = await _embeddingClient.EmbedAsync(batch.Select(c => c.Text).ToList(), ct);
                totalTokens += result.InputTokens;

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
            }

            await _chunkRepository.ReplaceForDocumentAsync(document.Id, writes, ct);

            document.Status = LibraryDocumentStatus.Indexed;
            document.ChunkCount = writes.Count;
            document.PageCount = pages.Count;
            document.ErrorMessage = null;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await _documentRepository.UpdateAsync(document, ct);

            await TrackUsageAsync(document, totalTokens, (int)stopwatch.ElapsedMilliseconds, succeeded: true, ct);

            _logger.LogInformation("Indexed library document {DocumentId}: {Pages} pages, {Chunks} chunks, {Tokens} tokens",
                document.Id, pages.Count, writes.Count, totalTokens);

            return ExtractionOutcome.Succeeded(Guid.Empty, writes.Count);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            _logger.LogWarning(ex, "Transient failure indexing {DocumentId}; message will be redelivered", document.Id);
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
            await TrackUsageAsync(document, 0, (int)stopwatch.ElapsedMilliseconds, succeeded: false, ct);
            return await FailAsync(document, "index_error", Truncate(ex.Message, 1900), ct);
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
            var pricing = _options.ModelPricing.GetValueOrDefault(_options.EmbeddingDeployment);
            var cost = pricing is null ? 0m : inputTokens * pricing.InputPerMillionTokensUsd / 1_000_000m;

            await _usageRepository.CreateAsync(new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                WorldId = document.WorldId,
                UserId = document.UploadedByUserId,
                OperationType = AiOperationType.Embedding,
                Model = _options.EmbeddingDeployment,
                InputTokens = inputTokens,
                OutputTokens = 0,
                TotalTokens = inputTokens,
                EstimatedCostUsd = cost,
                DurationMs = durationMs,
                Succeeded = succeeded,
                CreatedAt = DateTimeOffset.UtcNow,
            }, ct);
        }
        catch (Exception ex)
        {
            // Usage tracking must never fail the pipeline.
            _logger.LogError(ex, "Failed to record embedding usage for {DocumentId}", document.Id);
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex.Message.Contains("429", StringComparison.Ordinal) ||
        ex.Message.Contains("503", StringComparison.Ordinal) ||
        ex.Message.Contains("service unavailable", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
        ex is TimeoutException;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
