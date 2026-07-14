using Microsoft.Extensions.Logging;
using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class ArtifactMergeService : IArtifactMergeService
{
    private readonly IArtifactRepository _artifactRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly IProposalApplicator _proposalApplicator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ArtifactMergeService> _logger;

    public ArtifactMergeService(
        IArtifactRepository artifactRepository,
        ISourceRepository sourceRepository,
        IReviewBatchRepository reviewBatchRepository,
        IReviewProposalRepository reviewProposalRepository,
        IProposalApplicator proposalApplicator,
        IUnitOfWork unitOfWork,
        ILogger<ArtifactMergeService> logger)
    {
        _artifactRepository = artifactRepository;
        _sourceRepository = sourceRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _reviewProposalRepository = reviewProposalRepository;
        _proposalApplicator = proposalApplicator;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AppResult<Guid>> MergeAsync(
        Guid worldId,
        Guid duplicateArtifactId,
        Guid targetArtifactId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<Guid>.Fail(new AppError(403, "insufficient_role", "Only GMs can merge artifacts."));
        }

        if (duplicateArtifactId == targetArtifactId)
        {
            return AppResult<Guid>.Fail(new AppError(400, "invalid_merge", "An artifact cannot be merged into itself."));
        }

        var duplicate = await _artifactRepository.GetByIdAsync(duplicateArtifactId, ct);
        if (duplicate is null || duplicate.WorldId != worldId)
        {
            return AppResult<Guid>.Fail(new AppError(404, "not_found", "Duplicate artifact not found."));
        }

        var target = await _artifactRepository.GetByIdAsync(targetArtifactId, ct);
        if (target is null || target.WorldId != worldId)
        {
            return AppResult<Guid>.Fail(new AppError(404, "not_found", "Target artifact not found."));
        }

        var now = DateTimeOffset.UtcNow;

        // Provenance: the merge is an ordinary accepted MergeArtifact proposal, tied to
        // a synthetic source recording who folded what into what.
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.GMNote,
            Title = Truncate($"Artifact merge — {duplicate.Name} → {target.Name} — {now:yyyy-MM-dd}", 200),
            Body = $"GM merged duplicate artifact \"{duplicate.Name}\" ({duplicate.Id}) into \"{target.Name}\" ({target.Id}).",
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
                Status = ReviewBatchStatus.Completed,
                CreatedAt = now,
                CompletedAt = now
            };
            await _reviewBatchRepository.CreateAsync(batch, ct);

            var proposal = new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batch.Id,
                ChangeType = ReviewChangeType.MergeArtifact,
                TargetType = ReviewTargetType.Artifact,
                TargetId = target.Id,
                ProposedValueJson = $$"""{"sourceArtifactId":"{{duplicate.Id}}"}""",
                Rationale = "GM-initiated merge of duplicate artifact.",
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
            proposal.ReviewedByUserId = actingUserId;
            await _reviewProposalRepository.UpdateAsync(proposal, ct);

            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Artifact merged. WorldId={WorldId}, Duplicate={DuplicateId}, Target={TargetId}, User={UserId}",
                worldId, duplicate.Id, target.Id, actingUserId);

            return AppResult<Guid>.Success(target.Id);
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
