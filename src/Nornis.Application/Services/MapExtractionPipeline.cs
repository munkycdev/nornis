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

/// <summary>
/// What a map read produced. Exactly one of three cases: no stored map image (the
/// orchestrator files an empty completed batch, mirroring blank handwriting pages),
/// proposals synthesized from the image, or a terminal failure verdict for the
/// orchestrator's status mapping to act on.
/// </summary>
public sealed class MapExtractionResult
{
    private MapExtractionResult(AiExtractionResponse? response, ExtractionOutcome? failure)
    {
        Response = response;
        Failure = failure;
    }

    public static readonly MapExtractionResult NoMapImage = new(null, null);

    public static MapExtractionResult Extracted(AiExtractionResponse response) => new(response, null);

    public static MapExtractionResult Failed(ExtractionOutcome outcome) => new(null, outcome);

    public AiExtractionResponse? Response { get; }

    public ExtractionOutcome? Failure { get; }
}

/// <summary>
/// Map extraction: reads place names + normalized positions off the map image and turns
/// them into review proposals — CreateArtifact (with an embedded placemark block) for new
/// places, AddPlacemark for places matching existing Locations. Owned by
/// <see cref="ExtractionService"/> and carved out of it along with
/// <see cref="SourceTextDerivation"/>.
///
/// Same contract as the derivation: this class never writes source status. It returns a
/// verdict — proposals, no-image, or a failure outcome — and the orchestrator's one
/// mapping owns every ProcessingStatus transition, so the state machine did not get
/// smeared across three files by the carve. Persistence stays with the orchestrator too:
/// a map batch and a text batch commit through the same code, keeping "how an extraction
/// batch is written" in one place.
/// </summary>
public class MapExtractionPipeline
{
    private readonly ISourceAttachmentRepository _sourceAttachmentRepository;
    private readonly IMapPlacemarkRepository _mapPlacemarkRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IBlobStorageService _blobStorage;
    private readonly IMapExtractionClient _mapExtractionClient;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly ExtractionOptions _options;
    private readonly ILogger<MapExtractionPipeline> _logger;

    public MapExtractionPipeline(
        ISourceAttachmentRepository sourceAttachmentRepository,
        IMapPlacemarkRepository mapPlacemarkRepository,
        IArtifactRepository artifactRepository,
        IBlobStorageService blobStorage,
        IMapExtractionClient mapExtractionClient,
        IAiBudgetGuard budgetGuard,
        IAiUsageRecorder usageRecorder,
        IOptions<ExtractionOptions> options,
        ILogger<MapExtractionPipeline> logger)
    {
        _sourceAttachmentRepository = sourceAttachmentRepository;
        _mapPlacemarkRepository = mapPlacemarkRepository;
        _artifactRepository = artifactRepository;
        _blobStorage = blobStorage;
        _mapExtractionClient = mapExtractionClient;
        _budgetGuard = budgetGuard;
        _usageRecorder = usageRecorder;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MapExtractionResult> ExtractAsync(Source source, Guid worldId, CancellationToken ct)
    {
        var mapAttachment = (await _sourceAttachmentRepository.ListBySourceAsync(source.Id, ct))
            .FirstOrDefault(a => a.Kind == SourceAttachmentKind.MapImage && a.Status == SourceAttachmentStatus.Stored);

        if (mapAttachment is null)
        {
            _logger.LogInformation("Map source has no stored map image. SourceId={SourceId}", source.Id);
            return MapExtractionResult.NoMapImage;
        }

        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            _logger.LogWarning(
                "Map extraction blocked by AI budget. SourceId={SourceId}, WorldId={WorldId}", source.Id, worldId);
            return MapExtractionResult.Failed(
                ExtractionOutcome.NonTransient("BudgetExceeded", budgetError.Message));
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
            return MapExtractionResult.Failed(
                ExtractionOutcome.NonTransient(ErrorCategories.ValidationFailure,
                    "The map image is missing from storage."));
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
            TimeoutSeconds = _options.AiTimeoutSeconds,
            RefinePositions = _options.MapRefinePositions
        };

        var maxAttempts = 1 + _options.MaxParseRetryAttempts;
        string? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await _mapExtractionClient.ExtractAsync(request, ct);

                var proposals = await BuildMapProposalsAsync(source, mapAttachment, existingLocations, response, ct);

                return MapExtractionResult.Extracted(new AiExtractionResponse
                {
                    Proposals = proposals,
                    Usage = response.Usage
                });
            }
            catch (AiParseException ex)
            {
                lastError = ex.Message;
                _logger.LogWarning(ex,
                    "Map extraction parse failed on attempt {Attempt}/{MaxAttempts}. SourceId={SourceId}",
                    attempt, maxAttempts, source.Id);

                await RecordAttemptUsageAsync(source, worldId, ex.Usage, ErrorCategories.ParseFailure, ct);
            }
            catch (AiTimeoutException ex)
            {
                await TrackUsageAsync(source, worldId, null, false, ErrorCategories.Timeout, ct);
                return MapExtractionResult.Failed(
                    ExtractionOutcome.Transient(ErrorCategories.Timeout, ex.Message));
            }
            catch (TimeoutException ex)
            {
                await TrackUsageAsync(source, worldId, null, false, ErrorCategories.Timeout, ct);
                return MapExtractionResult.Failed(
                    ExtractionOutcome.Transient(ErrorCategories.Timeout, ex.Message));
            }
            catch (Exception ex) when (TransientFailureClassifier.IsPermanentHttpFailure(ex))
            {
                _logger.LogError(ex, "Permanent map extraction failure. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, worldId, null, false, ErrorCategories.AiCallFailure, ct);
                return MapExtractionResult.Failed(
                    ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message));
            }
            catch (Exception ex) when (ex is AiHttpException or HttpRequestException)
            {
                _logger.LogWarning(ex, "Transient map extraction failure. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, worldId, null, false, ErrorCategories.TransientError, ct);
                return MapExtractionResult.Failed(
                    ExtractionOutcome.Transient(ErrorCategories.TransientError, ex.Message));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Non-transient map extraction failure. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, worldId, null, false, ErrorCategories.AiCallFailure, ct);
                return MapExtractionResult.Failed(
                    ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message));
            }
        }

        _logger.LogError("Map extraction parse retries exhausted. SourceId={SourceId}, Error={Error}", source.Id, lastError);
        await TrackUsageAsync(source, worldId, null, false, ErrorCategories.ParseFailure, ct);
        return MapExtractionResult.Failed(
            ExtractionOutcome.NonTransient(ErrorCategories.ParseFailure,
                $"Map extraction failed after {_options.MaxParseRetryAttempts} retries: {lastError}"));
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

    private Task TrackUsageAsync(
        Source source, Guid worldId, AiUsage? usage,
        bool succeeded, string? errorCode, CancellationToken ct) =>
        _usageRecorder.RecordAsync(
            worldId, null, AiOperationType.MapExtraction, usage,
            succeeded, errorCode, sourceId: source.Id, fallbackModel: _options.AiModel, ct: ct);

    /// <summary>
    /// One ledger row for one paid call, whether or not its output could be used. Failures
    /// here are swallowed: metering must never be the thing that turns a retryable parse
    /// failure into a lost extraction.
    /// </summary>
    private async Task RecordAttemptUsageAsync(
        Source source, Guid worldId, AiUsage? usage, string errorCode, CancellationToken ct)
    {
        try
        {
            await _usageRecorder.RecordAsync(
                worldId, null, AiOperationType.MapExtraction, usage,
                succeeded: false, errorCode: errorCode, sourceId: source.Id,
                fallbackModel: _options.AiModel, ct: ct);
        }
        catch (Exception recordEx)
        {
            _logger.LogWarning(recordEx,
                "Failed to record usage for a failed map extraction attempt. SourceId={SourceId}", source.Id);
        }
    }
}
