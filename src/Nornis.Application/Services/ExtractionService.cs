using System.Text.Json;
using System.Text.Json.Nodes;
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

public class ExtractionService : IExtractionService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly ICampaignRepository _campaignRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IAiUsageRecordRepository _aiUsageRecordRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly ISourceAttachmentRepository _sourceAttachmentRepository;
    private readonly IMapPlacemarkRepository _mapPlacemarkRepository;
    private readonly IBlobStorageService _blobStorage;
    private readonly IPdfTextExtractor _pdfTextExtractor;
    private readonly IAiExtractionClient _aiExtractionClient;
    private readonly IHandwritingTranscriptionClient _transcriptionClient;
    private readonly IImageReadingClient _imageReadingClient;
    private readonly IMapExtractionClient _mapExtractionClient;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ExtractionOptions _options;
    private readonly ILogger<ExtractionService> _logger;

    private static readonly string[] ValidChangeTypes =
    [
        "CreateArtifact", "UpdateArtifact", "MergeArtifact",
        "AddFact", "UpdateFact", "AddRelationship", "UpdateRelationship",
        "AddPlacemark"
    ];

    private static readonly string[] ValidTargetTypes =
    [
        "Artifact", "ArtifactFact", "ArtifactRelationship"
    ];

    public ExtractionService(
        ISourceRepository sourceRepository,
        ICampaignRepository campaignRepository,
        IReviewBatchRepository reviewBatchRepository,
        IReviewProposalRepository reviewProposalRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IAiUsageRecordRepository aiUsageRecordRepository,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        ISourceAttachmentRepository sourceAttachmentRepository,
        IMapPlacemarkRepository mapPlacemarkRepository,
        IBlobStorageService blobStorage,
        IPdfTextExtractor pdfTextExtractor,
        IAiExtractionClient aiExtractionClient,
        IHandwritingTranscriptionClient transcriptionClient,
        IImageReadingClient imageReadingClient,
        IMapExtractionClient mapExtractionClient,
        IAiBudgetGuard budgetGuard,
        IUnitOfWork unitOfWork,
        IOptions<ExtractionOptions> options,
        ILogger<ExtractionService> logger)
    {
        _sourceAttachmentRepository = sourceAttachmentRepository;
        _mapPlacemarkRepository = mapPlacemarkRepository;
        _blobStorage = blobStorage;
        _pdfTextExtractor = pdfTextExtractor;
        _transcriptionClient = transcriptionClient;
        _imageReadingClient = imageReadingClient;
        _mapExtractionClient = mapExtractionClient;
        _budgetGuard = budgetGuard;
        _sourceRepository = sourceRepository;
        _campaignRepository = campaignRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _reviewProposalRepository = reviewProposalRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _aiUsageRecordRepository = aiUsageRecordRepository;
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _aiExtractionClient = aiExtractionClient;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExtractionOutcome> ProcessExtractionAsync(
        Guid sourceId,
        Guid worldId,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Starting extraction for SourceId={SourceId}, WorldId={WorldId}",
            sourceId, worldId);

        // 1. Retrieve source
        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);

        if (source is null)
        {
            _logger.LogWarning(
                "Source not found. SourceId={SourceId}, WorldId={WorldId}",
                sourceId, worldId);
            return ExtractionOutcome.NonTransient(ErrorCategories.SourceNotFound, "Source not found.");
        }

        // 2. Extraction opt-out: a queued message for a source stored without extraction
        //    (flag toggled after enqueue) must not extract. File it instead of leaving it
        //    claimed by the pipeline.
        if (!source.ExtractionEnabled)
        {
            if (source.ProcessingStatus is SourceProcessingStatus.Queued or SourceProcessingStatus.Processing)
            {
                await _sourceRepository.UpdateProcessingStatusAsync(sourceId, SourceProcessingStatus.Processed, ct);
            }

            _logger.LogInformation(
                "Source is stored without extraction; skipping. SourceId={SourceId}", sourceId);
            return ExtractionOutcome.SkippedIdempotent("Source is stored without extraction.");
        }

        // 3. Idempotency: check the ReviewBatch first — its presence proves extraction
        //    completed even when a crash landed before the final status write.
        var existingBatch = await _reviewBatchRepository.GetBySourceIdAsync(sourceId, ct);

        if (existingBatch is not null)
        {
            // Repair a run that committed its batch but crashed before transitioning
            // the source out of Processing; otherwise the source wedges forever.
            if (source.ProcessingStatus == SourceProcessingStatus.Processing)
            {
                await _sourceRepository.UpdateProcessingStatusAsync(sourceId, SourceProcessingStatus.Processed, ct);
                _logger.LogWarning(
                    "Repaired source stuck in Processing with a completed batch. SourceId={SourceId}, BatchId={BatchId}",
                    sourceId, existingBatch.Id);
                return ExtractionOutcome.SkippedIdempotent(
                    $"ReviewBatch already exists in {existingBatch.Status} status; source repaired to Processed.");
            }

            _logger.LogInformation(
                "ReviewBatch already exists for source. SourceId={SourceId}, BatchId={BatchId}, BatchStatus={BatchStatus}",
                sourceId, existingBatch.Id, existingBatch.Status);
            return ExtractionOutcome.SkippedIdempotent(
                $"ReviewBatch already exists in {existingBatch.Status} status.");
        }

        // 3. Idempotency: only Queued sources start extraction — except Processing with
        //    no batch, which is a run that crashed mid-extraction (the message was
        //    redelivered after a worker restart) and must be resumed, not skipped.
        if (source.ProcessingStatus == SourceProcessingStatus.Processing)
        {
            _logger.LogWarning(
                "Resuming extraction for source stuck in Processing with no batch (crashed run). SourceId={SourceId}",
                sourceId);
        }
        else if (source.ProcessingStatus != SourceProcessingStatus.Queued)
        {
            _logger.LogInformation(
                "Source already processed or not in Queued status. SourceId={SourceId}, Status={Status}",
                sourceId, source.ProcessingStatus);
            return ExtractionOutcome.SkippedIdempotent(
                $"Source is in {source.ProcessingStatus} status, not Queued.");
        }

        // 4. Transition: Queued → Processing
        await _sourceRepository.UpdateProcessingStatusAsync(sourceId, SourceProcessingStatus.Processing, ct);

        // 4b. Handwritten notes arrive as page images; vision transcription produces the
        // body here, then the normal pipeline continues. The transcription is persisted,
        // so a redelivered message sees a non-empty body and skips this step.
        if (source.Type == SourceType.HandwrittenNotes && string.IsNullOrWhiteSpace(source.Body))
        {
            var transcriptionOutcome = await TranscribeHandwrittenAsync(source, worldId, ct);
            if (transcriptionOutcome is not null)
            {
                return transcriptionOutcome;
            }
        }

        // 4c. Map sources take their own extraction path: place names + positions from
        // the map image become artifact/placemark proposals. Typed notes ride along as
        // naming context; they are not separately extracted.
        if (source.Type == SourceType.Map)
        {
            return await ProcessMapExtractionAsync(source, worldId, ct);
        }

        // 4d. Image/Upload sources derive text from their files (PDF text, file
        // contents, vision reads) exactly once. The derived text is persisted before
        // extraction so a redelivered message never re-buys the vision call.
        if (source.Type is SourceType.Image or SourceType.Upload && source.DerivedText is null)
        {
            var derivationOutcome = await DeriveAttachmentTextAsync(source, worldId, ct);
            if (derivationOutcome is not null)
            {
                return derivationOutcome;
            }
        }

        // Imported notes carry frontmatter and wikilink markup from the previous
        // system; normalize before the empty-body check so a frontmatter-only note
        // short-circuits. The entity is detached — the stored body stays raw.
        if (source.Type == SourceType.ImportedNote && source.Body is not null)
        {
            source.Body = ImportedNoteNormalizer.Normalize(source.Body);
        }

        // Compose typed notes + derived text in memory only: Body stays the user's.
        if (!string.IsNullOrWhiteSpace(source.DerivedText))
        {
            source.Body = ComposeEffectiveBody(source.Body, source.DerivedText);
        }

        // 5. Empty body short-circuit
        if (string.IsNullOrWhiteSpace(source.Body))
        {
            _logger.LogInformation(
                "Source body is empty, creating completed batch with zero proposals. SourceId={SourceId}",
                sourceId);

            return await HandleEmptyBodyAsync(source, worldId, ct);
        }

        // 6. Daily AI budget gate. The message is completed (not redelivered) and the
        // source fails visibly — the GM can retry from the UI once the budget resets.
        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            _logger.LogWarning(
                "Extraction blocked by AI budget. SourceId={SourceId}, WorldId={WorldId}",
                sourceId, worldId);
            await _sourceRepository.UpdateProcessingStatusAsync(sourceId, SourceProcessingStatus.Failed, ct);
            return ExtractionOutcome.NonTransient("BudgetExceeded", budgetError.Message);
        }

        // 7. Context assembly
        var context = await AssembleContextAsync(source, worldId, ct);

        // 8. AI invocation with parse retry
        return await InvokeAiWithRetriesAsync(source, worldId, context, ct);
    }

    /// <summary>
    /// Vision-transcribes a handwritten source's page images into its Body. Returns null
    /// to continue the normal pipeline (transcription succeeded, or there were no pages
    /// and the empty-body path should handle it), or a terminal outcome on failure.
    /// </summary>
    private async Task<ExtractionOutcome?> TranscribeHandwrittenAsync(Source source, Guid worldId, CancellationToken ct)
    {
        var pages = (await _sourceAttachmentRepository.ListBySourceAsync(source.Id, ct))
            .Where(a => a.Kind == SourceAttachmentKind.PageImage && a.Status == SourceAttachmentStatus.Stored)
            .ToList();

        if (pages.Count == 0)
        {
            return null; // nothing to transcribe — the empty-body short-circuit takes it
        }

        // Transcription is an AI spend of its own; gate it like extraction.
        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            _logger.LogWarning(
                "Handwriting transcription blocked by AI budget. SourceId={SourceId}, WorldId={WorldId}",
                source.Id, worldId);
            await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
            return ExtractionOutcome.NonTransient("BudgetExceeded", budgetError.Message);
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
                await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.ValidationFailure,
                    $"Page image '{page.FileName}' is missing from storage.");
            }
        }

        HandwritingTranscriptionResponse response;
        try
        {
            response = await _transcriptionClient.TranscribeAsync(new HandwritingTranscriptionRequest
            {
                Pages = images,
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
            await TrackTranscriptionUsageAsync(source, worldId, null, false, ErrorCategories.Timeout, ct);
            return await TransientOutcomeAsync(source, ErrorCategories.Timeout, ex.Message, ct);
        }
        catch (HttpRequestException ex) when (IsPermanentHttpFailure(ex))
        {
            _logger.LogError(ex, "Permanent transcription failure. SourceId={SourceId}", source.Id);
            await TrackTranscriptionUsageAsync(source, worldId, null, false, ErrorCategories.AiCallFailure, ct);
            await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
            return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Transient transcription failure. SourceId={SourceId}", source.Id);
            await TrackTranscriptionUsageAsync(source, worldId, null, false, ErrorCategories.TransientError, ct);
            return await TransientOutcomeAsync(source, ErrorCategories.TransientError, ex.Message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected transcription failure. SourceId={SourceId}", source.Id);
            await TrackTranscriptionUsageAsync(source, worldId, null, false, ErrorCategories.AiCallFailure, ct);
            await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
            return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
        }

        await TrackTranscriptionUsageAsync(source, worldId, response, true, null, ct);

        if (string.IsNullOrWhiteSpace(response.Markdown))
        {
            // Blank pages: nothing to extract — let the empty-body path close it out.
            _logger.LogInformation(
                "Transcription produced no text. SourceId={SourceId}, Pages={Pages}", source.Id, pages.Count);
            return null;
        }

        // Persist before continuing: extraction may still fail and retry, and the
        // transcription must not be re-bought on redelivery.
        await _sourceRepository.UpdateBodyAsync(source.Id, response.Markdown, ct);
        source.Body = response.Markdown;

        _logger.LogInformation(
            "Handwriting transcribed. SourceId={SourceId}, Pages={Pages}, Chars={Chars}",
            source.Id, pages.Count, response.Markdown.Length);

        return null;
    }

    private async Task TrackTranscriptionUsageAsync(
        Source source, Guid worldId, HandwritingTranscriptionResponse? response,
        bool succeeded, string? errorCode, CancellationToken ct)
    {
        var costUsd = response is null || !_options.ModelPricing.TryGetValue(response.Model, out var pricing)
            ? 0m
            : response.InputTokens * pricing.InputPerMillionTokensUsd / 1_000_000m
              + response.OutputTokens * pricing.OutputPerMillionTokensUsd / 1_000_000m;

        await _aiUsageRecordRepository.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            OperationType = AiOperationType.HandwritingTranscription,
            Model = response?.Model ?? _options.AiModel,
            InputTokens = response?.InputTokens ?? 0,
            OutputTokens = response?.OutputTokens ?? 0,
            TotalTokens = response?.TotalTokens ?? 0,
            EstimatedCostUsd = costUsd,
            DurationMs = response?.DurationMs ?? 0,
            Succeeded = succeeded,
            ErrorCode = errorCode,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);
    }

    /// <summary>
    /// Map extraction: reads place names + normalized positions off the map image and
    /// turns them into review proposals — CreateArtifact (with an embedded placemark
    /// block) for new places, AddPlacemark for places matching existing Locations.
    /// </summary>
    private async Task<ExtractionOutcome> ProcessMapExtractionAsync(Source source, Guid worldId, CancellationToken ct)
    {
        var mapAttachment = (await _sourceAttachmentRepository.ListBySourceAsync(source.Id, ct))
            .FirstOrDefault(a => a.Kind == SourceAttachmentKind.MapImage && a.Status == SourceAttachmentStatus.Stored);

        if (mapAttachment is null)
        {
            // A map source without a map image has nothing to extract — file it with an
            // empty completed batch, mirroring blank handwriting pages.
            _logger.LogInformation("Map source has no stored map image. SourceId={SourceId}", source.Id);
            return await HandleEmptyBodyAsync(source, worldId, ct);
        }

        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            _logger.LogWarning(
                "Map extraction blocked by AI budget. SourceId={SourceId}, WorldId={WorldId}", source.Id, worldId);
            await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
            return ExtractionOutcome.NonTransient("BudgetExceeded", budgetError.Message);
        }

        byte[] imageBytes;
        try
        {
            await using var stream = await _blobStorage.OpenReadAsync(mapAttachment.BlobPath, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            imageBytes = buffer.ToArray();
        }
        catch (FileNotFoundException)
        {
            _logger.LogError(
                "Map image blob missing. SourceId={SourceId}, BlobPath={BlobPath}", source.Id, mapAttachment.BlobPath);
            await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
            return ExtractionOutcome.NonTransient(ErrorCategories.ValidationFailure,
                "The map image is missing from storage.");
        }

        // Existing Locations the source's readers may see — the model matches against
        // these instead of proposing duplicates.
        var existingLocations = await _artifactRepository.ListByTypeAsync(
            worldId, ArtifactType.Location,
            VisibilityFilter.ForSourceContext(source.Visibility, source.CreatedByUserId), ct);

        var request = new MapExtractionRequest
        {
            ImageBytes = imageBytes,
            MediaType = mapAttachment.ContentType,
            SourceTitle = source.Title,
            SourceBody = source.Body,
            ExistingLocations = existingLocations.Select(a => new MapLocationContext(a.Id, a.Name)).ToList(),
            Model = _options.AiModel,
            TimeoutSeconds = _options.AiTimeoutSeconds
        };

        var maxAttempts = 1 + _options.MaxParseRetryAttempts;
        string? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await _mapExtractionClient.ExtractAsync(request, ct);

                var proposals = await BuildMapProposalsAsync(source, mapAttachment, existingLocations, response, ct);

                var synthesized = new AiExtractionResponse
                {
                    Proposals = proposals,
                    InputTokens = response.InputTokens,
                    OutputTokens = response.OutputTokens,
                    TotalTokens = response.TotalTokens,
                    DurationMs = response.DurationMs,
                    Model = response.Model
                };

                return await HandleSuccessfulResponseAsync(source, worldId, synthesized, ct, AiOperationType.MapExtraction);
            }
            catch (AiExtractionParseException ex)
            {
                lastError = ex.Message;
                _logger.LogWarning(ex,
                    "Map extraction parse failed on attempt {Attempt}/{MaxAttempts}. SourceId={SourceId}",
                    attempt, maxAttempts, source.Id);
            }
            catch (AiExtractionTimeoutException ex)
            {
                await TrackVisionUsageAsync(source, worldId, AiOperationType.MapExtraction, null, false, ErrorCategories.Timeout, ct);
                return await TransientOutcomeAsync(source, ErrorCategories.Timeout, ex.Message, ct);
            }
            catch (TimeoutException ex)
            {
                await TrackVisionUsageAsync(source, worldId, AiOperationType.MapExtraction, null, false, ErrorCategories.Timeout, ct);
                return await TransientOutcomeAsync(source, ErrorCategories.Timeout, ex.Message, ct);
            }
            catch (HttpRequestException ex) when (IsPermanentHttpFailure(ex))
            {
                _logger.LogError(ex, "Permanent map extraction failure. SourceId={SourceId}", source.Id);
                await TrackVisionUsageAsync(source, worldId, AiOperationType.MapExtraction, null, false, ErrorCategories.AiCallFailure, ct);
                await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Transient map extraction failure. SourceId={SourceId}", source.Id);
                await TrackVisionUsageAsync(source, worldId, AiOperationType.MapExtraction, null, false, ErrorCategories.TransientError, ct);
                return await TransientOutcomeAsync(source, ErrorCategories.TransientError, ex.Message, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Non-transient map extraction failure. SourceId={SourceId}", source.Id);
                await TrackVisionUsageAsync(source, worldId, AiOperationType.MapExtraction, null, false, ErrorCategories.AiCallFailure, ct);
                await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
            }
        }

        _logger.LogError("Map extraction parse retries exhausted. SourceId={SourceId}, Error={Error}", source.Id, lastError);
        await TrackVisionUsageAsync(source, worldId, AiOperationType.MapExtraction, null, false, ErrorCategories.ParseFailure, ct);
        await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
        return ExtractionOutcome.NonTransient(ErrorCategories.ParseFailure,
            $"Map extraction failed after {_options.MaxParseRetryAttempts} retries: {lastError}");
    }

    /// <summary>Turns extracted places into review proposals: hallucination-filtered,
    /// range-clamped, deduped, capped, and matched against existing Locations.</summary>
    private async Task<IReadOnlyList<ExtractionProposal>> BuildMapProposalsAsync(
        Source source, SourceAttachment mapAttachment, IReadOnlyList<Artifact> existingLocations,
        MapExtractionResponse response, CancellationToken ct)
    {
        const int maxPlaces = 100;

        var byId = existingLocations.ToDictionary(a => a.Id);
        var byName = existingLocations
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var proposals = new List<ExtractionProposal>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var place in response.Places)
        {
            if (proposals.Count >= maxPlaces)
            {
                _logger.LogWarning(
                    "Map extraction returned more than {Max} places; extras dropped. SourceId={SourceId}",
                    maxPlaces, source.Id);
                break;
            }

            var name = place.Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > 200 || !seenNames.Add(name))
            {
                continue;
            }

            // A hallucinated position is worse than a missing pin.
            if (place.X is < 0m or > 1m || place.Y is < 0m or > 1m)
            {
                continue;
            }

            var confidence = place.Confidence is >= 0m and <= 1m ? place.Confidence : null;

            // Match: model-supplied id (must exist in the offered context — anything else
            // is a hallucination), else unique exact name.
            Artifact? matched = null;
            var ambiguous = false;
            if (place.ExistingArtifactId is { } id && byId.TryGetValue(id, out var byIdMatch))
            {
                matched = byIdMatch;
            }
            else if (byName.TryGetValue(name, out var candidates))
            {
                if (candidates.Count == 1)
                {
                    matched = candidates[0];
                }
                else
                {
                    ambiguous = true;
                }
            }

            if (matched is not null)
            {
                // Already pinned on this map: nothing to propose (re-extraction hygiene).
                if (await _mapPlacemarkRepository.GetByAttachmentAndArtifactAsync(mapAttachment.Id, matched.Id, ct) is not null)
                {
                    continue;
                }

                proposals.Add(new ExtractionProposal
                {
                    ChangeType = "AddPlacemark",
                    TargetType = "Artifact",
                    TargetId = matched.Id,
                    ProposedValue = new Dictionary<string, object?>
                    {
                        ["artifactId"] = matched.Id,
                        ["attachmentId"] = mapAttachment.Id,
                        ["x"] = place.X,
                        ["y"] = place.Y,
                        ["label"] = name,
                        ["confidence"] = confidence
                    },
                    Rationale = $"'{name}' on the map matches the existing location '{matched.Name}'.",
                    Confidence = confidence,
                    Quote = name
                });
            }
            else if (ambiguous)
            {
                // Several artifacts share the name — the applicator surfaces the
                // ambiguity to the reviewer, same as name-referenced facts.
                proposals.Add(new ExtractionProposal
                {
                    ChangeType = "AddPlacemark",
                    TargetType = "Artifact",
                    TargetId = null,
                    ProposedValue = new Dictionary<string, object?>
                    {
                        ["artifactName"] = name,
                        ["attachmentId"] = mapAttachment.Id,
                        ["x"] = place.X,
                        ["y"] = place.Y,
                        ["label"] = name,
                        ["confidence"] = confidence
                    },
                    Rationale = $"'{name}' on the map matches more than one existing location by name.",
                    Confidence = confidence,
                    Quote = name
                });
            }
            else
            {
                proposals.Add(new ExtractionProposal
                {
                    ChangeType = "CreateArtifact",
                    TargetType = "Artifact",
                    ProposedValue = new Dictionary<string, object?>
                    {
                        ["name"] = name,
                        ["type"] = "Location",
                        ["summary"] = KindToSummary(place.Kind),
                        ["mapPlacemark"] = new Dictionary<string, object?>
                        {
                            ["attachmentId"] = mapAttachment.Id,
                            ["x"] = place.X,
                            ["y"] = place.Y,
                            ["label"] = name
                        }
                    },
                    Rationale = $"Labeled on the map \"{source.Title}\".",
                    Confidence = confidence,
                    Quote = name
                });
            }
        }

        return proposals;
    }

    private static string? KindToSummary(string? kind) => kind switch
    {
        null or "" or "other" => null,
        "body_of_water" => "A body of water marked on the map.",
        _ => $"A {kind.Replace('_', ' ')} marked on the map."
    };

    /// <summary>Matches SourceService.ValidateBody — the composed prompt body honors the
    /// same ceiling the typed body does.</summary>
    private const int MaxComposedBodyChars = 100_000;

    private static string ComposeEffectiveBody(string? body, string derivedText)
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
    private async Task<ExtractionOutcome?> DeriveAttachmentTextAsync(Source source, Guid worldId, CancellationToken ct)
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
                        await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
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
                await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
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
                await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
                return ExtractionOutcome.NonTransient("BudgetExceeded", budgetError.Message);
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
                await TrackVisionUsageAsync(source, worldId, AiOperationType.ImageReading, null, false, ErrorCategories.Timeout, ct);
                return await TransientOutcomeAsync(source, ErrorCategories.Timeout, ex.Message, ct);
            }
            catch (HttpRequestException ex) when (IsPermanentHttpFailure(ex))
            {
                _logger.LogError(ex, "Permanent image reading failure. SourceId={SourceId}", source.Id);
                await TrackVisionUsageAsync(source, worldId, AiOperationType.ImageReading, null, false, ErrorCategories.AiCallFailure, ct);
                await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Transient image reading failure. SourceId={SourceId}", source.Id);
                await TrackVisionUsageAsync(source, worldId, AiOperationType.ImageReading, null, false, ErrorCategories.TransientError, ct);
                return await TransientOutcomeAsync(source, ErrorCategories.TransientError, ex.Message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected image reading failure. SourceId={SourceId}", source.Id);
                await TrackVisionUsageAsync(source, worldId, AiOperationType.ImageReading, null, false, ErrorCategories.AiCallFailure, ct);
                await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
            }

            await TrackVisionUsageAsync(source, worldId, AiOperationType.ImageReading,
                (response.Model, response.InputTokens, response.OutputTokens, response.TotalTokens, response.DurationMs),
                true, null, ct);

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

    private async Task TrackVisionUsageAsync(
        Source source, Guid worldId, AiOperationType operationType,
        (string Model, int InputTokens, int OutputTokens, int TotalTokens, int DurationMs)? usage,
        bool succeeded, string? errorCode, CancellationToken ct)
    {
        var costUsd = usage is null || !_options.ModelPricing.TryGetValue(usage.Value.Model, out var pricing)
            ? 0m
            : usage.Value.InputTokens * pricing.InputPerMillionTokensUsd / 1_000_000m
              + usage.Value.OutputTokens * pricing.OutputPerMillionTokensUsd / 1_000_000m;

        await _aiUsageRecordRepository.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            OperationType = operationType,
            Model = usage?.Model ?? _options.AiModel,
            InputTokens = usage?.InputTokens ?? 0,
            OutputTokens = usage?.OutputTokens ?? 0,
            TotalTokens = usage?.TotalTokens ?? 0,
            EstimatedCostUsd = costUsd,
            DurationMs = usage?.DurationMs ?? 0,
            Succeeded = succeeded,
            ErrorCode = errorCode,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);
    }

    private async Task<ExtractionOutcome> HandleEmptyBodyAsync(
        Source source, Guid worldId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Completed,
            CreatedAt = now,
            CompletedAt = now
        };

        await _reviewBatchRepository.CreateAsync(batch, ct);
        await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Processed, ct);

        return ExtractionOutcome.Succeeded(batch.Id, 0);
    }

    private async Task<IReadOnlyList<ArtifactContext>> AssembleContextAsync(
        Source source, Guid worldId, CancellationToken ct)
    {
        var filter = VisibilityFilter.ForSourceContext(source.Visibility, source.CreatedByUserId);

        // Load name-matched artifacts
        var nameMatched = await _artifactRepository.ListByNamesInTextAsync(
            worldId, source.Body!, filter, ct);

        // Load recent artifacts
        var recent = await _artifactRepository.ListRecentByWorldAsync(
            worldId, filter, _options.MaxArtifactContextCount, ct);

        // Merge: name-matched first, then recent, deduplicate by Id
        var seen = new HashSet<Guid>();
        var merged = new List<Artifact>();

        foreach (var artifact in nameMatched)
        {
            if (seen.Add(artifact.Id))
            {
                merged.Add(artifact);
            }
        }

        foreach (var artifact in recent)
        {
            if (seen.Add(artifact.Id))
            {
                merged.Add(artifact);
            }
        }

        // Cap at MaxArtifactContextCount
        if (merged.Count > _options.MaxArtifactContextCount)
        {
            merged = merged.Take(_options.MaxArtifactContextCount).ToList();
        }

        if (merged.Count == 0)
        {
            return [];
        }

        // Load facts for each artifact, scoped to what this source's readers may see —
        // a PartyVisible extraction must never quote GM-only material back into
        // party-visible proposals. Hidden truth states are GM knowledge regardless of
        // the fact's visibility scope, so only GM-authored (GMOnly) sources see them.
        var artifactIds = merged.Select(a => a.Id).ToList();
        var includeHiddenTruths = source.Visibility == VisibilityScope.GMOnly;
        var facts = (await _artifactFactRepository.ListByArtifactIdsAsync(
                artifactIds, filter, _options.MaxFactsPerArtifact, ct))
            .Where(f => includeHiddenTruths || f.TruthState != TruthState.Hidden)
            .ToList();

        var factsByArtifact = facts.GroupBy(f => f.ArtifactId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // The GM's storyline hierarchy ("PartOf" links) grounds the model's own PartOf
        // proposals: a storyline that already has a parent must not get another.
        var storylineIds = merged
            .Where(a => a.Type == ArtifactType.Storyline)
            .Select(a => a.Id)
            .ToList();
        var parentNameByChild = new Dictionary<Guid, string>();
        if (storylineIds.Count > 0)
        {
            var partOfLinks = (await _artifactRelationshipRepository.ListByArtifactIdsAsync(storylineIds, filter, ct))
                .Where(r => r.Type == ArtifactService.PartOfRelationshipType && storylineIds.Contains(r.ArtifactAId))
                .DistinctBy(r => r.ArtifactAId)
                .ToList();

            var namesById = merged.ToDictionary(a => a.Id, a => a.Name);
            foreach (var link in partOfLinks)
            {
                if (!namesById.TryGetValue(link.ArtifactBId, out var parentName))
                {
                    parentName = (await _artifactRepository.GetByIdAsync(link.ArtifactBId, ct))?.Name ?? "another storyline";
                }
                parentNameByChild[link.ArtifactAId] = parentName;
            }
        }

        // Build context models
        return merged.Select(a => new ArtifactContext
        {
            Id = a.Id,
            Name = a.Name,
            Type = a.Type.ToString(),
            Summary = a.Summary,
            Facts = factsByArtifact.TryGetValue(a.Id, out var artifactFacts)
                ? artifactFacts.Select(f => new FactContext
                {
                    Id = f.Id,
                    Predicate = f.Predicate,
                    Value = f.Value
                }).ToList()
                : [],
            PartOfName = parentNameByChild.GetValueOrDefault(a.Id)
        }).ToList();
    }

    private async Task<ExtractionOutcome> InvokeAiWithRetriesAsync(
        Source source, Guid worldId, IReadOnlyList<ArtifactContext> context, CancellationToken ct)
    {
        // Campaign context helps the AI disambiguate recurring names across campaign eras.
        Campaign? campaign = null;
        if (source.CampaignId is not null)
        {
            campaign = await _campaignRepository.GetByIdAsync(source.CampaignId.Value, ct);
        }

        var request = BuildExtractionRequest(source, campaign, context);
        var maxAttempts = 1 + _options.MaxParseRetryAttempts; // initial + retries

        AiExtractionResponse? lastResponse = null;
        string? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await _aiExtractionClient.ExtractAsync(request, ct);
                lastResponse = response;

                // Validate response
                var validationError = ValidateResponse(response);
                if (validationError is null)
                {
                    // Success — create proposals and track usage
                    return await HandleSuccessfulResponseAsync(source, worldId, response, ct);
                }

                lastError = validationError;
                _logger.LogWarning(
                    "AI response validation failed on attempt {Attempt}/{MaxAttempts}. SourceId={SourceId}, Error={Error}",
                    attempt, maxAttempts, source.Id, validationError);
            }
            catch (AiExtractionParseException ex)
            {
                // Malformed AI output (bad JSON, invalid fields) is retryable: sampling
                // variance means the next attempt usually parses. Exhausted retries fall
                // through to the ParseFailure path below.
                lastError = ex.Message;
                _logger.LogWarning(ex,
                    "AI response parse failed on attempt {Attempt}/{MaxAttempts}. SourceId={SourceId}",
                    attempt, maxAttempts, source.Id);
            }
            catch (AiExtractionTimeoutException ex)
            {
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.Timeout, ct);
                return await TransientOutcomeAsync(source, ErrorCategories.Timeout, ex.Message, ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout — transient failure
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.Timeout, ct);
                return await TransientOutcomeAsync(source, ErrorCategories.Timeout, "AI call timed out.", ct);
            }
            catch (HttpRequestException ex) when (IsPermanentHttpFailure(ex))
            {
                // 4xx (other than 408/429): the request itself is bad — a retry sends the same
                // bytes and fails the same way. Fail the source so the problem surfaces.
                _logger.LogError(ex,
                    "Permanent AI request failure. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.AiCallFailure, ct);
                await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
            }
            catch (HttpRequestException ex)
            {
                // Network error / 5xx / throttling — transient failure
                _logger.LogWarning(ex,
                    "Network error during AI call. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.TransientError, ct);
                return await TransientOutcomeAsync(source, ErrorCategories.TransientError, ex.Message, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // propagate cancellation
            }
            catch (Exception ex) when (IsTransientException(ex))
            {
                _logger.LogWarning(ex,
                    "Transient error during AI call. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.TransientError, ct);
                return await TransientOutcomeAsync(source, ErrorCategories.TransientError, ex.Message, ct);
            }
            catch (Exception ex)
            {
                // Non-transient AI call failure
                _logger.LogError(ex,
                    "Non-transient AI call failure. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.AiCallFailure, ct);
                await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
            }
        }

        // All parse retries exhausted — non-transient failure
        _logger.LogError(
            "Parse retries exhausted. SourceId={SourceId}, Error={Error}",
            source.Id, lastError);

        await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.ParseFailure, ct);
        await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
        return ExtractionOutcome.NonTransient(ErrorCategories.ParseFailure,
            $"AI response validation failed after {_options.MaxParseRetryAttempts} retries: {lastError}");
    }

    private async Task<ExtractionOutcome> HandleSuccessfulResponseAsync(
        Source source, Guid worldId, AiExtractionResponse response, CancellationToken ct,
        AiOperationType operationType = AiOperationType.SourceExtraction)
    {
        var now = DateTimeOffset.UtcNow;

        // Handle empty proposals from AI
        if (response.Proposals.Count == 0)
        {
            var emptyBatch = new ReviewBatch
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                SourceId = source.Id,
                Status = ReviewBatchStatus.Completed,
                CreatedAt = now,
                CompletedAt = now
            };

            await _reviewBatchRepository.CreateAsync(emptyBatch, ct);
            await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Processed, ct);
            await TrackUsageAsync(source, worldId, response, true, null, ct, emptyBatch.Id, operationType);

            return ExtractionOutcome.Succeeded(emptyBatch.Id, 0);
        }

        // Atomic creation: ReviewBatch + ReviewProposals + SourceReferences
        Guid batchId;
        try
        {
            batchId = await CreateProposalsAtomicallyAsync(source, worldId, response, now, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist proposals atomically. SourceId={SourceId}", source.Id);
            await TrackUsageAsync(source, worldId, response, false, ErrorCategories.ValidationFailure, ct, operationType: operationType);
            await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
            return ExtractionOutcome.NonTransient(ErrorCategories.ValidationFailure,
                "Failed to persist proposals: " + ex.Message);
        }

        // Transition source to Processed
        await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Processed, ct);

        // Track usage OUTSIDE the proposal transaction (persists even on rollback)
        await TrackUsageAsync(source, worldId, response, true, null, ct, batchId, operationType);

        return ExtractionOutcome.Succeeded(batchId, response.Proposals.Count);
    }

    private async Task<Guid> CreateProposalsAtomicallyAsync(
        Source source, Guid worldId, AiExtractionResponse response,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            var batch = new ReviewBatch
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                SourceId = source.Id,
                Status = ReviewBatchStatus.Pending,
                CreatedAt = now
            };

            await _reviewBatchRepository.CreateAsync(batch, ct);

            foreach (var proposal in response.Proposals)
            {
                var proposedValueJson = EnforceVisibility(proposal.ProposedValue, source.Visibility);
                proposedValueJson = NormalizeIdFields(proposedValueJson, proposal.ChangeType);

                var reviewProposal = new ReviewProposal
                {
                    Id = Guid.NewGuid(),
                    ReviewBatchId = batch.Id,
                    ChangeType = ParseChangeType(proposal.ChangeType),
                    TargetType = ParseTargetType(proposal.TargetType),
                    TargetId = proposal.TargetId,
                    ProposedValueJson = proposedValueJson,
                    Rationale = proposal.Rationale,
                    Confidence = proposal.Confidence,
                    Status = ReviewProposalStatus.Pending,
                    CreatedAt = now
                };

                await _reviewProposalRepository.CreateAsync(reviewProposal, ct);

                var sourceReference = new SourceReference
                {
                    Id = Guid.NewGuid(),
                    SourceId = source.Id,
                    TargetType = SourceReferenceTargetType.ReviewProposal,
                    TargetId = reviewProposal.Id,
                    Quote = proposal.Quote,
                    CreatedAt = now
                };

                await _sourceReferenceRepository.CreateAsync(sourceReference, ct);
            }

            await transaction.CommitAsync(ct);
            return batch.Id;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task TrackUsageAsync(
        Source source, Guid worldId, AiExtractionResponse? response,
        bool succeeded, string? errorCode, CancellationToken ct, Guid? reviewBatchId = null,
        AiOperationType operationType = AiOperationType.SourceExtraction)
    {
        var costUsd = CalculateCost(response);

        var record = new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            OperationType = operationType,
            Model = response?.Model ?? _options.AiModel,
            InputTokens = response?.InputTokens ?? 0,
            OutputTokens = response?.OutputTokens ?? 0,
            TotalTokens = response?.TotalTokens ?? 0,
            EstimatedCostUsd = costUsd,
            DurationMs = response?.DurationMs ?? 0,
            Succeeded = succeeded,
            ErrorCode = errorCode,
            ReviewBatchId = reviewBatchId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _aiUsageRecordRepository.CreateAsync(record, ct);
    }

    private decimal CalculateCost(AiExtractionResponse? response)
    {
        if (response is null)
        {
            return 0m;
        }

        var model = response.Model;

        if (!_options.ModelPricing.TryGetValue(model, out var pricing))
        {
            return 0m;
        }

        var inputCost = response.InputTokens * pricing.InputPerMillionTokensUsd / 1_000_000m;
        var outputCost = response.OutputTokens * pricing.OutputPerMillionTokensUsd / 1_000_000m;

        return inputCost + outputCost;
    }

    private static string? ValidateResponse(AiExtractionResponse response)
    {
        if (response.Proposals.Count > 50)
        {
            return $"Response contains {response.Proposals.Count} proposals, maximum is 50.";
        }

        for (var i = 0; i < response.Proposals.Count; i++)
        {
            var proposal = response.Proposals[i];

            if (!ValidChangeTypes.Contains(proposal.ChangeType))
            {
                return $"Proposal[{i}] has invalid ChangeType '{proposal.ChangeType}'.";
            }

            if (!ValidTargetTypes.Contains(proposal.TargetType))
            {
                return $"Proposal[{i}] has invalid TargetType '{proposal.TargetType}'.";
            }

            if (string.IsNullOrEmpty(proposal.Rationale))
            {
                return $"Proposal[{i}] has empty Rationale.";
            }

            if (proposal.Rationale.Length > 500)
            {
                return $"Proposal[{i}] Rationale exceeds 500 characters ({proposal.Rationale.Length}).";
            }

            if (proposal.Confidence.HasValue)
            {
                if (proposal.Confidence.Value < 0.0m || proposal.Confidence.Value > 1.0m)
                {
                    return $"Proposal[{i}] Confidence {proposal.Confidence.Value} is outside 0.0–1.0 range.";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The model occasionally puts an artifact NAME in a relationship ID field, which
    /// would fail Guid deserialization at accept time. Move any non-UUID string from an
    /// ID field into the matching Name field (when that is empty) so the proposal stays
    /// acceptable.
    /// </summary>
    internal static string NormalizeIdFields(string proposedValueJson, string changeType)
    {
        if (changeType is not "AddRelationship")
        {
            return proposedValueJson;
        }

        try
        {
            if (JsonNode.Parse(proposedValueJson) is not JsonObject obj)
            {
                return proposedValueJson;
            }

            var changed = false;
            foreach (var (idField, nameField) in new[] { ("artifactAId", "artifactAName"), ("artifactBId", "artifactBName") })
            {
                var raw = obj[idField]?.GetValue<string?>();
                if (string.IsNullOrWhiteSpace(raw) || Guid.TryParse(raw, out _))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(obj[nameField]?.GetValue<string?>()))
                {
                    obj[nameField] = raw;
                }

                obj[idField] = null;
                changed = true;
            }

            return changed ? obj.ToJsonString() : proposedValueJson;
        }
        catch (Exception)
        {
            // Malformed payloads are the validator's problem, not ours.
            return proposedValueJson;
        }
    }

    private static string EnforceVisibility(object proposedValue, VisibilityScope sourceVisibility)
    {
        var json = proposedValue is JsonElement element
            ? element.GetRawText()
            : JsonSerializer.Serialize(proposedValue);

        // Parse and override visibility in the proposed value
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj)
            {
                obj["visibility"] = sourceVisibility.ToString();
            }

            var result = node?.ToJsonString() ?? json;

            // Enforce max length of 50,000 characters
            if (result.Length > 50_000)
            {
                result = result[..50_000];
            }

            return result;
        }
        catch
        {
            // If we can't parse, just serialize and enforce the limit
            if (json.Length > 50_000)
            {
                json = json[..50_000];
            }

            return json;
        }
    }

    private static ExtractionRequest BuildExtractionRequest(Source source, Campaign? campaign, IReadOnlyList<ArtifactContext> context)
    {
        return new ExtractionRequest
        {
            SourceBody = source.Body!,
            SourceTitle = source.Title,
            SourceType = source.Type.ToString(),
            SourceVisibility = source.Visibility.ToString(),
            OccurredAt = source.OccurredAt,
            CampaignName = campaign?.Name,
            CampaignStatus = campaign?.Status.ToString(),
            ExistingArtifacts = context
        };
    }


    private static ReviewChangeType ParseChangeType(string changeType) =>
        Enum.Parse<ReviewChangeType>(changeType);

    private static ReviewTargetType ParseTargetType(string targetType) =>
        Enum.Parse<ReviewTargetType>(targetType);

    private static bool IsTransientException(Exception ex) =>
        ex.Message.Contains("429", StringComparison.Ordinal) ||
        ex.Message.Contains("503", StringComparison.Ordinal) ||
        ex.Message.Contains("service unavailable", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 4xx responses other than timeout (408) and throttling (429) mean the request itself is
    /// rejected — retrying sends the same request and fails the same way.
    /// </summary>
    private static bool IsPermanentHttpFailure(HttpRequestException ex) =>
        ex.StatusCode is { } code
        && (int)code >= 400 && (int)code < 500
        && code != System.Net.HttpStatusCode.RequestTimeout
        && code != System.Net.HttpStatusCode.TooManyRequests;

    /// <summary>
    /// A transient failure must put the source back to Queued: the message is abandoned for
    /// redelivery, and the idempotency check skips any source that is not Queued — leaving the
    /// status at Processing would turn every retry into a silent no-op.
    /// </summary>
    private async Task<ExtractionOutcome> TransientOutcomeAsync(
        Source source, string category, string message, CancellationToken ct)
    {
        await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Queued, ct);
        return ExtractionOutcome.Transient(category, message);
    }
}
