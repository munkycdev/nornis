using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Drafts the fix for one continuity finding. The audit names a problem across several cited
/// items; a note on one artifact only addresses one leg, so this service reads the finding plus
/// the full record around its evidence and asks the AI for concrete changes — retire the losing
/// fact (UpdateFact/TruthState), rewrite the drifted summary (UpdateArtifact), settle the
/// relationship (UpdateRelationship), or add the missing follow-up (AddFact). The results land
/// as ordinary Pending review proposals with a synthesized GM-only provenance source (mirroring
/// StorylineRetrospectiveService), so the GM approves every change before canon moves.
/// </summary>
public class ContinuityFixService : IContinuityFixService
{
    /// <summary>Proposals accepted per draft are capped to keep the batch reviewable.</summary>
    public const int MaxProposals = 10;

    /// <summary>Batch kind marking drafts produced by this service.</summary>
    public const string BatchKind = "ContinuityFix";

    public const string SystemPrompt = """
        You are the Continuity Fixer for Nornis, a tabletop RPG world memory system. You receive
        ONE continuity finding — a contradiction, dangling thread, stale storyline, timeline
        conflict, or summary drift — together with the slice of the world record it cites. Your
        job is to propose the smallest set of concrete record changes that RESOLVES the finding
        across every evidence item it cites. A fix that touches one cited item but leaves the
        others contradicting is not a fix.

        ## Allowed changes
        - UpdateFact: correct a fact's value, or retire a fact that lost the argument by setting
          truthState to "False" (or soften it to "Disputed"/"Rumor"). targetRef MUST be the
          fact's [ref:fact:...] id.
        - UpdateArtifact: rewrite a summary that drifted from its facts, or change a storyline's
          status (e.g. "Dormant", "Resolved"). targetRef MUST be the artifact's
          [ref:artifact:...] id.
        - UpdateRelationship: correct a relationship's type or description, or retire it via
          truthState "False". targetRef MUST be the relationship's [ref:rel:...] id.
        - AddFact: record the follow-up the record is missing (for dangling threads). targetRef
          MUST be the [ref:artifact:...] id of the artifact that should own the new fact;
          predicate and value are required.

        ## Grounding rules — non-negotiable
        - targetRef MUST be copied EXACTLY from the record below. A change you cannot anchor to
          a real ref id is not allowed.
        - Propose ONLY changes that resolve THIS finding. Do not tidy unrelated material.
        - Prefer a few precise changes over many speculative ones. When two facts contradict,
          decide which one the record better supports, keep it, and retire the other — say why
          in the rationale.
        - An empty proposals array is a valid answer when no concrete change would help.
        - Every rationale must explain the change in one or two sentences a GM can judge at a
          glance in the review queue.

        ## Truth states
        Confirmed, Likely, Rumor, Disputed, False, Hidden.

        ## Output
        Respond with a JSON object matching the schema: a "proposals" array (0 to 10 items).
        Unused fields are null.
        """;

    private static readonly JsonSerializerOptions PayloadJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IHealthAssessmentRepository _assessmentRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IProposalValidator _proposalValidator;
    private readonly IContinuityFixAiClient _aiClient;
    private readonly IAiUsageRecordRepository _aiUsageRecordRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly LoremasterOptions _options;

    public ContinuityFixService(
        IAiBudgetGuard budgetGuard,
        IHealthAssessmentRepository assessmentRepository,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository,
        ISourceRepository sourceRepository,
        IReviewBatchRepository reviewBatchRepository,
        IReviewProposalRepository reviewProposalRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IProposalValidator proposalValidator,
        IContinuityFixAiClient aiClient,
        IAiUsageRecordRepository aiUsageRecordRepository,
        IUnitOfWork unitOfWork,
        IOptions<LoremasterOptions> options)
    {
        _budgetGuard = budgetGuard;
        _assessmentRepository = assessmentRepository;
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _sourceRepository = sourceRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _reviewProposalRepository = reviewProposalRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _proposalValidator = proposalValidator;
        _aiClient = aiClient;
        _aiUsageRecordRepository = aiUsageRecordRepository;
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public async Task<AppResult<ContinuityFixDraft>> DraftFixAsync(
        Guid worldId, Guid findingId, Guid actingUserId, CancellationToken ct)
    {
        // 0. The finding must exist, belong to this world, and still be worth fixing.
        var finding = await _assessmentRepository.GetFindingByIdAsync(findingId, ct);
        if (finding is null || finding.HealthAssessment.WorldId != worldId)
        {
            return AppResult<ContinuityFixDraft>.Fail(
                new AppError(404, "not_found", "Finding not found."));
        }

        if (finding.Status != ContinuityFindingStatus.Open)
        {
            return AppResult<ContinuityFixDraft>.Fail(
                new AppError(409, "finding_not_open", "Only open findings can be drafted against."));
        }

        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
            return AppResult<ContinuityFixDraft>.Fail(budgetError);

        // 1. Load the record slice around the finding: every cited item, the artifacts that own
        // or anchor them, and those artifacts' full facts and relationships — the fixer needs
        // the surroundings to judge which side of a contradiction the record supports.
        var (artifacts, facts, relationships) = await LoadFindingContextAsync(finding, ct);
        if (artifacts.Count == 0)
        {
            return AppResult<ContinuityFixDraft>.Fail(new AppError(409, "evidence_gone",
                "None of the finding's cited evidence exists anymore. Re-run the assessment instead."));
        }

        var userMessage = BuildUserMessage(finding, artifacts, facts, relationships);

        // 2. Call the AI. Track usage on success and failure alike (parity with the audit).
        var request = new ContinuityFixAiRequest
        {
            SystemPrompt = SystemPrompt,
            UserMessage = userMessage,
            Model = _options.AiModel,
            TimeoutSeconds = _options.AiTimeoutSeconds
        };

        ContinuityFixAiResponse response;
        try
        {
            response = await _aiClient.DraftAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await TrackUsageAsync(worldId, actingUserId, null, false, "ServiceError", ct);
            return AppResult<ContinuityFixDraft>.Fail(
                new AppError(503, "service_unavailable",
                    "The continuity fixer is temporarily unavailable. Please try again."));
        }

        await TrackUsageAsync(worldId, actingUserId, response, true, null, ct);

        // 3. Validate + persist. An empty draft is a valid outcome — no batch is created.
        var drafts = BuildValidatedProposals(
            response.Proposals, _proposalValidator, artifacts, facts, relationships);
        if (drafts.Count == 0)
        {
            return AppResult<ContinuityFixDraft>.Success(new ContinuityFixDraft(null, null, 0));
        }

        var (batchId, sourceId) = await PersistDraftsAsync(worldId, actingUserId, finding, drafts, ct);

        return AppResult<ContinuityFixDraft>.Success(
            new ContinuityFixDraft(batchId, sourceId, drafts.Count));
    }

    // ------------------------------------------------------------------------ Context load --

    private async Task<(
        IReadOnlyList<Artifact> Artifacts,
        IReadOnlyList<ArtifactFact> Facts,
        IReadOnlyList<ArtifactRelationship> Relationships)> LoadFindingContextAsync(
        ContinuityFinding finding, CancellationToken ct)
    {
        var artifactIds = new HashSet<Guid>();
        var factIds = new HashSet<Guid>();
        var relIds = new HashSet<Guid>();

        foreach (var refId in DeserializeEvidence(finding.EvidenceJson))
        {
            if (!ContinuityAuditService.TryParseRef(refId, out var kind, out var id))
                continue;
            switch (kind)
            {
                case "artifact": artifactIds.Add(id); break;
                case "fact": factIds.Add(id); break;
                case "rel": relIds.Add(id); break;
            }
        }

        if (finding.ArtifactId is { } primary)
            artifactIds.Add(primary);

        var citedFacts = await _factRepository.ListByIdsAsync([.. factIds], ct);
        var citedRels = await _relationshipRepository.ListByIdsAsync([.. relIds], ct);

        foreach (var f in citedFacts)
            artifactIds.Add(f.ArtifactId);
        foreach (var r in citedRels)
        {
            artifactIds.Add(r.ArtifactAId);
            artifactIds.Add(r.ArtifactBId);
        }

        var artifacts = await _artifactRepository.ListByIdsAsync([.. artifactIds], ct);
        var involvedIds = artifacts.Select(a => a.Id).ToList();

        // Full LIVE context for the involved artifacts (retired False items stay out, as in the
        // audit), then union the cited items back in — a cited item must never fall out of the
        // prompt to the per-artifact cap or to having been retired since the audit ran.
        var facts = (await _factRepository.ListByArtifactIdsAsync(
                involvedIds, VisibilityFilter.All, ContinuityAuditService.MaxFactsPerArtifactInAudit, ct))
            .Where(f => f.TruthState != TruthState.False)
            .UnionBy(citedFacts, f => f.Id)
            .ToList();
        var relationships = (await _relationshipRepository.ListByArtifactIdsAsync(
                involvedIds, VisibilityFilter.All, ct))
            .Where(r => r.TruthState != TruthState.False)
            .UnionBy(citedRels, r => r.Id)
            .ToList();

        return (artifacts, facts, relationships);
    }

    internal static string BuildUserMessage(
        ContinuityFinding finding,
        IReadOnlyList<Artifact> artifacts,
        IReadOnlyList<ArtifactFact> facts,
        IReadOnlyList<ArtifactRelationship> relationships)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Finding to resolve");
        sb.AppendLine($"- Category: {finding.Category}");
        sb.AppendLine($"- Severity: {finding.Severity}");
        sb.AppendLine($"- Summary: {finding.Summary}");
        if (!string.IsNullOrWhiteSpace(finding.SuggestedAction))
            sb.AppendLine($"- Suggested action: {finding.SuggestedAction}");
        sb.AppendLine($"- Cited evidence: {string.Join(", ", DeserializeEvidence(finding.EvidenceJson).Select(e => $"[ref:{e}]"))}");
        sb.AppendLine();

        sb.Append(ContinuityAuditService.FormatWorldRecord(artifacts, facts, relationships, [], []));

        return sb.ToString();
    }

    // -------------------------------------------------------------------------- Validation --

    /// <summary>A validated proposal ready to persist, minus the batch id assigned at save time.</summary>
    internal sealed record DraftProposal(
        ReviewChangeType ChangeType,
        ReviewTargetType TargetType,
        Guid TargetId,
        string ProposedValueJson,
        string Rationale,
        decimal? Confidence);

    /// <summary>
    /// Turns raw AI proposals into persistable drafts: unknown change types are dropped, the
    /// target ref must resolve to a real item of the matching kind within the finding's
    /// context, enum-ish fields that don't parse are nulled, empty updates are dropped, the
    /// payload must pass the same validator the review queue enforces, and the total is capped.
    /// </summary>
    internal static List<DraftProposal> BuildValidatedProposals(
        IReadOnlyList<ContinuityFixProposal> raw,
        IProposalValidator validator,
        IReadOnlyList<Artifact> artifacts,
        IReadOnlyList<ArtifactFact> facts,
        IReadOnlyList<ArtifactRelationship> relationships)
    {
        var artifactIds = artifacts.Select(a => a.Id).ToHashSet();
        var factIds = facts.Select(f => f.Id).ToHashSet();
        var relIds = relationships.Select(r => r.Id).ToHashSet();

        var result = new List<DraftProposal>();

        foreach (var p in raw)
        {
            if (string.IsNullOrWhiteSpace(p.Rationale))
                continue;

            var normalized = ContinuityAuditService.NormalizeRefId(p.TargetRef ?? string.Empty);
            if (!ContinuityAuditService.TryParseRef(normalized, out var refKind, out var targetId))
                continue;

            var truthState = ParseOrNull<TruthState>(p.TruthState);
            var status = ParseOrNull<ArtifactStatus>(p.Status);
            var confidence = p.Confidence is >= 0 and <= 1 ? p.Confidence : null;

            (ReviewChangeType ChangeType, ReviewTargetType TargetType, object Payload)? mapped =
                p.ChangeType switch
                {
                    "UpdateFact" when refKind == "fact" && factIds.Contains(targetId)
                        && (p.Value is not null || truthState is not null) =>
                        (ReviewChangeType.UpdateFact, ReviewTargetType.ArtifactFact,
                            (object)new UpdateFactPayload(p.Value, confidence, truthState?.ToString(), null)),

                    "UpdateArtifact" when refKind == "artifact" && artifactIds.Contains(targetId)
                        && (p.Name is not null || p.Summary is not null || status is not null) =>
                        (ReviewChangeType.UpdateArtifact, ReviewTargetType.Artifact,
                            new UpdateArtifactPayload(p.Name, p.Summary, null, confidence, status?.ToString())),

                    "UpdateRelationship" when refKind == "rel" && relIds.Contains(targetId)
                        && (p.RelationshipType is not null || p.Description is not null || truthState is not null) =>
                        (ReviewChangeType.UpdateRelationship, ReviewTargetType.ArtifactRelationship,
                            new UpdateRelationshipPayload(p.RelationshipType, p.Description, confidence, truthState?.ToString(), null)),

                    "AddFact" when refKind == "artifact" && artifactIds.Contains(targetId)
                        && !string.IsNullOrWhiteSpace(p.Predicate) && !string.IsNullOrWhiteSpace(p.Value) =>
                        (ReviewChangeType.AddFact, ReviewTargetType.ArtifactFact,
                            new AddFactPayload(p.Predicate!, p.Value!, confidence, truthState?.ToString(), null)),

                    _ => null
                };

            if (mapped is not { } m)
                continue;

            var json = JsonSerializer.Serialize(m.Payload, m.Payload.GetType(), PayloadJson);
            if (!validator.ValidateProposedValue(json, m.ChangeType).IsSuccess)
                continue;

            result.Add(new DraftProposal(
                m.ChangeType, m.TargetType, targetId, json, Truncate(p.Rationale.Trim(), 500), confidence));

            if (result.Count >= MaxProposals)
                break;
        }

        return result;
    }

    private static TEnum? ParseOrNull<TEnum>(string? value) where TEnum : struct, Enum =>
        value is not null && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : null;

    // ------------------------------------------------------------------------- Persistence --

    /// <summary>
    /// Persists the drafts the way StorylineRetrospectiveService does: a synthesized GM-only
    /// provenance source, a Pending batch marked with <see cref="BatchKind"/>, one Pending
    /// proposal per draft, and a source reference per proposal — all in one transaction.
    /// </summary>
    private async Task<(Guid BatchId, Guid SourceId)> PersistDraftsAsync(
        Guid worldId, Guid actingUserId, ContinuityFinding finding,
        IReadOnlyList<DraftProposal> drafts, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var body = new StringBuilder();
        body.AppendLine($"AI-drafted fix for continuity finding ({finding.Category}, {finding.Severity}):");
        body.AppendLine(finding.Summary);
        body.AppendLine();
        body.AppendLine("Proposed changes:");
        foreach (var draft in drafts)
        {
            body.AppendLine($"- {draft.ChangeType}: {draft.Rationale}");
        }

        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.GMNote,
            Title = $"Continuity fix — {Truncate(finding.Summary, 80)}",
            Body = body.ToString(),
            Visibility = VisibilityScope.GMOnly,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = now,
            CreatedByUserId = actingUserId
        };

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _sourceRepository.CreateAsync(source, ct);

            var batch = new ReviewBatch
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                SourceId = source.Id,
                Status = ReviewBatchStatus.Pending,
                Kind = BatchKind,
                CreatedAt = now
            };
            await _reviewBatchRepository.CreateAsync(batch, ct);

            foreach (var draft in drafts)
            {
                var proposal = new ReviewProposal
                {
                    Id = Guid.NewGuid(),
                    ReviewBatchId = batch.Id,
                    ChangeType = draft.ChangeType,
                    TargetType = draft.TargetType,
                    TargetId = draft.TargetId,
                    ProposedValueJson = draft.ProposedValueJson,
                    Rationale = draft.Rationale,
                    Confidence = draft.Confidence,
                    Status = ReviewProposalStatus.Pending,
                    CreatedAt = now
                };
                await _reviewProposalRepository.CreateAsync(proposal, ct);

                await _sourceReferenceRepository.CreateAsync(new SourceReference
                {
                    Id = Guid.NewGuid(),
                    SourceId = source.Id,
                    TargetType = SourceReferenceTargetType.ReviewProposal,
                    TargetId = proposal.Id,
                    Notes = $"Drafted fix for finding: {Truncate(finding.Summary, 200)}",
                    CreatedAt = now
                }, ct);
            }

            await transaction.CommitAsync(ct);
            return (batch.Id, source.Id);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    // ----------------------------------------------------------------------------- Shared --

    private static IReadOnlyList<string> DeserializeEvidence(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";

    // --------------------------------------------------------------------- Usage tracking --

    private async Task TrackUsageAsync(
        Guid worldId, Guid? userId, ContinuityFixAiResponse? response, bool succeeded,
        string? errorCode, CancellationToken ct)
    {
        var record = new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            UserId = userId,
            OperationType = AiOperationType.ContinuityFix,
            Model = response?.Model ?? _options.AiModel,
            InputTokens = response?.InputTokens ?? 0,
            OutputTokens = response?.OutputTokens ?? 0,
            TotalTokens = response?.TotalTokens ?? 0,
            EstimatedCostUsd = CalculateCost(response),
            DurationMs = response?.DurationMs ?? 0,
            Succeeded = succeeded,
            ErrorCode = errorCode,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _aiUsageRecordRepository.CreateAsync(record, ct);
    }

    private decimal CalculateCost(ContinuityFixAiResponse? response)
    {
        if (response is null)
            return 0m;

        if (!_options.ModelPricing.TryGetValue(response.Model, out var pricing))
            return 0m;

        var inputCost = response.InputTokens * pricing.InputPerMillionTokensUsd / 1_000_000m;
        var outputCost = response.OutputTokens * pricing.OutputPerMillionTokensUsd / 1_000_000m;

        return inputCost + outputCost;
    }
}
