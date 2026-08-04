using Microsoft.Extensions.Logging;
using Nornis.Application.Application;
using Nornis.Application.Common;
using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class ArtifactMergeService : IArtifactMergeService
{
    private readonly IArtifactRepository _artifactRepository;
    private readonly SyntheticBatchWriter _batchWriter;
    private readonly ILogger<ArtifactMergeService> _logger;

    public ArtifactMergeService(
        IArtifactRepository artifactRepository,
        SyntheticBatchWriter batchWriter,
        ILogger<ArtifactMergeService> logger)
    {
        _artifactRepository = artifactRepository;
        _batchWriter = batchWriter;
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

        // Provenance: the merge is an ordinary accepted MergeArtifact proposal, tied to
        // a synthetic source recording who folded what into what.
        var written = await _batchWriter.WriteAcceptedAsync(
            new SyntheticSourceSpec
            {
                WorldId = worldId,
                ActingUserId = actingUserId,
                Title = $"Artifact merge — {duplicate.Name} → {target.Name} — {DateTimeOffset.UtcNow:yyyy-MM-dd}".Truncate(200),
                Body = $"GM merged duplicate artifact \"{duplicate.Name}\" ({duplicate.Id}) into \"{target.Name}\" ({target.Id})."
            },
            ReviewBatchKinds.ArtifactMerge,
            [
                new SyntheticProposalSpec
                {
                    ChangeType = ReviewChangeType.MergeArtifact,
                    TargetType = ReviewTargetType.Artifact,
                    TargetId = target.Id,
                    ProposedValueJson = $$"""{"sourceArtifactId":"{{duplicate.Id}}"}""",
                    Rationale = "GM-initiated merge of duplicate artifact."
                }
            ],
            ct);

        if (!written.IsSuccess)
        {
            return AppResult<Guid>.Fail(written.Error!);
        }

        _logger.LogInformation(
            "Artifact merged. WorldId={WorldId}, Duplicate={DuplicateId}, Target={TargetId}, User={UserId}",
            worldId, duplicate.Id, target.Id, actingUserId);

        return AppResult<Guid>.Success(target.Id);
    }
}
