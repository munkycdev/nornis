using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Validation;
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
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly IAiExtractionClient _aiExtractionClient;
    private readonly MapExtractionPipeline _mapExtractionPipeline;
    private readonly SourceTextDerivation _sourceTextDerivation;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ExtractionOptions _options;
    private readonly ILogger<ExtractionService> _logger;

    private readonly IReferencePassageRetriever _passageRetriever;

    // A zero-proposal extraction completes its batch with no review step, so the replay walk
    // must be nudged from here; batches WITH proposals advance from the review pipeline instead.
    private readonly IExtractionReplayAdvancer _replayAdvancer;

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
        IAiUsageRecorder usageRecorder,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        IAiExtractionClient aiExtractionClient,
        MapExtractionPipeline mapExtractionPipeline,
        SourceTextDerivation sourceTextDerivation,
        IAiBudgetGuard budgetGuard,
        IUnitOfWork unitOfWork,
        IOptions<ExtractionOptions> options,
        ILogger<ExtractionService> logger,
        // Required, not optional-with-null. These were defaulted so the many existing
        // construction sites kept compiling, which meant a host that forgot to register one
        // lost a feature silently: replays stalled, grounding vanished, nothing said so.
        // Callers with genuinely no library or no replay pass NoOpReferencePassageRetriever /
        // NoOpExtractionReplayAdvancer and say it out loud.
        IReferencePassageRetriever passageRetriever,
        IExtractionReplayAdvancer replayAdvancer)
    {
        _passageRetriever = passageRetriever;
        _replayAdvancer = replayAdvancer;
        _mapExtractionPipeline = mapExtractionPipeline;
        _sourceTextDerivation = sourceTextDerivation;
        _budgetGuard = budgetGuard;
        _sourceRepository = sourceRepository;
        _campaignRepository = campaignRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _reviewProposalRepository = reviewProposalRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _usageRecorder = usageRecorder;
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

        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);

        if (source is null)
        {
            _logger.LogWarning(
                "Source not found. SourceId={SourceId}, WorldId={WorldId}",
                sourceId, worldId);
            return ExtractionOutcome.NonTransient(ErrorCategories.SourceNotFound, "Source not found.");
        }

        // 1a. The message's worldId is taken on trust everywhere below — the budget guard
        //     meters against it, the review batch is filed under it, retrieved context is
        //     scoped by it. A mis-enqueued pair therefore extracts perfectly normally while
        //     spending another world's daily allowance and filing the result somewhere the
        //     source does not live. Nothing downstream can notice, because every consumer is
        //     reading the same wrong id. Non-transient on purpose: redelivering an
        //     inconsistent pair produces the same inconsistency.
        if (source.WorldId != worldId)
        {
            _logger.LogError(
                "Extraction message world mismatch. SourceId={SourceId}, MessageWorldId={MessageWorldId}, SourceWorldId={SourceWorldId}",
                sourceId, worldId, source.WorldId);
            return ExtractionOutcome.NonTransient(ErrorCategories.ValidationFailure,
                "The message's world does not match the source's world.");
        }

        // Extraction opt-out: a queued message for a source stored without extraction
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

        // Idempotency: check the ReviewBatch first — its presence proves extraction
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

        // Idempotency: only Queued sources start extraction — except Processing with
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

        // Claim: Queued → Processing, and only from Queued. The check above and this write
        //    used to be a read followed by an unconditional update, which two deliveries of the
        //    same message could both pass — losing the race now costs nothing, because the loser
        //    stops here, before the first paid call. The Processing branch above is a crashed
        //    run being resumed and is already claimed; the index on ReviewBatches is what stops
        //    *that* one racing a still-live original.
        if (source.ProcessingStatus == SourceProcessingStatus.Queued
            && !await _sourceRepository.TryClaimForExtractionAsync(sourceId, ct))
        {
            _logger.LogInformation(
                "Another delivery already claimed this source for extraction. SourceId={SourceId}", sourceId);
            return ExtractionOutcome.SkippedIdempotent("Another run already claimed this source.");
        }

        var outcome = await RunClaimedExtractionAsync(source, worldId, ct);
        return await ApplyFailureStatusAsync(source.Id, outcome, ct);
    }

    /// <summary>
    /// The claimed source's route through the four pipelines: transcription and attachment
    /// derivation (owned by <see cref="SourceTextDerivation"/>), the map path (owned by
    /// <see cref="MapExtractionPipeline"/>), and the text extraction this class keeps.
    /// The collaborators return verdicts and never touch ProcessingStatus — every
    /// transition an outcome implies is applied by <see cref="ApplyFailureStatusAsync"/>,
    /// so the state machine lives in one method instead of twenty call sites.
    /// </summary>
    private async Task<ExtractionOutcome> RunClaimedExtractionAsync(
        Source source, Guid worldId, CancellationToken ct)
    {
        // 4b. Handwritten notes arrive as page images; vision transcription produces the
        // body here, then the normal pipeline continues. The transcription is persisted,
        // so a redelivered message sees a non-empty body and skips this step.
        if (source.Type == SourceType.HandwrittenNotes && string.IsNullOrWhiteSpace(source.Body))
        {
            var transcriptionOutcome = await _sourceTextDerivation.TranscribeHandwrittenAsync(source, worldId, ct);
            if (transcriptionOutcome is not null)
            {
                return transcriptionOutcome;
            }
        }

        // 4c. Map sources take their own extraction path: place names + positions from
        // the map image become artifact/placemark proposals. Typed notes ride along as
        // naming context; they are not separately extracted. Persistence stays here — a
        // map batch and a text batch commit through the same code.
        if (source.Type == SourceType.Map)
        {
            var mapResult = await _mapExtractionPipeline.ExtractAsync(source, worldId, ct);
            if (mapResult.Failure is not null)
            {
                return mapResult.Failure;
            }

            if (mapResult.Response is null)
            {
                // A map source without a map image has nothing to extract — file it with
                // an empty completed batch, mirroring blank handwriting pages.
                return await HandleEmptyBodyAsync(source, worldId, ct);
            }

            return await HandleSuccessfulResponseAsync(source, worldId, mapResult.Response, ct, AiOperationType.MapExtraction);
        }

        // 4d. Image/Upload sources derive text from their files (PDF text, file
        // contents, vision reads) exactly once. The derived text is persisted before
        // extraction so a redelivered message never re-buys the vision call.
        if (source.Type is SourceType.Image or SourceType.Upload && source.DerivedText is null)
        {
            var derivationOutcome = await _sourceTextDerivation.DeriveAttachmentTextAsync(source, worldId, ct);
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
            source.Body = SourceTextDerivation.ComposeEffectiveBody(source.Body, source.DerivedText);
        }

        if (string.IsNullOrWhiteSpace(source.Body))
        {
            _logger.LogInformation(
                "Source body is empty, creating completed batch with zero proposals. SourceId={SourceId}",
                source.Id);

            return await HandleEmptyBodyAsync(source, worldId, ct);
        }

        // Daily AI budget gate. The message is completed (not redelivered) and the
        // source fails visibly — the GM can retry from the UI once the budget resets.
        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            _logger.LogWarning(
                "Extraction blocked by AI budget. SourceId={SourceId}, WorldId={WorldId}",
                source.Id, worldId);
            return ExtractionOutcome.NonTransient("BudgetExceeded", budgetError.Message);
        }

        var context = await AssembleContextAsync(source, worldId, ct);

        return await InvokeAiWithRetriesAsync(source, worldId, context, ct);
    }

    /// <summary>
    /// The post-claim state machine, in one place. A transient failure puts the source
    /// back to Queued: the message is abandoned for redelivery, and the idempotency check
    /// skips any source that is not Queued — leaving it at Processing would turn every
    /// retry into a silent no-op. A non-transient failure marks it Failed so the problem
    /// surfaces in the UI. Success writes Processed beside its batch commit and a skipped
    /// run writes nothing at all — the concurrent winner owns the status (see
    /// <see cref="LostTheBatchRace"/>) — which is why those two arms are absent here.
    /// </summary>
    private async Task<ExtractionOutcome> ApplyFailureStatusAsync(
        Guid sourceId, ExtractionOutcome outcome, CancellationToken ct)
    {
        switch (outcome.Type)
        {
            case OutcomeType.TransientFailure:
                await _sourceRepository.UpdateProcessingStatusAsync(sourceId, SourceProcessingStatus.Queued, ct);
                break;
            case OutcomeType.NonTransientFailure:
                await _sourceRepository.UpdateProcessingStatusAsync(sourceId, SourceProcessingStatus.Failed, ct);
                break;
        }

        return outcome;
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

        if (await _reviewBatchRepository.TryCreateExtractionBatchAsync(batch, ct) is null)
        {
            return LostTheBatchRace(source.Id);
        }

        await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Processed, ct);
        await TryAdvanceReplayAsync(worldId, source.Id, ct);

        return ExtractionOutcome.Succeeded(batch.Id, 0);
    }

    /// <summary>
    /// A concurrent run committed the source's extraction batch first. The winner owns the
    /// source's status from here, so this run must not touch it — writing Failed would clobber
    /// a Processed the other run had already earned, and writing Processed would be claiming
    /// credit for work it rolled back.
    /// </summary>
    private ExtractionOutcome LostTheBatchRace(Guid sourceId)
    {
        _logger.LogWarning(
            "A concurrent run already committed this source's extraction batch. SourceId={SourceId}", sourceId);
        return ExtractionOutcome.SkippedIdempotent("A concurrent run already committed this source's batch.");
    }

    /// <summary>An empty batch is born Completed — no review will ever touch it, so a
    /// waiting replay is nudged from here.</summary>
    private Task TryAdvanceReplayAsync(Guid worldId, Guid sourceId, CancellationToken ct) =>
        _replayAdvancer.TryAdvanceAsync(worldId, sourceId, ct);

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

        // Ground extraction in the world's published reference shelf (party-visible docs,
        // plus GM-only shelves for a GM-only source). Retrieved once, before the retry loop.
        var referencePassages = await RetrieveReferencePassagesAsync(source, worldId, ct);

        // Where the party last was, so "the tavern" and "back at the keep" resolve to the
        // right place even when this note never names it.
        var recentLocations = await AssembleRecentLocationContextAsync(source, worldId, ct);

        var extractionRequest = BuildExtractionRequest(source, campaign, context, referencePassages, recentLocations);

        // Application owns the prompt text; the client receives finished strings and keeps
        // only transport, timeout, and parse — the same seam the five prompt-in/JSON-out
        // clients already use.
        var request = new AiPromptRequest
        {
            SystemPrompt = ExtractionPromptBuilder.BuildSystemPrompt(extractionRequest),
            UserMessage = ExtractionPromptBuilder.BuildUserMessage(extractionRequest),
            Model = _options.AiModel,
            TimeoutSeconds = _options.AiTimeoutSeconds
        };

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
            catch (AiParseException ex)
            {
                // Malformed AI output (bad JSON, invalid fields) is retryable: sampling
                // variance means the next attempt usually parses. Exhausted retries fall
                // through to the ParseFailure path below.
                lastError = ex.Message;
                _logger.LogWarning(ex,
                    "AI response parse failed on attempt {Attempt}/{MaxAttempts}. SourceId={SourceId}",
                    attempt, maxAttempts, source.Id);

                // Metered per attempt, not once at the end. Unparseable output is still paid
                // output, and a model that needs three tries costs three times — which the
                // daily budget guard used to see as nothing at all, precisely when spend was
                // roughest. Recorded here rather than after the loop so the row count matches
                // the call count.
                await RecordAttemptUsageAsync(source, worldId, ex.Usage, ErrorCategories.ParseFailure, ct);
            }
            catch (AiTimeoutException ex)
            {
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.Timeout, ct);
                return ExtractionOutcome.Transient(ErrorCategories.Timeout, ex.Message);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout — transient failure
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.Timeout, ct);
                return ExtractionOutcome.Transient(ErrorCategories.Timeout, "AI call timed out.");
            }
            catch (Exception ex) when (TransientFailureClassifier.IsPermanentHttpFailure(ex))
            {
                // 4xx (other than 408/429): the request itself is bad — a retry sends the same
                // bytes and fails the same way. Fail the source so the problem surfaces.
                _logger.LogError(ex,
                    "Permanent AI request failure. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.AiCallFailure, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
            }
            catch (Exception ex) when (ex is AiHttpException or HttpRequestException)
            {
                // Network error / 5xx / throttling — transient failure
                _logger.LogWarning(ex,
                    "Network error during AI call. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.TransientError, ct);
                return ExtractionOutcome.Transient(ErrorCategories.TransientError, ex.Message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // propagate cancellation
            }
            catch (Exception ex) when (TransientFailureClassifier.IsTransient(ex))
            {
                _logger.LogWarning(ex,
                    "Transient error during AI call. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.TransientError, ct);
                return ExtractionOutcome.Transient(ErrorCategories.TransientError, ex.Message);
            }
            catch (Exception ex)
            {
                // Non-transient AI call failure
                _logger.LogError(ex,
                    "Non-transient AI call failure. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.AiCallFailure, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
            }
        }

        // All parse retries exhausted — non-transient failure
        _logger.LogError(
            "Parse retries exhausted. SourceId={SourceId}, Error={Error}",
            source.Id, lastError);

        await TrackUsageAsync(source, worldId, lastResponse, false, ErrorCategories.ParseFailure, ct);
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

            if (await _reviewBatchRepository.TryCreateExtractionBatchAsync(emptyBatch, ct) is null)
            {
                // The call was still made and still billed, so it is still metered — the ledger
                // records spend, not usefulness.
                await TrackUsageAsync(source, worldId, response, true, null, ct, null, operationType);
                return LostTheBatchRace(source.Id);
            }

            await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Processed, ct);
            await TrackUsageAsync(source, worldId, response, true, null, ct, emptyBatch.Id, operationType);
            await TryAdvanceReplayAsync(worldId, source.Id, ct);

            return ExtractionOutcome.Succeeded(emptyBatch.Id, 0);
        }

        // Atomic creation: ReviewBatch + ReviewProposals + SourceReferences
        Guid? batchId;
        try
        {
            batchId = await CreateProposalsAtomicallyAsync(source, worldId, response, now, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist proposals atomically. SourceId={SourceId}", source.Id);
            await TrackUsageAsync(source, worldId, response, false, ErrorCategories.ValidationFailure, ct, operationType: operationType);
            return ExtractionOutcome.NonTransient(ErrorCategories.ValidationFailure,
                "Failed to persist proposals: " + ex.Message);
        }

        // Null is a lost race, not a failure, and the difference matters at exactly this line:
        // the catch above marks the source Failed, which for a loser would overwrite the
        // Processed the winner just wrote and report a working extraction as broken.
        if (batchId is null)
        {
            await TrackUsageAsync(source, worldId, response, true, null, ct, null, operationType);
            return LostTheBatchRace(source.Id);
        }

        // Transition source to Processed
        await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Processed, ct);

        // Track usage OUTSIDE the proposal transaction (persists even on rollback)
        await TrackUsageAsync(source, worldId, response, true, null, ct, batchId, operationType);

        return ExtractionOutcome.Succeeded(batchId.Value, response.Proposals.Count);
    }

    /// <summary>
    /// Returns the new batch's id, or null when another run committed this source's extraction
    /// batch first — a rollback with nothing lost, not an error.
    /// </summary>
    private async Task<Guid?> CreateProposalsAtomicallyAsync(
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

            if (await _reviewBatchRepository.TryCreateExtractionBatchAsync(batch, ct) is null)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            foreach (var proposal in response.Proposals)
            {
                var proposedValueJson = EnforceVisibility(proposal.ProposedValue, source.Visibility);
                proposedValueJson = NormalizeIdFields(proposedValueJson, proposal.ChangeType);
                proposedValueJson = NormalizePayloadFields(proposedValueJson);

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

    /// <summary>
    /// One ledger row for one paid call, whether or not its output could be used. Failures
    /// here are swallowed: metering must never be the thing that turns a retryable parse
    /// failure into a lost extraction.
    /// </summary>
    private async Task RecordAttemptUsageAsync(
        Source source, Guid worldId, AiUsage? usage, string errorCode, CancellationToken ct,
        AiOperationType operationType = AiOperationType.SourceExtraction)
    {
        try
        {
            await _usageRecorder.RecordAsync(
                worldId, null, operationType, usage,
                succeeded: false, errorCode: errorCode, sourceId: source.Id,
                fallbackModel: _options.AiModel, ct: ct);
        }
        catch (Exception recordEx)
        {
            _logger.LogWarning(recordEx,
                "Failed to record usage for a failed extraction attempt. SourceId={SourceId}", source.Id);
        }
    }

    private Task TrackUsageAsync(
        Source source, Guid worldId, AiExtractionResponse? response,
        bool succeeded, string? errorCode, CancellationToken ct, Guid? reviewBatchId = null,
        AiOperationType operationType = AiOperationType.SourceExtraction) =>
        _usageRecorder.RecordAsync(
            worldId, null, operationType, response?.Usage,
            succeeded, errorCode, sourceId: source.Id, reviewBatchId: reviewBatchId,
            fallbackModel: _options.AiModel, ct: ct);

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

    /// <summary>
    /// Tidies the model's output into the shapes the payload schemas actually declare, before
    /// it is stored — so a sloppy generation never becomes an unacceptable proposal.
    ///
    /// Two problems, both seen in real extractions:
    ///
    /// 1. Quoted numbers — <c>"confidence": "0.99"</c> — which used to fail deserialization and
    ///    take the whole proposal down with it. Parsing is invariant-culture on purpose: the
    ///    model emits JSON, not locale text, so "0,99" is not a European decimal — it is
    ///    garbage, and it stays a string for the validator to reject. (The applicator and
    ///    validator also tolerate quoted numbers, for payloads stored before this ran and for
    ///    hand edits.)
    ///
    /// 2. Sloppy whitespace in the name fields used for matching — <c>"Salt  Factor"</c>. Names
    ///    are collapsed with <see cref="ArtifactNameKey"/>, the same policy dedup and name
    ///    resolution use, so a stray double space cannot make a proposal reference something
    ///    canon does not appear to contain. Case is preserved: the collapse is about typos,
    ///    not about renaming what the GM will see.
    /// </summary>
    internal static string NormalizePayloadFields(string proposedValueJson)
    {
        try
        {
            if (JsonNode.Parse(proposedValueJson) is not JsonObject obj)
            {
                return proposedValueJson;
            }

            var changed = NormalizeNumericFields(obj);
            changed |= NormalizeNameFields(obj);

            // CreateArtifact carries its pin inline; its coordinates are numeric too.
            if (obj["mapPlacemark"] is JsonObject pin)
            {
                changed |= NormalizeNumericFields(pin);
            }

            return changed ? obj.ToJsonString() : proposedValueJson;
        }
        catch (Exception)
        {
            // Malformed payloads are the validator's problem, not ours.
            return proposedValueJson;
        }
    }

    /// <summary>Numeric fields across every payload schema, matched case-insensitively.</summary>
    private static readonly string[] NumericPayloadFields = ["confidence", "x", "y"];

    /// <summary>
    /// The payload fields that are matched against artifact names — the proposed name on a
    /// create/rename, and the by-name references a fact or relationship resolves through.
    /// </summary>
    private static readonly string[] NamePayloadFields =
        ["name", "artifactName", "artifactAName", "artifactBName"];

    private static bool NormalizeNameFields(JsonObject obj)
    {
        var changed = false;

        foreach (var field in NamePayloadFields)
        {
            var key = FindKey(obj, field);
            if (key is null || obj[key] is not JsonValue value || !value.TryGetValue<string>(out var raw))
            {
                continue;
            }

            var collapsed = ArtifactNameKey.Collapse(raw);
            if (collapsed.Length == 0 || string.Equals(collapsed, raw, StringComparison.Ordinal))
            {
                continue;
            }

            obj[key] = JsonValue.Create(collapsed);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Property names arrive in whatever case the model chose, and the payload readers are
    /// case-insensitive, so field lookup has to be too.
    /// </summary>
    private static string? FindKey(JsonObject obj, string field) =>
        obj.Select(p => p.Key)
            .FirstOrDefault(k => string.Equals(k, field, StringComparison.OrdinalIgnoreCase));

    private static bool NormalizeNumericFields(JsonObject obj)
    {
        var changed = false;

        foreach (var field in NumericPayloadFields)
        {
            var key = FindKey(obj, field);
            if (key is null || obj[key] is not JsonValue value || !value.TryGetValue<string>(out var raw))
            {
                continue;
            }

            // Float, not Number: NumberStyles.Number allows thousands separators, which would
            // read "0,99" as 99 rather than leaving it for the validator to reject.
            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                continue;
            }

            obj[key] = JsonValue.Create(parsed);
            changed = true;
        }

        return changed;
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
            EnsureWithinCap(result);
            return result;
        }
        catch (AiParseException)
        {
            throw;
        }
        catch
        {
            EnsureWithinCap(json);
            return json;
        }
    }

    /// <summary>
    /// Rejects an oversized payload instead of trimming it. The old code cut the string at
    /// a fixed length, which for JSON means slicing mid-token and guaranteeing a payload
    /// that can never deserialize — and it cut at 50,000 while the accept path refused
    /// anything over <see cref="ProposalValidator.MaxJsonLength"/>, so the survivors were
    /// unacceptable anyway. Throwing rolls the batch back and fails the source with a
    /// reason, which is recoverable; a stored proposal nobody can ever accept is not.
    /// </summary>
    private static void EnsureWithinCap(string json)
    {
        if (json.Length > ProposalValidator.MaxJsonLength)
        {
            throw new AiParseException(
                $"Proposed value is {json.Length} characters, over the {ProposalValidator.MaxJsonLength} limit.");
        }
    }

    private static ExtractionRequest BuildExtractionRequest(
        Source source, Campaign? campaign, IReadOnlyList<ArtifactContext> context,
        IReadOnlyList<KnowledgePassage> referencePassages, RecentLocationContext? recentLocations)
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
            ExistingArtifacts = context,
            ReferencePassages = referencePassages,
            RecentLocations = recentLocations
        };
    }

    /// <summary>
    /// The party's last known location, per the record: walks the timeline sources preceding
    /// this source's own moment (nearest first) and returns the first that carries accepted
    /// Location references — extractor-authored and accepted, or hand-linked on the session
    /// page. Pivoting on the source's OccurredAt rather than "now" keeps backfilled imports
    /// and re-extractions anchored to their place in the story; using only accepted references
    /// keeps unreviewed guesses from compounding. Null when no prior source in scope
    /// establishes a location.
    /// </summary>
    private async Task<RecentLocationContext?> AssembleRecentLocationContextAsync(
        Source source, Guid worldId, CancellationToken ct)
    {
        if (_options.LocationContextLookbackSources <= 0)
        {
            return null;
        }

        var filter = VisibilityFilter.ForSourceContext(source.Visibility, source.CreatedByUserId);
        var priors = await _sourceRepository.ListTimelineBeforeAsync(
            worldId, source.CampaignId, source.OccurredAt ?? source.CreatedAt, source.CreatedAt,
            filter, _options.LocationContextLookbackSources, ct);

        foreach (var prior in priors)
        {
            var references = await _sourceReferenceRepository.ListBySourceAsync(prior.Id, ct);
            var artifactIds = references
                .Where(r => r.TargetType == SourceReferenceTargetType.Artifact)
                .Select(r => r.TargetId)
                .Distinct()
                .ToList();
            if (artifactIds.Count == 0)
            {
                continue;
            }

            // Same gate as SourceLocationService: a place, still in canon, and visible to
            // this source's readers — GM-only whereabouts never leak into party context.
            var locations = (await _artifactRepository.ListByIdsAsync(artifactIds, ct))
                .Where(a => a.WorldId == worldId
                    && a.Type == ArtifactType.Location
                    && a.Status != ArtifactStatus.Archived
                    && filter.CanSee(a.Visibility, a.CreatedByUserId))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(a => new PriorLocation { Id = a.Id, Name = a.Name, Summary = a.Summary })
                .ToList();

            if (locations.Count > 0)
            {
                return new RecentLocationContext
                {
                    SourceTitle = prior.Title,
                    OccurredAt = prior.OccurredAt,
                    Locations = locations
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Retrieves published-reference passages from the world's Library to ground extraction.
    /// A party-visible source reads only party-visible shelves; a GM-only source may also read
    /// GM-only shelves. Returns nothing when the world has no indexed documents in scope — and
    /// never throws (the retriever swallows its own failures).
    /// </summary>
    private async Task<IReadOnlyList<KnowledgePassage>> RetrieveReferencePassagesAsync(
        Source source, Guid worldId, CancellationToken ct)
    {
        var allowedScopes = source.Visibility == VisibilityScope.GMOnly
            ? new[] { VisibilityScope.PartyVisible, VisibilityScope.GMOnly }
            : [VisibilityScope.PartyVisible];

        var query = BuildRetrievalQuery(source);
        return await _passageRetriever.RetrieveForScopesAsync(
            query, worldId, allowedScopes, source.CreatedByUserId, ct);
    }

    /// <summary>The retrieval query: the title plus the head of the body, bounded so a long
    /// source doesn't blow the embedding model's token limit.</summary>
    private static string BuildRetrievalQuery(Source source)
    {
        const int maxQueryChars = 4000;
        var body = source.Body ?? string.Empty;
        if (body.Length > maxQueryChars)
        {
            body = body[..maxQueryChars];
        }

        return string.IsNullOrWhiteSpace(source.Title) ? body : $"{source.Title}\n{body}";
    }


    private static ReviewChangeType ParseChangeType(string changeType) =>
        Enum.Parse<ReviewChangeType>(changeType);

    private static ReviewTargetType ParseTargetType(string targetType) =>
        Enum.Parse<ReviewTargetType>(targetType);

    // Retry classification lives in TransientFailureClassifier — one definition shared with
    // library indexing, deciding on typed status codes rather than substrings of the message.

}
