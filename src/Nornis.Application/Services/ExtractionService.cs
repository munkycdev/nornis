using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class ExtractionService : IExtractionService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IAiUsageRecordRepository _aiUsageRecordRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IAiExtractionClient _aiExtractionClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ExtractionOptions _options;
    private readonly ILogger<ExtractionService> _logger;

    private static readonly string[] ValidChangeTypes =
    [
        "CreateArtifact", "UpdateArtifact", "MergeArtifact",
        "AddFact", "UpdateFact", "AddRelationship", "UpdateRelationship"
    ];

    private static readonly string[] ValidTargetTypes =
    [
        "Artifact", "ArtifactFact", "ArtifactRelationship"
    ];

    public ExtractionService(
        ISourceRepository sourceRepository,
        IReviewBatchRepository reviewBatchRepository,
        IReviewProposalRepository reviewProposalRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IAiUsageRecordRepository aiUsageRecordRepository,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IAiExtractionClient aiExtractionClient,
        IUnitOfWork unitOfWork,
        IOptions<ExtractionOptions> options,
        ILogger<ExtractionService> logger)
    {
        _sourceRepository = sourceRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _reviewProposalRepository = reviewProposalRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _aiUsageRecordRepository = aiUsageRecordRepository;
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _aiExtractionClient = aiExtractionClient;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExtractionOutcome> ProcessExtractionAsync(
        Guid sourceId,
        Guid campaignId,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Starting extraction for SourceId={SourceId}, CampaignId={CampaignId}",
            sourceId, campaignId);

        // 1. Retrieve source
        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);

        if (source is null)
        {
            _logger.LogWarning(
                "Source not found. SourceId={SourceId}, CampaignId={CampaignId}",
                sourceId, campaignId);
            return ExtractionOutcome.NonTransient(ErrorCategories.SourceNotFound, "Source not found.");
        }

        // 2. Idempotency: check source status
        if (source.ProcessingStatus != SourceProcessingStatus.Queued)
        {
            _logger.LogInformation(
                "Source already processed or not in Queued status. SourceId={SourceId}, Status={Status}",
                sourceId, source.ProcessingStatus);
            return ExtractionOutcome.SkippedIdempotent(
                $"Source is in {source.ProcessingStatus} status, not Queued.");
        }

        // 3. Idempotency: check existing ReviewBatch
        var existingBatch = await _reviewBatchRepository.GetBySourceIdAsync(sourceId, ct);

        if (existingBatch is not null)
        {
            _logger.LogInformation(
                "ReviewBatch already exists for source. SourceId={SourceId}, BatchId={BatchId}, BatchStatus={BatchStatus}",
                sourceId, existingBatch.Id, existingBatch.Status);
            return ExtractionOutcome.SkippedIdempotent(
                $"ReviewBatch already exists in {existingBatch.Status} status.");
        }

        // 4. Transition: Queued → Processing
        await _sourceRepository.UpdateProcessingStatusAsync(sourceId, SourceProcessingStatus.Processing, ct);

        // 5. Empty body short-circuit
        if (string.IsNullOrWhiteSpace(source.Body))
        {
            _logger.LogInformation(
                "Source body is empty, creating completed batch with zero proposals. SourceId={SourceId}",
                sourceId);

            return await HandleEmptyBodyAsync(source, campaignId, ct);
        }

        // 6. Context assembly
        var context = await AssembleContextAsync(source, campaignId, ct);

        // 7. AI invocation with parse retry
        return await InvokeAiWithRetriesAsync(source, campaignId, context, ct);
    }

    private async Task<ExtractionOutcome> HandleEmptyBodyAsync(
        Source source, Guid campaignId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
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
        Source source, Guid campaignId, CancellationToken ct)
    {
        var allowedVisibilities = GetAllowedContextScopes(source.Visibility);

        // Load name-matched artifacts
        var nameMatched = await _artifactRepository.ListByNamesInTextAsync(
            campaignId, source.Body!, allowedVisibilities, ct);

        // Load recent artifacts
        var recent = await _artifactRepository.ListRecentByCampaignAsync(
            campaignId, allowedVisibilities, _options.MaxArtifactContextCount, ct);

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

        // Load facts for each artifact
        var artifactIds = merged.Select(a => a.Id).ToList();
        var facts = await _artifactFactRepository.ListByArtifactIdsAsync(
            artifactIds, _options.MaxFactsPerArtifact, ct);

        var factsByArtifact = facts.GroupBy(f => f.ArtifactId)
            .ToDictionary(g => g.Key, g => g.ToList());

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
                    Predicate = f.Predicate,
                    Value = f.Value
                }).ToList()
                : []
        }).ToList();
    }

    private async Task<ExtractionOutcome> InvokeAiWithRetriesAsync(
        Source source, Guid campaignId, IReadOnlyList<ArtifactContext> context, CancellationToken ct)
    {
        var request = BuildExtractionRequest(source, context);
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
                    return await HandleSuccessfulResponseAsync(source, campaignId, response, ct);
                }

                lastError = validationError;
                _logger.LogWarning(
                    "AI response validation failed on attempt {Attempt}/{MaxAttempts}. SourceId={SourceId}, Error={Error}",
                    attempt, maxAttempts, source.Id, validationError);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout — transient failure
                await TrackUsageAsync(source, campaignId, lastResponse, false, ErrorCategories.Timeout, ct);
                return ExtractionOutcome.Transient(ErrorCategories.Timeout, "AI call timed out.");
            }
            catch (HttpRequestException ex)
            {
                // Network error — transient failure
                _logger.LogWarning(ex,
                    "Network error during AI call. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, campaignId, lastResponse, false, ErrorCategories.TransientError, ct);
                return ExtractionOutcome.Transient(ErrorCategories.TransientError, ex.Message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // propagate cancellation
            }
            catch (Exception ex) when (IsTransientException(ex))
            {
                _logger.LogWarning(ex,
                    "Transient error during AI call. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, campaignId, lastResponse, false, ErrorCategories.TransientError, ct);
                return ExtractionOutcome.Transient(ErrorCategories.TransientError, ex.Message);
            }
            catch (Exception ex)
            {
                // Non-transient AI call failure
                _logger.LogError(ex,
                    "Non-transient AI call failure. SourceId={SourceId}", source.Id);
                await TrackUsageAsync(source, campaignId, lastResponse, false, ErrorCategories.AiCallFailure, ct);
                await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
                return ExtractionOutcome.NonTransient(ErrorCategories.AiCallFailure, ex.Message);
            }
        }

        // All parse retries exhausted — non-transient failure
        _logger.LogError(
            "Parse retries exhausted. SourceId={SourceId}, Error={Error}",
            source.Id, lastError);

        await TrackUsageAsync(source, campaignId, lastResponse, false, ErrorCategories.ParseFailure, ct);
        await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
        return ExtractionOutcome.NonTransient(ErrorCategories.ParseFailure,
            $"AI response validation failed after {_options.MaxParseRetryAttempts} retries: {lastError}");
    }

    private async Task<ExtractionOutcome> HandleSuccessfulResponseAsync(
        Source source, Guid campaignId, AiExtractionResponse response, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Handle empty proposals from AI
        if (response.Proposals.Count == 0)
        {
            var emptyBatch = new ReviewBatch
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                SourceId = source.Id,
                Status = ReviewBatchStatus.Completed,
                CreatedAt = now,
                CompletedAt = now
            };

            await _reviewBatchRepository.CreateAsync(emptyBatch, ct);
            await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Processed, ct);
            await TrackUsageAsync(source, campaignId, response, true, null, ct, emptyBatch.Id);

            return ExtractionOutcome.Succeeded(emptyBatch.Id, 0);
        }

        // Atomic creation: ReviewBatch + ReviewProposals + SourceReferences
        Guid batchId;
        try
        {
            batchId = await CreateProposalsAtomicallyAsync(source, campaignId, response, now, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist proposals atomically. SourceId={SourceId}", source.Id);
            await TrackUsageAsync(source, campaignId, response, false, ErrorCategories.ValidationFailure, ct);
            await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Failed, ct);
            return ExtractionOutcome.NonTransient(ErrorCategories.ValidationFailure,
                "Failed to persist proposals: " + ex.Message);
        }

        // Transition source to Processed
        await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Processed, ct);

        // Track usage OUTSIDE the proposal transaction (persists even on rollback)
        await TrackUsageAsync(source, campaignId, response, true, null, ct, batchId);

        return ExtractionOutcome.Succeeded(batchId, response.Proposals.Count);
    }

    private async Task<Guid> CreateProposalsAtomicallyAsync(
        Source source, Guid campaignId, AiExtractionResponse response,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            var batch = new ReviewBatch
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                SourceId = source.Id,
                Status = ReviewBatchStatus.Pending,
                CreatedAt = now
            };

            await _reviewBatchRepository.CreateAsync(batch, ct);

            foreach (var proposal in response.Proposals)
            {
                var proposedValueJson = EnforceVisibility(proposal.ProposedValue, source.Visibility);

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
        Source source, Guid campaignId, AiExtractionResponse? response,
        bool succeeded, string? errorCode, CancellationToken ct, Guid? reviewBatchId = null)
    {
        var costUsd = CalculateCost(response);

        var record = new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            SourceId = source.Id,
            OperationType = AiOperationType.SourceExtraction,
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

    private static ExtractionRequest BuildExtractionRequest(Source source, IReadOnlyList<ArtifactContext> context)
    {
        return new ExtractionRequest
        {
            SourceBody = source.Body!,
            SourceTitle = source.Title,
            SourceType = source.Type.ToString(),
            SourceVisibility = source.Visibility.ToString(),
            OccurredAt = source.OccurredAt,
            ExistingArtifacts = context
        };
    }

    private static IReadOnlyList<VisibilityScope> GetAllowedContextScopes(VisibilityScope sourceVisibility) =>
        sourceVisibility switch
        {
            VisibilityScope.Private => [VisibilityScope.Private],
            VisibilityScope.GMOnly => [VisibilityScope.GMOnly, VisibilityScope.PartyVisible],
            VisibilityScope.PartyVisible => [VisibilityScope.PartyVisible],
            _ => [VisibilityScope.PartyVisible]
        };

    private static ReviewChangeType ParseChangeType(string changeType) =>
        Enum.Parse<ReviewChangeType>(changeType);

    private static ReviewTargetType ParseTargetType(string targetType) =>
        Enum.Parse<ReviewTargetType>(targetType);

    private static bool IsTransientException(Exception ex) =>
        ex.Message.Contains("429", StringComparison.Ordinal) ||
        ex.Message.Contains("503", StringComparison.Ordinal) ||
        ex.Message.Contains("service unavailable", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
}
