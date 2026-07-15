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
    private readonly ISourceRepository _sourceRepository;
    private readonly ICharacterRepository _characterRepository;
    private readonly IWorldMemberRepository _worldMemberRepository;

    public ArtifactService(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        ISourceRepository sourceRepository,
        ICharacterRepository characterRepository,
        IWorldMemberRepository worldMemberRepository)
    {
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _sourceRepository = sourceRepository;
        _characterRepository = characterRepository;
        _worldMemberRepository = worldMemberRepository;
    }

    private static bool CanSeeSource(Source source, Guid userId, WorldRole role) => source.Visibility switch
    {
        VisibilityScope.PartyVisible => true,
        VisibilityScope.Private => role == WorldRole.GM || source.CreatedByUserId == userId,
        VisibilityScope.GMOnly => role == WorldRole.GM,
        _ => false
    };

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

    public async Task<AppResult<ArtifactGraph>> GetGraphAsync(Guid worldId, WorldRole role, CancellationToken ct)
    {
        var allowedScopes = GetAllowedScopes(role);

        var artifacts = (await _artifactRepository.ListByWorldAsync(worldId, null, null, ct))
            .Where(a => allowedScopes.Contains(a.Visibility))
            .ToList();

        var nodes = artifacts
            .Select(a => new ArtifactGraphNode(a.Id, a.Name, a.Type.ToString(), a.Status.ToString()))
            .ToList();

        var visibleIds = artifacts.Select(a => a.Id).ToHashSet();

        var edges = (await _relationshipRepository.ListByArtifactIdsAsync(visibleIds.ToList(), allowedScopes, ct))
            .Where(r => visibleIds.Contains(r.ArtifactAId) && visibleIds.Contains(r.ArtifactBId))
            .DistinctBy(r => r.Id)
            .Select(r => new ArtifactGraphEdge(r.Id, r.ArtifactAId, r.ArtifactBId, r.Type))
            .ToList();

        return AppResult<ArtifactGraph>.Success(new ArtifactGraph(nodes, edges));
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

        // Resolve titles for the cited sources so the UI can link back to them —
        // but only for sources the caller is allowed to see.
        var sourceTitles = new Dictionary<Guid, string>();
        foreach (var sourceId in sourceReferences.Select(r => r.SourceId).Distinct())
        {
            var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
            if (source is not null && CanSeeSource(source, requestingUserId, role))
            {
                sourceTitles[sourceId] = source.Title;
            }
        }

        var playedBy = await ResolvePlayedByAsync(artifact, ct);

        var detail = new ArtifactDetail(
            Artifact: artifact,
            Facts: facts,
            Relationships: relationships,
            ConnectedArtifacts: connectedArtifacts,
            SourceReferences: sourceReferences,
            SourceTitles: sourceTitles,
            PlayedBy: playedBy);

        return AppResult<ArtifactDetail>.Success(detail);
    }

    /// <summary>
    /// Reverse lookup from a Character artifact to the members playing it: any Character
    /// record linking to this artifact names its owner. Non-Character artifacts skip the
    /// queries entirely.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolvePlayedByAsync(Artifact artifact, CancellationToken ct)
    {
        if (artifact.Type != ArtifactType.Character)
        {
            return [];
        }

        var linkedCharacters = (await _characterRepository.ListByWorldAsync(artifact.WorldId, ct))
            .Where(c => c.ArtifactId == artifact.Id)
            .ToList();

        if (linkedCharacters.Count == 0)
        {
            return [];
        }

        var members = await _worldMemberRepository.ListByWorldAsync(artifact.WorldId, ct);

        return linkedCharacters
            .Select(c => members.FirstOrDefault(m => m.Id == c.WorldMemberId))
            .Where(m => m is not null)
            .Select(m => !string.IsNullOrWhiteSpace(m!.DisplayName)
                ? m.DisplayName!
                : $"User {m.UserId.ToString()[..8]}")
            .Distinct()
            .ToList();
    }

    public async Task<AppResult<Artifact>> RenameAsync(RenameArtifactCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<Artifact>.Fail(new AppError(403, "insufficient_role", "Only GMs can rename artifacts."));
        }

        var name = command.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return AppResult<Artifact>.Fail(new AppError(400, "validation_error", "Artifact name must not be empty or whitespace."));
        }

        if (name.Length > 200)
        {
            return AppResult<Artifact>.Fail(new AppError(400, "validation_error", "Artifact name must be between 1 and 200 characters."));
        }

        var artifact = await _artifactRepository.GetByIdAsync(command.ArtifactId, ct);
        if (artifact is null || artifact.WorldId != command.WorldId)
        {
            return AppResult<Artifact>.Fail(new AppError(404, "not_found", "Artifact not found."));
        }

        artifact.Name = name;
        artifact.UpdatedAt = DateTimeOffset.UtcNow;
        artifact = await _artifactRepository.UpdateAsync(artifact, ct);

        return AppResult<Artifact>.Success(artifact);
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
