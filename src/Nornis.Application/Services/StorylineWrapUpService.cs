using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Application;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// The session wrap-up composes services that already exist rather than adding a parallel
/// closure pipeline: staleness from the continuity signal, lineage from pending PartOf
/// proposals, and — on apply — closures through the proposal applicator (confirm-and-apply,
/// like <see cref="ArtifactMergeService"/>), accept/reject through <see cref="IReviewService"/>,
/// and parenting through <see cref="IArtifactService.SetStorylineParentAsync"/>.
/// </summary>
public class StorylineWrapUpService : IStorylineWrapUpService
{
    public const string BatchKind = "SessionWrapUp";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly StorylineDevelopmentReader _reader;
    private readonly ContinuityOptions _options;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IProposalApplicator _proposalApplicator;
    private readonly IReviewService _reviewService;
    private readonly IArtifactService _artifactService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StorylineWrapUpService> _logger;

    public StorylineWrapUpService(
        StorylineDevelopmentReader reader,
        IOptions<ContinuityOptions> options,
        IArtifactRepository artifactRepository,
        IReviewProposalRepository reviewProposalRepository,
        IReviewBatchRepository reviewBatchRepository,
        ISourceRepository sourceRepository,
        IProposalApplicator proposalApplicator,
        IReviewService reviewService,
        IArtifactService artifactService,
        IUnitOfWork unitOfWork,
        ILogger<StorylineWrapUpService> logger)
    {
        _reader = reader;
        _options = options.Value;
        _artifactRepository = artifactRepository;
        _reviewProposalRepository = reviewProposalRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _sourceRepository = sourceRepository;
        _proposalApplicator = proposalApplicator;
        _reviewService = reviewService;
        _artifactService = artifactService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    // ------------------------------------------------------------------------ Read --

    public async Task<AppResult<WrapUpView>> GetWrapUpAsync(
        Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<WrapUpView>.Fail(new AppError(403, "insufficient_role",
                "Only GMs can open the session wrap-up."));
        }

        var data = await _reader.ReadAsync(worldId, requestingUserId, role, ct);
        var continuity = StorylineContinuityService.BuildReport(data, _options.StaleThresholdSessions);

        // The recent window: the last k dated sessions, and which storylines they touched.
        var sessionSourceIds = data.Developments.Keys.Select(k => k.SourceId).Distinct().ToList();
        var recentSessionIds = sessionSourceIds
            .OrderByDescending(id => data.Sources[id].OccurredAt!.Value)
            .Take(_options.RecentSessionWindow)
            .ToHashSet();
        var recentlyTouched = data.Developments.Keys
            .Where(k => recentSessionIds.Contains(k.SourceId))
            .Select(k => k.StorylineId)
            .ToHashSet();

        var advanced = BuildAdvanced(data, recentSessionIds);
        var unparented = BuildUnparented(data, recentSessionIds);
        var couldNest = await BuildCouldNestAsync(worldId, data, recentlyTouched, ct);

        var parentOptions = data.Storylines
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new WrapUpParentOption(s.Id, s.Name, s.Status.ToString()))
            .ToList();

        var hasWork = continuity.Quiet.Count > 0 || couldNest.Count > 0 || unparented.Count > 0;

        return AppResult<WrapUpView>.Success(new WrapUpView(
            hasWork,
            continuity.LatestSession,
            advanced,
            continuity.Quiet,
            couldNest,
            unparented,
            parentOptions));
    }

    private static List<WrapUpAdvanced> BuildAdvanced(
        StorylineDevelopmentData data, IReadOnlySet<Guid> recentSessionIds)
    {
        var advanced = new List<WrapUpAdvanced>();
        foreach (var s in data.Storylines)
        {
            var recentDevs = data.Developments
                .Where(kv => kv.Key.StorylineId == s.Id && recentSessionIds.Contains(kv.Key.SourceId))
                .ToList();
            if (recentDevs.Count == 0)
            {
                continue;
            }

            var count = recentDevs.Sum(kv => kv.Value.Count);
            var lastDev = recentDevs.Max(kv => data.Sources[kv.Key.SourceId].OccurredAt!.Value);
            advanced.Add(new WrapUpAdvanced(s.Id, s.Name, s.Status.ToString(), count, lastDev));
        }

        return advanced.OrderByDescending(a => a.LastDevelopmentAt).ToList();
    }

    private static List<WrapUpUnparented> BuildUnparented(
        StorylineDevelopmentData data, IReadOnlySet<Guid> recentSessionIds)
    {
        var unparented = new List<WrapUpUnparented>();
        foreach (var s in data.Storylines)
        {
            if (data.ParentByChild.ContainsKey(s.Id))
            {
                continue; // already nested
            }

            var devs = data.Developments
                .Where(kv => kv.Key.StorylineId == s.Id)
                .Select(kv => (kv.Key.SourceId, Date: data.Sources[kv.Key.SourceId].OccurredAt!.Value))
                .OrderBy(x => x.Date)
                .ToList();
            if (devs.Count == 0)
            {
                continue; // unanchored — not a recent arc
            }

            // A recent arc: its very first dated development lands inside the window.
            if (!recentSessionIds.Contains(devs[0].SourceId))
            {
                continue;
            }

            unparented.Add(new WrapUpUnparented(s.Id, s.Name, s.Status.ToString(), devs[0].Date));
        }

        return unparented.OrderByDescending(u => u.FirstDevelopmentAt).ToList();
    }

    private async Task<List<WrapUpNestSuggestion>> BuildCouldNestAsync(
        Guid worldId, StorylineDevelopmentData data, IReadOnlySet<Guid> recentlyTouched, CancellationToken ct)
    {
        var pending = await _reviewProposalRepository.ListPendingByWorldAsync(worldId, ct);

        var suggestions = new List<WrapUpNestSuggestion>();
        foreach (var proposal in pending)
        {
            if (proposal.ChangeType != ReviewChangeType.AddRelationship)
            {
                continue;
            }

            AddRelationshipPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<AddRelationshipPayload>(proposal.ProposedValueJson, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (payload is null
                || !string.Equals(payload.Type, ArtifactService.PartOfRelationshipType, StringComparison.Ordinal)
                || payload.ArtifactAId is not { } childId
                || payload.ArtifactBId is not { } parentId)
            {
                continue;
            }

            // Both endpoints must be visible storylines; the child must be recently touched.
            if (!data.AllArtifacts.TryGetValue(childId, out var child)
                || !data.AllArtifacts.TryGetValue(parentId, out var parent)
                || child.Type != ArtifactType.Storyline
                || parent.Type != ArtifactType.Storyline
                || !recentlyTouched.Contains(childId))
            {
                continue;
            }

            suggestions.Add(new WrapUpNestSuggestion(
                proposal.Id, childId, child.Name, parentId, parent.Name, proposal.Rationale, proposal.Confidence));
        }

        return suggestions
            .OrderBy(s => s.ChildName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ----------------------------------------------------------------------- Apply --

    public async Task<AppResult<WrapUpResult>> ApplyAsync(WrapUpDecisionsCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<WrapUpResult>.Fail(new AppError(403, "insufficient_role",
                "Only GMs can apply a session wrap-up."));
        }

        // A wrap-up only ever proposes closing a storyline down, never reopening it.
        foreach (var closure in command.Closures)
        {
            if (closure.Status is not (ArtifactStatus.Dormant or ArtifactStatus.Resolved))
            {
                return AppResult<WrapUpResult>.Fail(new AppError(400, "invalid_closure_status",
                    "A wrap-up closure must set Dormant or Resolved."));
            }
        }

        // 1. Closures — one synthetic source + batch, applied and accepted (confirm-and-apply).
        Guid? batchId = null;
        var closed = 0;
        if (command.Closures.Count > 0)
        {
            var closureResult = await ApplyClosuresAsync(command, ct);
            if (!closureResult.IsSuccess)
            {
                return AppResult<WrapUpResult>.Fail(closureResult.Error!);
            }
            batchId = closureResult.Value;
            closed = command.Closures.Count;
        }

        // 2..4 delegate to existing self-contained flows (each manages its own transaction),
        // so they run outside the closure transaction rather than nested inside it.
        var nested = 0;
        foreach (var proposalId in command.AcceptProposalIds)
        {
            var result = await _reviewService.AcceptProposalAsync(
                new AcceptProposalCommand(proposalId, command.WorldId, command.ActingUserId, command.ActingUserRole), ct);
            if (!result.IsSuccess)
            {
                return AppResult<WrapUpResult>.Fail(result.Error!);
            }
            nested++;
        }

        var rejected = 0;
        foreach (var proposalId in command.RejectProposalIds)
        {
            var result = await _reviewService.RejectProposalAsync(
                new RejectProposalCommand(proposalId, command.WorldId, command.ActingUserId, command.ActingUserRole), ct);
            if (!result.IsSuccess)
            {
                return AppResult<WrapUpResult>.Fail(result.Error!);
            }
            rejected++;
        }

        var parented = 0;
        foreach (var assignment in command.Parents)
        {
            var result = await _artifactService.SetStorylineParentAsync(
                new SetStorylineParentCommand(
                    assignment.ChildStorylineId, command.WorldId, command.ActingUserId,
                    command.ActingUserRole, assignment.ParentStorylineId), ct);
            if (!result.IsSuccess)
            {
                return AppResult<WrapUpResult>.Fail(result.Error!);
            }
            parented++;
        }

        _logger.LogInformation(
            "Session wrap-up applied. WorldId={WorldId}, Closed={Closed}, Nested={Nested}, Rejected={Rejected}, Parented={Parented}, BatchId={BatchId}",
            command.WorldId, closed, nested, rejected, parented, batchId);

        return AppResult<WrapUpResult>.Success(new WrapUpResult(closed, nested, rejected, parented, batchId));
    }

    private async Task<AppResult<Guid>> ApplyClosuresAsync(WrapUpDecisionsCommand command, CancellationToken ct)
    {
        // Validate every target before writing anything.
        var targets = new List<(WrapUpClosure Closure, Artifact Artifact)>();
        foreach (var closure in command.Closures)
        {
            var artifact = await _artifactRepository.GetByIdAsync(closure.StorylineId, ct);
            if (artifact is null || artifact.WorldId != command.WorldId || artifact.Type != ArtifactType.Storyline)
            {
                return AppResult<Guid>.Fail(new AppError(404, "not_found",
                    $"Storyline {closure.StorylineId} not found in this world."));
            }
            targets.Add((closure, artifact));
        }

        var now = DateTimeOffset.UtcNow;

        var body = new StringBuilder();
        body.AppendLine($"Session wrap-up closures ({targets.Count}):");
        foreach (var (closure, artifact) in targets)
        {
            body.AppendLine($"- {artifact.Name} → {closure.Status}");
        }

        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            Type = SourceType.GMNote,
            Title = Truncate($"Session wrap-up — {now:yyyy-MM-dd}", 200),
            Body = body.ToString(),
            Visibility = VisibilityScope.GMOnly,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = now,
            CreatedByUserId = command.ActingUserId
        };

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _sourceRepository.CreateAsync(source, ct);

            var batch = new ReviewBatch
            {
                Id = Guid.NewGuid(),
                WorldId = command.WorldId,
                SourceId = source.Id,
                Kind = BatchKind,
                Status = ReviewBatchStatus.Completed,
                CreatedAt = now,
                CompletedAt = now
            };
            await _reviewBatchRepository.CreateAsync(batch, ct);

            foreach (var (closure, artifact) in targets)
            {
                var proposal = new ReviewProposal
                {
                    Id = Guid.NewGuid(),
                    ReviewBatchId = batch.Id,
                    ChangeType = ReviewChangeType.UpdateArtifact,
                    TargetType = ReviewTargetType.Artifact,
                    TargetId = artifact.Id,
                    ProposedValueJson = $$"""{"status":"{{closure.Status}}"}""",
                    Rationale = "GM closed this storyline during the session wrap-up.",
                    Status = ReviewProposalStatus.Pending,
                    CreatedAt = now
                };
                await _reviewProposalRepository.CreateAsync(proposal, ct);

                var applyResult = await _proposalApplicator.ApplyAsync(proposal, batch, ct);
                if (!applyResult.IsSuccess)
                {
                    await transaction.RollbackAsync(ct);
                    return AppResult<Guid>.Fail(applyResult.Error!);
                }

                proposal.Status = ReviewProposalStatus.Accepted;
                proposal.ReviewedAt = now;
                proposal.ReviewedByUserId = command.ActingUserId;
                await _reviewProposalRepository.UpdateAsync(proposal, ct);
            }

            await transaction.CommitAsync(ct);
            return AppResult<Guid>.Success(batch.Id);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
