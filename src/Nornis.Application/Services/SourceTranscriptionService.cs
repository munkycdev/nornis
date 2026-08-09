using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Transcribes a handwritten source's page images on demand, so the GM can read what the
/// vision model made of their handwriting and fix it before extraction turns it into
/// proposals. Every other AI path in the system reads its input unattended; this one exists
/// because handwriting is the input most likely to be misread, and a misreading that
/// reaches extraction becomes canon nobody typed.
///
/// The transcription itself belongs to <see cref="HandwritingTranscriptionPipeline"/> — the extraction
/// pipeline runs the same method for sources sent straight through without a look. This
/// class adds what a synchronous, user-facing caller needs and the queue does not:
/// authorization, and a verdict translated out of the pipeline's vocabulary into HTTP.
/// </summary>
public class SourceTranscriptionService : ISourceTranscriptionService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly HandwritingTranscriptionPipeline _transcription;
    private readonly ILogger<SourceTranscriptionService> _logger;

    public SourceTranscriptionService(
        ISourceRepository sourceRepository,
        HandwritingTranscriptionPipeline transcription,
        ILogger<SourceTranscriptionService> logger)
    {
        _sourceRepository = sourceRepository;
        _transcription = transcription;
        _logger = logger;
    }

    public async Task<AppResult<SourceTranscriptionResult>> TranscribeAsync(
        TranscribeSourceCommand command, CancellationToken ct)
    {
        var source = await _sourceRepository.GetByIdAsync(command.SourceId, ct);

        if (SourceWriteGate.Check(source, command.WorldId, command.ActingUserId, command.ActingUserRole)
            is { } gateError)
        {
            return AppResult<SourceTranscriptionResult>.Fail(gateError);
        }

        if (source!.Type != SourceType.HandwrittenNotes)
        {
            return AppResult<SourceTranscriptionResult>.Fail(new AppError(400, "invalid_source_type",
                "Only handwritten notes are transcribed. Other file types are read as part of processing."));
        }

        var result = await _transcription.TranscribeAsync(source, command.WorldId, ct);

        if (result.Failure is not null)
        {
            _logger.LogWarning(
                "On-demand transcription failed. SourceId={SourceId}, WorldId={WorldId}, Category={Category}",
                command.SourceId, command.WorldId, result.Failure.ErrorCategory);

            return AppResult<SourceTranscriptionResult>.Fail(ToApiError(result.Failure));
        }

        if (result.Markdown is null)
        {
            return AppResult<SourceTranscriptionResult>.Fail(new AppError(400, "no_page_images",
                "Add at least one photo or image of the notes before transcribing."));
        }

        return AppResult<SourceTranscriptionResult>.Success(new SourceTranscriptionResult(result.Markdown));
    }

    /// <summary>
    /// The one place the extraction pipeline's failure vocabulary becomes an HTTP status.
    /// The queue reads <see cref="ExtractionOutcome"/> to decide whether to redeliver; a
    /// caller holding a phone needs to know instead whether pressing the button again is
    /// worth trying, which is the same distinction under a different name — so the transient
    /// categories map to 503 and everything else to a status the user cannot retry past.
    ///
    /// Budget keeps the guard's own 429 and code: one condition, one code, whether it
    /// surfaces here or out of Ask.
    /// </summary>
    private static AppError ToApiError(ExtractionOutcome failure) => failure.ErrorCategory switch
    {
        ErrorCategories.BudgetExceeded => new AppError(429, "ai_budget_exceeded", failure.ErrorMessage!),
        ErrorCategories.Timeout or ErrorCategories.TransientError => new AppError(503, "ai_unavailable",
            "Nornis could not reach the reading service. Try again in a moment."),
        ErrorCategories.ValidationFailure => new AppError(400, "validation_error", failure.ErrorMessage!),
        _ => new AppError(502, "ai_failed", "Nornis could not read these pages."),
    };
}
