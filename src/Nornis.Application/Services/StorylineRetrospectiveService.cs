using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class StorylineRetrospectiveService : IStorylineRetrospectiveService
{
    private const int ChunkSize = 40; // stays under the 50-proposal batch convention

    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IAiUsageRecordRepository _aiUsageRecordRepository;
    private readonly IRetrospectiveAiClient _aiClient;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IUnitOfWork _unitOfWork;
    private readonly LoremasterOptions _options;
    private readonly ILogger<StorylineRetrospectiveService> _logger;

    public StorylineRetrospectiveService(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        ISourceRepository sourceRepository,
        IReviewBatchRepository reviewBatchRepository,
        IReviewProposalRepository reviewProposalRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IAiUsageRecordRepository aiUsageRecordRepository,
        IRetrospectiveAiClient aiClient,
        IAiBudgetGuard budgetGuard,
        IUnitOfWork unitOfWork,
        IOptions<LoremasterOptions> options,
        ILogger<StorylineRetrospectiveService> logger)
    {
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _sourceRepository = sourceRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _reviewProposalRepository = reviewProposalRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _aiUsageRecordRepository = aiUsageRecordRepository;
        _aiClient = aiClient;
        _budgetGuard = budgetGuard;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AppResult<RetrospectiveResult>> RunAsync(Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<RetrospectiveResult>.Fail(new AppError(403, "insufficient_role",
                "Only GMs can run a storyline retrospective."));
        }

        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            return AppResult<RetrospectiveResult>.Fail(budgetError);
        }

        var storylines = (await _artifactRepository.ListByWorldAsync(worldId, ArtifactType.Storyline, null, ct))
            .Where(a => a.Status == ArtifactStatus.Active)
            .OrderBy(a => a.Name)
            .ToList();

        if (storylines.Count == 0)
        {
            return AppResult<RetrospectiveResult>.Success(new RetrospectiveResult(0, 0, null));
        }

        var factsByStoryline = new Dictionary<Guid, IReadOnlyList<ArtifactFact>>();
        foreach (var storyline in storylines)
        {
            factsByStoryline[storyline.Id] = await _factRepository.ListByArtifactAsync(storyline.Id, ct);
        }

        // Assess in chunks; collect verdicts across all calls before persisting anything.
        var verdicts = new List<RetrospectiveVerdict>();
        foreach (var chunk in storylines.Chunk(ChunkSize))
        {
            RetrospectiveAiResponse response;
            try
            {
                response = await _aiClient.AssessAsync(new RetrospectiveAiRequest
                {
                    SystemPrompt = BuildSystemPrompt(),
                    UserMessage = BuildUserMessage(chunk, factsByStoryline),
                    Model = _options.AiModel,
                    TimeoutSeconds = _options.AiTimeoutSeconds
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Storyline retrospective AI call failed. WorldId={WorldId}", worldId);
                await TrackUsageAsync(worldId, actingUserId, null, false, "AiCallFailure", ct);
                return AppResult<RetrospectiveResult>.Fail(new AppError(502, "ai_call_failed",
                    "The retrospective AI call failed. Please try again."));
            }

            await TrackUsageAsync(worldId, actingUserId, response, true, null, ct);
            verdicts.AddRange(response.Verdicts);
        }

        // Only verdicts that resolve to a real Active storyline and propose a change count.
        var storylineById = storylines.ToDictionary(s => s.Id);
        var closures = verdicts
            .Select(v => (Verdict: v, Status: MapVerdict(v.Verdict), Ok: Guid.TryParse(v.StorylineId, out var id) ? id : Guid.Empty))
            .Where(x => x.Status is not null && x.Ok != Guid.Empty && storylineById.ContainsKey(x.Ok))
            .DistinctBy(x => x.Ok)
            .ToList();

        if (closures.Count == 0)
        {
            return AppResult<RetrospectiveResult>.Success(new RetrospectiveResult(storylines.Count, 0, null));
        }

        var batchId = await PersistProposalsAsync(worldId, actingUserId, storylines, closures, storylineById, ct);

        _logger.LogInformation(
            "Storyline retrospective complete. WorldId={WorldId}, Assessed={Assessed}, Proposed={Proposed}, BatchId={BatchId}",
            worldId, storylines.Count, closures.Count, batchId);

        return AppResult<RetrospectiveResult>.Success(new RetrospectiveResult(storylines.Count, closures.Count, batchId));
    }

    private async Task<Guid> PersistProposalsAsync(
        Guid worldId,
        Guid actingUserId,
        IReadOnlyList<Artifact> storylines,
        IReadOnlyList<(RetrospectiveVerdict Verdict, ArtifactStatus? Status, Guid Ok)> closures,
        IReadOnlyDictionary<Guid, Artifact> storylineById,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Provenance: the retrospective itself becomes a source, so accepted status
        // changes cite what was assessed and when.
        var body = new StringBuilder();
        body.AppendLine($"AI storyline retrospective over {storylines.Count} active storylines:");
        foreach (var storyline in storylines)
        {
            body.AppendLine($"- {storyline.Name}");
        }

        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.GMNote,
            Title = $"Storyline Retrospective — {now:yyyy-MM-dd}",
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
                CreatedAt = now
            };
            await _reviewBatchRepository.CreateAsync(batch, ct);

            foreach (var (verdict, status, storylineId) in closures)
            {
                var proposal = new ReviewProposal
                {
                    Id = Guid.NewGuid(),
                    ReviewBatchId = batch.Id,
                    ChangeType = ReviewChangeType.UpdateArtifact,
                    TargetType = ReviewTargetType.Artifact,
                    TargetId = storylineId,
                    ProposedValueJson = $$"""{"status":"{{status}}"}""",
                    Rationale = Truncate(verdict.Rationale, 500),
                    Confidence = verdict.Confidence,
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
                    Notes = $"Retrospective verdict for {storylineById[storylineId].Name}",
                    CreatedAt = now
                }, ct);
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

    private static ArtifactStatus? MapVerdict(string verdict) => verdict switch
    {
        "Resolved" => ArtifactStatus.Resolved,
        "Dormant" => ArtifactStatus.Dormant,
        _ => null // StillActive and anything unrecognized produce no proposal
    };

    internal static string BuildSystemPrompt()
    {
        return """
            You are the storyline retrospective engine for Nornis, a tabletop RPG world memory
            system. The user imported or accumulated a long record, and some storylines marked
            Active actually concluded long ago — the players simply never wrote "this is done".

            You receive Active storylines with their recorded facts (including open questions).
            For EACH storyline, return a verdict:
            - "Resolved": the record clearly shows the arc concluded — the mystery answered, the
              threat ended, the goal reached or permanently abandoned.
            - "Dormant": the record shows no meaningful activity or unresolved tension left to
              pursue, but no clear conclusion either.
            - "StillActive": open questions or recent developments show the arc is in motion, or
              the record is too thin to judge. When in doubt, choose StillActive — a wrong
              closure hides a live thread from the table.

            Every verdict needs a one-or-two sentence rationale grounded in the listed facts
            (mention the deciding fact), and a confidence from 0.0 to 1.0. Return a verdict for
            every storyline listed, using its exact storylineId.
            """;
    }

    internal static string BuildUserMessage(
        IReadOnlyList<Artifact> storylines,
        IReadOnlyDictionary<Guid, IReadOnlyList<ArtifactFact>> factsByStoryline)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Active Storylines");
        foreach (var storyline in storylines)
        {
            sb.AppendLine();
            sb.AppendLine($"### {storyline.Name} (storylineId: {storyline.Id})");
            if (!string.IsNullOrWhiteSpace(storyline.Summary))
            {
                sb.AppendLine(storyline.Summary);
            }

            if (factsByStoryline.TryGetValue(storyline.Id, out var facts) && facts.Count > 0)
            {
                sb.AppendLine("Facts:");
                foreach (var fact in facts)
                {
                    var open = fact.Predicate == "open question" && fact.TruthState != TruthState.False
                        ? " [OPEN]"
                        : string.Empty;
                    sb.AppendLine($"- {fact.Predicate}: {fact.Value} (truth: {fact.TruthState}){open}");
                }
            }
            else
            {
                sb.AppendLine("Facts: none recorded.");
            }
        }

        return sb.ToString();
    }

    private async Task TrackUsageAsync(
        Guid worldId, Guid userId, RetrospectiveAiResponse? response, bool succeeded, string? errorCode, CancellationToken ct)
    {
        var record = new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            UserId = userId,
            OperationType = AiOperationType.StorylineRetrospective,
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

    private decimal CalculateCost(RetrospectiveAiResponse? response)
    {
        if (response is null || !_options.ModelPricing.TryGetValue(response.Model, out var pricing))
        {
            return 0m;
        }

        return response.InputTokens * pricing.InputPerMillionTokensUsd / 1_000_000m
             + response.OutputTokens * pricing.OutputPerMillionTokensUsd / 1_000_000m;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
