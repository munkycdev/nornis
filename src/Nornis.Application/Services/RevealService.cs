using System.Text;
using Microsoft.Extensions.Logging;
using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Reveal promotes a GM-curated set of GM-only knowledge to the party. It reuses the review
/// machinery wholesale: every change is an accepted <c>Update*</c> proposal on a synthetic,
/// party-visible reveal source (so the applicator flips visibility <em>and</em> stamps
/// player-visible provenance), applied through the real <see cref="IProposalApplicator"/> in a
/// single transaction — the same confirm-and-apply shape as
/// <see cref="ArtifactMergeService"/>. It never lowers visibility and never touches
/// <c>Private</c> knowledge.
/// </summary>
public class RevealService : IRevealService
{
    private const string RevealVisibilityJson = """{"visibility":"PartyVisible"}""";

    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly IProposalApplicator _proposalApplicator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RevealService> _logger;

    public RevealService(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository,
        ISourceRepository sourceRepository,
        IReviewBatchRepository reviewBatchRepository,
        IReviewProposalRepository reviewProposalRepository,
        IProposalApplicator proposalApplicator,
        IUnitOfWork unitOfWork,
        ILogger<RevealService> logger)
    {
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _sourceRepository = sourceRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _reviewProposalRepository = reviewProposalRepository;
        _proposalApplicator = proposalApplicator;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AppResult<RevealResult>> RevealAsync(RevealCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return Fail(403, "insufficient_role", "Only GMs can reveal knowledge.");
        }

        // Current visibility of every artifact the reveal touches — directly, or as a fact's
        // parent or a relationship's endpoint. Drives the closure check.
        var knownArtifactVisibility = new Dictionary<Guid, VisibilityScope>();

        async Task<AppError?> RecordArtifactVisibilityAsync(Guid artifactId)
        {
            if (knownArtifactVisibility.ContainsKey(artifactId))
            {
                return null;
            }

            var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);
            if (artifact is null || artifact.WorldId != command.WorldId)
            {
                return new AppError(404, "not_found", $"Artifact {artifactId} not found in this world.");
            }

            knownArtifactVisibility[artifactId] = artifact.Visibility;
            return null;
        }

        // 1. Artifacts to promote (GMOnly only; already-PartyVisible are no-ops, Private rejected).
        var artifactsToReveal = new List<Artifact>();
        foreach (var id in command.ArtifactIds.Distinct())
        {
            var artifact = await _artifactRepository.GetByIdAsync(id, ct);
            if (artifact is null || artifact.WorldId != command.WorldId)
            {
                return Fail(404, "not_found", $"Artifact {id} not found in this world.");
            }

            knownArtifactVisibility[id] = artifact.Visibility;

            if (PrivateGuard(artifact.Visibility, "artifact", id) is { } priv)
            {
                return AppResult<RevealResult>.Fail(priv);
            }

            if (artifact.Visibility == VisibilityScope.GMOnly)
            {
                artifactsToReveal.Add(artifact);
            }
        }

        // 2. Facts to promote; each needs its parent artifact visible (closure).
        var factsToReveal = new List<ArtifactFact>();
        var factParentIds = new List<Guid>();
        foreach (var id in command.FactIds.Distinct())
        {
            var fact = await _factRepository.GetByIdAsync(id, ct);
            if (fact is null)
            {
                return Fail(404, "not_found", $"Fact {id} not found.");
            }

            if (await RecordArtifactVisibilityAsync(fact.ArtifactId) is { } parentError)
            {
                return AppResult<RevealResult>.Fail(parentError);
            }

            if (PrivateGuard(fact.Visibility, "fact", id) is { } priv)
            {
                return AppResult<RevealResult>.Fail(priv);
            }

            if (fact.Visibility == VisibilityScope.GMOnly)
            {
                factsToReveal.Add(fact);
                factParentIds.Add(fact.ArtifactId);
            }
        }

        // 3. Relationships to promote; each needs both endpoint artifacts visible (closure).
        var relationshipsToReveal = new List<ArtifactRelationship>();
        foreach (var id in command.RelationshipIds.Distinct())
        {
            var relationship = await _relationshipRepository.GetByIdAsync(id, ct);
            if (relationship is null || relationship.WorldId != command.WorldId)
            {
                return Fail(404, "not_found", $"Relationship {id} not found in this world.");
            }

            if (PrivateGuard(relationship.Visibility, "relationship", id) is { } priv)
            {
                return AppResult<RevealResult>.Fail(priv);
            }

            if (relationship.Visibility == VisibilityScope.GMOnly)
            {
                relationshipsToReveal.Add(relationship);
                if (await RecordArtifactVisibilityAsync(relationship.ArtifactAId) is { } aError)
                {
                    return AppResult<RevealResult>.Fail(aError);
                }
                if (await RecordArtifactVisibilityAsync(relationship.ArtifactBId) is { } bError)
                {
                    return AppResult<RevealResult>.Fail(bError);
                }
            }
        }

        // 4. Corrections: existing facts to re-truth-state as the reveal supersedes them.
        var corrections = new List<FactCorrection>();
        foreach (var correction in command.Corrections)
        {
            var fact = await _factRepository.GetByIdAsync(correction.FactId, ct);
            if (fact is null)
            {
                return Fail(404, "not_found", $"Fact {correction.FactId} not found.");
            }

            if (await RecordArtifactVisibilityAsync(fact.ArtifactId) is { } parentError)
            {
                return AppResult<RevealResult>.Fail(parentError);
            }

            corrections.Add(correction);
        }

        // 5. Closure: reject an incomplete set whole, returning the missing dependencies so the
        //    GM can confirm the expanded scope — never silently reveal more than asked.
        var missing = RevealClosure.MissingArtifactDependencies(
            artifactsToReveal.Select(a => a.Id).ToList(),
            factParentIds,
            relationshipsToReveal.Select(r => (r.ArtifactAId, r.ArtifactBId)).ToList(),
            knownArtifactVisibility);

        if (missing.Count > 0)
        {
            return AppResult<RevealResult>.Success(new RevealResult(null, 0, 0, 0, 0, missing));
        }

        // 6. Nothing to do (all no-ops) — idempotent success, no batch minted.
        if (artifactsToReveal.Count == 0 && factsToReveal.Count == 0
            && relationshipsToReveal.Count == 0 && corrections.Count == 0)
        {
            return AppResult<RevealResult>.Success(new RevealResult(null, 0, 0, 0, 0, []));
        }

        // 7. Provenance + apply, all in one transaction.
        var now = DateTimeOffset.UtcNow;
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            Type = SourceType.Reveal,
            Title = Truncate($"Reveal — {now:yyyy-MM-dd}", 200),
            Body = BuildBody(command.Note, artifactsToReveal, factsToReveal.Count, relationshipsToReveal.Count, corrections.Count),
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = now,
            CreatedByUserId = command.ActingUserId
        };

        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Completed,
            Kind = "Reveal",
            CreatedAt = now,
            CompletedAt = now
        };

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _sourceRepository.CreateAsync(source, ct);
            await _reviewBatchRepository.CreateAsync(batch, ct);

            foreach (var artifact in artifactsToReveal)
            {
                if (await ApplyAsync(batch, ReviewChangeType.UpdateArtifact, ReviewTargetType.Artifact,
                        artifact.Id, RevealVisibilityJson, "Revealed to the party.", command.ActingUserId, now, ct) is { } error)
                {
                    await transaction.RollbackAsync(ct);
                    return AppResult<RevealResult>.Fail(error);
                }
            }

            foreach (var fact in factsToReveal)
            {
                if (await ApplyAsync(batch, ReviewChangeType.UpdateFact, ReviewTargetType.ArtifactFact,
                        fact.Id, RevealVisibilityJson, "Revealed to the party.", command.ActingUserId, now, ct) is { } error)
                {
                    await transaction.RollbackAsync(ct);
                    return AppResult<RevealResult>.Fail(error);
                }
            }

            foreach (var relationship in relationshipsToReveal)
            {
                if (await ApplyAsync(batch, ReviewChangeType.UpdateRelationship, ReviewTargetType.ArtifactRelationship,
                        relationship.Id, RevealVisibilityJson, "Revealed to the party.", command.ActingUserId, now, ct) is { } error)
                {
                    await transaction.RollbackAsync(ct);
                    return AppResult<RevealResult>.Fail(error);
                }
            }

            foreach (var correction in corrections)
            {
                var json = $$"""{"truthState":"{{correction.TruthState}}"}""";
                if (await ApplyAsync(batch, ReviewChangeType.UpdateFact, ReviewTargetType.ArtifactFact,
                        correction.FactId, json, "Corrected on reveal.", command.ActingUserId, now, ct) is { } error)
                {
                    await transaction.RollbackAsync(ct);
                    return AppResult<RevealResult>.Fail(error);
                }
            }

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        _logger.LogInformation(
            "Reveal applied. WorldId={WorldId}, Artifacts={Artifacts}, Facts={Facts}, Relationships={Relationships}, Corrections={Corrections}, BatchId={BatchId}, User={UserId}",
            command.WorldId, artifactsToReveal.Count, factsToReveal.Count, relationshipsToReveal.Count, corrections.Count, batch.Id, command.ActingUserId);

        return AppResult<RevealResult>.Success(new RevealResult(
            batch.Id, artifactsToReveal.Count, factsToReveal.Count, relationshipsToReveal.Count, corrections.Count, []));
    }

    public async Task<AppResult<RevealSourceResult>> RevealSourceAsync(
        Guid worldId, Guid sourceId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<RevealSourceResult>.Fail(new AppError(403, "insufficient_role", "Only GMs can reveal a source."));
        }

        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null || source.WorldId != worldId)
        {
            return AppResult<RevealSourceResult>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        if (source.Visibility == VisibilityScope.PartyVisible)
        {
            return AppResult<RevealSourceResult>.Success(new RevealSourceResult(source.Id, source.Title, true));
        }

        if (source.Visibility == VisibilityScope.Private)
        {
            return AppResult<RevealSourceResult>.Fail(new AppError(400, "cannot_reveal_private",
                "Cannot reveal a Private source; reveal promotes GM-only material to the party."));
        }

        // GMOnly -> PartyVisible via the scoped write, deliberately bypassing SourceService's
        // post-extraction visibility lock — reveal is the sanctioned way to surface it.
        await _sourceRepository.UpdateVisibilityAsync(sourceId, VisibilityScope.PartyVisible, ct);

        _logger.LogInformation(
            "Source revealed to the party. WorldId={WorldId}, SourceId={SourceId}, User={UserId}",
            worldId, sourceId, actingUserId);

        return AppResult<RevealSourceResult>.Success(new RevealSourceResult(source.Id, source.Title, false));
    }

    /// <summary>
    /// Creates a Pending <c>Update*</c> proposal, applies it through the real applicator (which
    /// flips visibility / truth state and stamps party-visible provenance), and accept-stamps it.
    /// Returns the applicator's error to roll the reveal back, or null on success.
    /// </summary>
    private async Task<AppError?> ApplyAsync(
        ReviewBatch batch, ReviewChangeType changeType, ReviewTargetType targetType,
        Guid targetId, string proposedValueJson, string rationale, Guid actingUserId,
        DateTimeOffset now, CancellationToken ct)
    {
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = changeType,
            TargetType = targetType,
            TargetId = targetId,
            ProposedValueJson = proposedValueJson,
            Rationale = rationale,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = now
        };
        await _reviewProposalRepository.CreateAsync(proposal, ct);

        var applyResult = await _proposalApplicator.ApplyAsync(proposal, batch, ct);
        if (!applyResult.IsSuccess)
        {
            return applyResult.Error;
        }

        proposal.Status = ReviewProposalStatus.Accepted;
        proposal.ReviewedAt = now;
        proposal.ReviewedByUserId = actingUserId;
        await _reviewProposalRepository.UpdateAsync(proposal, ct);
        return null;
    }

    private static AppError? PrivateGuard(VisibilityScope visibility, string kind, Guid id) =>
        visibility == VisibilityScope.Private
            ? new AppError(400, "cannot_reveal_private",
                $"Cannot reveal a Private {kind} ({id}); reveal promotes GM-only knowledge only.")
            : null;

    private static string BuildBody(
        string? note, IReadOnlyList<Artifact> artifacts, int facts, int relationships, int corrections)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(note))
        {
            sb.AppendLine(note.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("Revealed to the party:");
        foreach (var artifact in artifacts)
        {
            sb.AppendLine($"- {artifact.Type}: {artifact.Name}");
        }
        if (facts > 0)
        {
            sb.AppendLine($"- {facts} fact(s)");
        }
        if (relationships > 0)
        {
            sb.AppendLine($"- {relationships} relationship(s)");
        }
        if (corrections > 0)
        {
            sb.AppendLine($"- {corrections} correction(s)");
        }

        return sb.ToString().TrimEnd();
    }

    private static AppResult<RevealResult> Fail(int status, string code, string message) =>
        AppResult<RevealResult>.Fail(new AppError(status, code, message));

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
