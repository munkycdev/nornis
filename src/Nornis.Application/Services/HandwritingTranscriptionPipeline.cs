using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// What a handwriting transcription produced. Exactly one of three cases, mirroring
/// <see cref="MapExtractionResult"/>: no stored page images (both properties null — the
/// extraction orchestrator files an empty batch, the API tells the user to add a photo),
/// transcribed text, or a terminal failure verdict.
///
/// <see cref="Markdown"/> being empty is a success, not a miss: blank pages transcribe to
/// nothing, and both callers already have a path for a source with no text.
/// </summary>
public sealed class HandwritingTranscriptionResult
{
    private HandwritingTranscriptionResult(string? markdown, ExtractionOutcome? failure)
    {
        Markdown = markdown;
        Failure = failure;
    }

    public static readonly HandwritingTranscriptionResult NoPages = new(null, null);

    public static HandwritingTranscriptionResult Transcribed(string markdown) => new(markdown, null);

    public static HandwritingTranscriptionResult Failed(ExtractionOutcome outcome) => new(null, outcome);

    public string? Markdown { get; }

    public ExtractionOutcome? Failure { get; }
}

/// <summary>
/// Vision transcription of a handwritten source's page images into its body.
///
/// Its own class rather than a third method on <see cref="SourceTextDerivation"/> because
/// it acquired a second caller in a second host. The API transcribes on demand — the GM
/// reads what the model made of their handwriting and corrects it before extraction turns
/// it into proposals — and a host that only ever transcribes should not have to register a
/// PDF extractor and an image lore-reader to satisfy a constructor, which is how it would
/// come to advertise two capabilities it does not have.
///
/// Same contract as its siblings: this class writes source CONTENT (the
/// persist-before-continue that keeps a redelivered message from re-buying the vision call)
/// but never source STATUS. It returns a verdict; every ProcessingStatus transition stays
/// in <see cref="ExtractionService"/>'s one mapping.
/// </summary>
public class HandwritingTranscriptionPipeline
{
    private readonly ISourceRepository _sourceRepository;
    private readonly ISourceAttachmentRepository _sourceAttachmentRepository;
    private readonly IBlobStorageService _blobStorage;
    private readonly IHandwritingTranscriptionClient _transcriptionClient;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly HandwritingTranscriptionSettings _settings;
    private readonly ILogger<HandwritingTranscriptionPipeline> _logger;

    public HandwritingTranscriptionPipeline(
        ISourceRepository sourceRepository,
        ISourceAttachmentRepository sourceAttachmentRepository,
        IBlobStorageService blobStorage,
        IHandwritingTranscriptionClient transcriptionClient,
        IAiBudgetGuard budgetGuard,
        IAiUsageRecorder usageRecorder,
        HandwritingTranscriptionSettings settings,
        ILogger<HandwritingTranscriptionPipeline> logger)
    {
        _sourceRepository = sourceRepository;
        _sourceAttachmentRepository = sourceAttachmentRepository;
        _blobStorage = blobStorage;
        _transcriptionClient = transcriptionClient;
        _budgetGuard = budgetGuard;
        _usageRecorder = usageRecorder;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Vision-transcribes a handwritten source's page images into its Body.
    ///
    /// The "already has a body, don't buy this again" guard lives here rather than at the
    /// call site, because the extraction pipeline is no longer the only caller: the API
    /// transcribes on demand so the GM can correct the reading before extraction runs, and
    /// a guard left in the pipeline would have let that second caller overwrite the
    /// corrections it exists to collect. It is still the same guard that keeps a
    /// redelivered queue message from re-buying the vision call.
    /// </summary>
    public async Task<HandwritingTranscriptionResult> TranscribeAsync(
        Source source, Guid worldId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(source.Body))
        {
            return HandwritingTranscriptionResult.Transcribed(source.Body);
        }

        var pages = (await _sourceAttachmentRepository.ListBySourceAsync(source.Id, ct))
            .Where(a => a.Kind == SourceAttachmentKind.PageImage && a.Status == SourceAttachmentStatus.Stored)
            .ToList();

        if (pages.Count == 0)
        {
            return HandwritingTranscriptionResult.NoPages;
        }

        // Transcription is an AI spend of its own; gate it like extraction.
        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            _logger.LogWarning(
                "Handwriting transcription blocked by AI budget. SourceId={SourceId}, WorldId={WorldId}",
                source.Id, worldId);
            return HandwritingTranscriptionResult.Failed(
                ExtractionOutcome.NonTransient(ErrorCategories.BudgetExceeded, budgetError.Message));
        }

        var images = new List<TranscriptionPage>(pages.Count);
        foreach (var page in pages)
        {
            try
            {
                await using var stream = await _blobStorage.OpenReadAsync(page.BlobPath, ct);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                images.Add(new TranscriptionPage(buffer.ToArray(), page.ContentType));
            }
            catch (FileNotFoundException)
            {
                _logger.LogError(
                    "Page image blob missing for handwritten source. SourceId={SourceId}, BlobPath={BlobPath}",
                    source.Id, page.BlobPath);
                return HandwritingTranscriptionResult.Failed(
                    ExtractionOutcome.NonTransient(ErrorCategories.ValidationFailure,
                        $"Page image '{page.FileName}' is missing from storage."));
            }
        }

        HandwritingTranscriptionResponse response;
        try
        {
            response = await _transcriptionClient.TranscribeAsync(new HandwritingTranscriptionRequest
            {
                Pages = images,
                Model = _settings.Model,
                TimeoutSeconds = _settings.TimeoutSeconds
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            await TrackUsageAsync(source, worldId, null, false, ErrorCategories.Timeout, ct);
            return HandwritingTranscriptionResult.Failed(
                ExtractionOutcome.Transient(ErrorCategories.Timeout, ex.Message));
        }
        catch (Exception ex) when (TransientFailureClassifier.IsPermanentHttpFailure(ex))
        {
            _logger.LogError(ex, "Permanent transcription failure. SourceId={SourceId}", source.Id);
            await TrackUsageAsync(source, worldId, null, false, ErrorCategories.AiCallFailure, ct);
            return HandwritingTranscriptionResult.Failed(
                ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message));
        }
        catch (Exception ex) when (ex is AiHttpException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Transient transcription failure. SourceId={SourceId}", source.Id);
            await TrackUsageAsync(source, worldId, null, false, ErrorCategories.TransientError, ct);
            return HandwritingTranscriptionResult.Failed(
                ExtractionOutcome.Transient(ErrorCategories.TransientError, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected transcription failure. SourceId={SourceId}", source.Id);
            await TrackUsageAsync(source, worldId, null, false, ErrorCategories.AiCallFailure, ct);
            return HandwritingTranscriptionResult.Failed(
                ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message));
        }

        await TrackUsageAsync(source, worldId, response, true, null, ct);

        if (string.IsNullOrWhiteSpace(response.Markdown))
        {
            // Blank pages: nothing to extract — let the empty-body path close it out.
            _logger.LogInformation(
                "Transcription produced no text. SourceId={SourceId}, Pages={Pages}", source.Id, pages.Count);
            return HandwritingTranscriptionResult.Transcribed(string.Empty);
        }

        // Persist before continuing: extraction may still fail and retry, and the
        // transcription must not be re-bought on redelivery.
        await _sourceRepository.UpdateBodyAsync(source.Id, response.Markdown, ct);
        source.Body = response.Markdown;

        _logger.LogInformation(
            "Handwriting transcribed. SourceId={SourceId}, Pages={Pages}, Chars={Chars}",
            source.Id, pages.Count, response.Markdown.Length);

        return HandwritingTranscriptionResult.Transcribed(response.Markdown);
    }

    private Task TrackUsageAsync(
        Source source, Guid worldId, HandwritingTranscriptionResponse? response,
        bool succeeded, string? errorCode, CancellationToken ct) =>
        _usageRecorder.RecordAsync(
            worldId, null, AiOperationType.HandwritingTranscription, response?.Usage,
            succeeded, errorCode, sourceId: source.Id, fallbackModel: _settings.Model, ct: ct);
}
