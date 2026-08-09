using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Turns an Image or Upload source's attachments into text the extraction prompt can carry:
/// PDF text, file contents, and one batched vision read over the images. Owned by
/// <see cref="ExtractionService"/> and carved out of it — the four-pipeline class had grown
/// to the point where its size was shaping API decisions. Handwriting transcription was
/// carved out again, into <see cref="HandwritingTranscriptionPipeline"/>, when it gained a
/// caller outside the queue.
///
/// The contract that makes the carve safe: this class writes source CONTENT
/// (UpdateDerivedTextAsync — the persist-before-continue that keeps a redelivered message
/// from re-buying a vision call) but never source STATUS. A returned outcome is a verdict,
/// not a transition; ProcessingStatus moves only in the orchestrator's one mapping, so the
/// state machine stays whole in one file.
///
/// Returns null to mean "continue the pipeline" — text was derived and persisted, or there
/// was nothing to derive and the empty-body path decides.
/// </summary>
public class SourceTextDerivation
{
    private readonly ISourceRepository _sourceRepository;
    private readonly ISourceAttachmentRepository _sourceAttachmentRepository;
    private readonly IBlobStorageService _blobStorage;
    private readonly IPdfTextExtractor _pdfTextExtractor;
    private readonly IImageReadingClient _imageReadingClient;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly ExtractionOptions _options;
    private readonly ILogger<SourceTextDerivation> _logger;

    public SourceTextDerivation(
        ISourceRepository sourceRepository,
        ISourceAttachmentRepository sourceAttachmentRepository,
        IBlobStorageService blobStorage,
        IPdfTextExtractor pdfTextExtractor,
        IImageReadingClient imageReadingClient,
        IAiBudgetGuard budgetGuard,
        IAiUsageRecorder usageRecorder,
        IOptions<ExtractionOptions> options,
        ILogger<SourceTextDerivation> logger)
    {
        _sourceRepository = sourceRepository;
        _sourceAttachmentRepository = sourceAttachmentRepository;
        _blobStorage = blobStorage;
        _pdfTextExtractor = pdfTextExtractor;
        _imageReadingClient = imageReadingClient;
        _budgetGuard = budgetGuard;
        _usageRecorder = usageRecorder;
        _options = options.Value;
        _logger = logger;
    }

    public const int MaxComposedBodyChars = SourceService.MaxBodyChars;

    /// <summary>Compose typed notes + derived text in memory only: Body stays the user's.</summary>
    public static string ComposeEffectiveBody(string? body, string derivedText)
    {
        var composed = string.IsNullOrWhiteSpace(body)
            ? derivedText
            : $"{body}\n\n{derivedText}";

        return composed.Length <= MaxComposedBodyChars
            ? composed
            : composed[..MaxComposedBodyChars];
    }

    /// <summary>
    /// Derives text from an Image/Upload source's attachments: PDF text via PdfPig,
    /// text files read verbatim, and one batched vision read over the images. Returns
    /// null to continue the pipeline (derived text persisted, or nothing to derive),
    /// or a terminal outcome on failure.
    /// </summary>
    public async Task<ExtractionOutcome?> DeriveAttachmentTextAsync(Source source, Guid worldId, CancellationToken ct)
    {
        var files = (await _sourceAttachmentRepository.ListBySourceAsync(source.Id, ct))
            .Where(a => a.Kind is SourceAttachmentKind.ImageFile or SourceAttachmentKind.Document)
            .Where(a => a.Status == SourceAttachmentStatus.Stored)
            .OrderBy(a => a.Ord)
            .ToList();

        if (files.Count == 0)
        {
            return null; // nothing to derive — typed body (or the empty-body path) decides
        }

        var sections = new List<(int Ord, string Text)>();
        var images = new List<ImageToRead>();
        var firstImageOrd = int.MaxValue;

        foreach (var file in files)
        {
            try
            {
                if (string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                {
                    await using var stream = await _blobStorage.OpenReadAsync(file.BlobPath, ct);
                    IReadOnlyList<PdfPageText> pdfPages;
                    try
                    {
                        pdfPages = await _pdfTextExtractor.ExtractPagesAsync(stream, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "PDF text extraction failed. SourceId={SourceId}, File={FileName}", source.Id, file.FileName);
                        return ExtractionOutcome.NonTransient(ErrorCategories.ValidationFailure,
                            $"Could not extract text from '{file.FileName}' — is it a digital (non-scanned) PDF?");
                    }

                    var text = string.Join("\n\n", pdfPages.Select(p => p.Text)).Trim();
                    if (text.Length > 0)
                    {
                        sections.Add((file.Ord, $"### Extracted from {file.FileName}\n\n{text}"));
                    }
                }
                else if (file.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                {
                    await using var stream = await _blobStorage.OpenReadAsync(file.BlobPath, ct);
                    using var reader = new StreamReader(stream);
                    var text = (await reader.ReadToEndAsync(ct)).Trim();
                    if (text.Length > 0)
                    {
                        sections.Add((file.Ord, $"### Extracted from {file.FileName}\n\n{text}"));
                    }
                }
                else if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    await using var stream = await _blobStorage.OpenReadAsync(file.BlobPath, ct);
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, ct);
                    images.Add(new ImageToRead(buffer.ToArray(), file.ContentType, file.FileName));
                    firstImageOrd = Math.Min(firstImageOrd, file.Ord);
                }
            }
            catch (FileNotFoundException)
            {
                _logger.LogError(
                    "Attachment blob missing. SourceId={SourceId}, BlobPath={BlobPath}", source.Id, file.BlobPath);
                return ExtractionOutcome.NonTransient(ErrorCategories.ValidationFailure,
                    $"File '{file.FileName}' is missing from storage.");
            }
        }

        if (images.Count > 0)
        {
            // Vision is an AI spend of its own; gate it like extraction.
            var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
            if (budgetError is not null)
            {
                _logger.LogWarning(
                    "Image reading blocked by AI budget. SourceId={SourceId}, WorldId={WorldId}", source.Id, worldId);
                return ExtractionOutcome.NonTransient(ErrorCategories.BudgetExceeded, budgetError.Message);
            }

            ImageReadingResponse response;
            try
            {
                response = await _imageReadingClient.ReadAsync(new ImageReadingRequest
                {
                    Images = images,
                    Model = _options.AiModel,
                    TimeoutSeconds = _options.AiTimeoutSeconds
                }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException ex)
            {
                await TrackVisionUsageAsync(source, worldId, null, false, ErrorCategories.Timeout, ct);
                return ExtractionOutcome.Transient(ErrorCategories.Timeout, ex.Message);
            }
            catch (Exception ex) when (TransientFailureClassifier.IsPermanentHttpFailure(ex))
            {
                _logger.LogError(ex, "Permanent image reading failure. SourceId={SourceId}", source.Id);
                await TrackVisionUsageAsync(source, worldId, null, false, ErrorCategories.AiCallFailure, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
            }
            catch (Exception ex) when (ex is AiHttpException or HttpRequestException)
            {
                _logger.LogWarning(ex, "Transient image reading failure. SourceId={SourceId}", source.Id);
                await TrackVisionUsageAsync(source, worldId, null, false, ErrorCategories.TransientError, ct);
                return ExtractionOutcome.Transient(ErrorCategories.TransientError, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected image reading failure. SourceId={SourceId}", source.Id);
                await TrackVisionUsageAsync(source, worldId, null, false, ErrorCategories.AiCallFailure, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
            }

            await TrackVisionUsageAsync(source, worldId, response.Usage, true, null, ct);

            if (!string.IsNullOrWhiteSpace(response.Markdown))
            {
                // The client already emits "## {filename}" sections per image.
                sections.Add((firstImageOrd, response.Markdown.Trim()));
            }
        }

        var derived = string.Join("\n\n", sections.OrderBy(s => s.Ord).Select(s => s.Text)).Trim();
        if (derived.Length == 0)
        {
            return null; // blank files — the empty-body path (or the typed body) takes it
        }

        // Keep the composed prompt within the body ceiling; typed notes win the budget.
        var available = MaxComposedBodyChars - (source.Body?.Length ?? 0) - 2;
        const string truncationMarker = "\n\n[Extracted content truncated]";
        if (available <= truncationMarker.Length)
        {
            derived = "[Extracted content omitted — the typed body already fills the source]";
        }
        else if (derived.Length > available)
        {
            derived = derived[..(available - truncationMarker.Length)] + truncationMarker;
        }

        // Persist before extracting: extraction may fail and retry, and the derivation
        // (especially the vision read) must not be re-bought on redelivery.
        await _sourceRepository.UpdateDerivedTextAsync(source.Id, derived, ct);
        source.DerivedText = derived;

        _logger.LogInformation(
            "Attachment text derived. SourceId={SourceId}, Files={Files}, Chars={Chars}",
            source.Id, files.Count, derived.Length);

        return null;
    }

    private Task TrackVisionUsageAsync(
        Source source, Guid worldId, AiUsage? usage,
        bool succeeded, string? errorCode, CancellationToken ct) =>
        _usageRecorder.RecordAsync(
            worldId, null, AiOperationType.ImageReading, usage,
            succeeded, errorCode, sourceId: source.Id, fallbackModel: _options.AiModel, ct: ct);
}
