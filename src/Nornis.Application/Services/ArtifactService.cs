using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class ArtifactService : IArtifactService
{
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;

    public ArtifactService(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository)
    {
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
    }

    public async Task<AppResult<IReadOnlyList<Artifact>>> ListAsync(ArtifactListQuery query, CancellationToken ct)
    {
        var allowedScopes = GetAllowedScopes(query.ActingUserRole);

        var artifacts = await _artifactRepository.ListByWorldAsync(query.WorldId, query.Type, null, ct);

        var visible = artifacts
            .Where(a => allowedScopes.Contains(a.Visibility))
            .Where(a => query.Status is null || a.Status == query.Status)
            .OrderByDescending(a => a.UpdatedAt)
            .ToList();

        return AppResult<IReadOnlyList<Artifact>>.Success(visible);
    }

    public async Task<AppResult<ArtifactDetail>> GetDetailAsync(
        Guid artifactId,
        Guid worldId,
        Guid requestingUserId,
        WorldRole role,
        CancellationToken ct)
    {
        var allowedScopes = GetAllowedScopes(role);

        var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);

        // Return not-found for missing, cross-world, or invisible artifacts so we do not
        // leak the existence of resources the caller may not see.
        if (artifact is null
            || artifact.WorldId != worldId
            || !allowedScopes.Contains(artifact.Visibility))
        {
            return AppResult<ArtifactDetail>.Fail(new AppError(404, "not_found", "Artifact not found."));
        }

        var facts = (await _factRepository.ListByArtifactAsync(artifactId, ct))
            .Where(f => allowedScopes.Contains(f.Visibility))
            .OrderBy(f => f.CreatedAt)
            .ToList();

        var relationships = (await _relationshipRepository.ListByArtifactAsync(artifactId, ct))
            .Where(r => allowedScopes.Contains(r.Visibility))
            .OrderBy(r => r.CreatedAt)
            .ToList();

        // Resolve the counterpart artifact for each relationship, keeping only those the
        // caller may see and that belong to the same world.
        var connectedArtifacts = await ResolveConnectedArtifactsAsync(
            artifactId, worldId, relationships, allowedScopes, ct);

        // Source references may cite the artifact itself, any of its facts, or any of its
        // relationships. Target ids are distinct GUIDs, so a single id-based lookup is safe.
        var targetIds = new List<Guid> { artifactId };
        targetIds.AddRange(facts.Select(f => f.Id));
        targetIds.AddRange(relationships.Select(r => r.Id));

        var sourceReferences = await _sourceReferenceRepository.ListByTargetIdsAsync(targetIds, ct);

        var detail = new ArtifactDetail(
            Artifact: artifact,
            Facts: facts,
            Relationships: relationships,
            ConnectedArtifacts: connectedArtifacts,
            SourceReferences: sourceReferences);

        return AppResult<ArtifactDetail>.Success(detail);
    }

    private async Task<IReadOnlyList<Artifact>> ResolveConnectedArtifactsAsync(
        Guid artifactId,
        Guid worldId,
        IReadOnlyList<ArtifactRelationship> relationships,
        IReadOnlyList<VisibilityScope> allowedScopes,
        CancellationToken ct)
    {
        var otherIds = relationships
            .Select(r => r.ArtifactAId == artifactId ? r.ArtifactBId : r.ArtifactAId)
            .Distinct()
            .ToList();

        var connected = new List<Artifact>();
        foreach (var otherId in otherIds)
        {
            var other = await _artifactRepository.GetByIdAsync(otherId, ct);
            if (other is not null
                && other.WorldId == worldId
                && allowedScopes.Contains(other.Visibility))
            {
                connected.Add(other);
            }
        }

        return connected;
    }

    /// <summary>
    /// Maps a world role to the visibility scopes it may read. Artifacts, facts, and
    /// relationships carry no per-record creator, so Private content is visible to any GM or
    /// Player in the world. This mirrors the retrieval scoping used by the Loremaster.
    /// </summary>
    private static IReadOnlyList<VisibilityScope> GetAllowedScopes(WorldRole role) =>
        role switch
        {
            WorldRole.GM => [VisibilityScope.PartyVisible, VisibilityScope.GMOnly, VisibilityScope.Private],
            WorldRole.Player => [VisibilityScope.PartyVisible, VisibilityScope.Private],
            WorldRole.Observer => [VisibilityScope.PartyVisible],
            _ => [VisibilityScope.PartyVisible]
        };
}
