using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
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
        var filter = VisibilityFilter.ForRole(query.ActingUserRole, query.ActingUserId);

        var artifacts = await _artifactRepository.ListByWorldAsync(query.WorldId, query.Type, null, ct);

        var visible = artifacts
            .Where(a => filter.CanSee(a.Visibility, a.CreatedByUserId))
            .Where(a => query.Status is null || a.Status == query.Status)
            .OrderByDescending(a => a.UpdatedAt)
            .ToList();

        return AppResult<IReadOnlyList<Artifact>>.Success(visible);
    }

    public async Task<AppResult<ArtifactGraph>> GetGraphAsync(Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct)
    {
        var filter = VisibilityFilter.ForRole(role, requestingUserId);

        var artifacts = (await _artifactRepository.ListByWorldAsync(worldId, null, null, ct))
            .Where(a => filter.CanSee(a.Visibility, a.CreatedByUserId))
            .ToList();

        var nodes = artifacts
            .Select(a => new ArtifactGraphNode(a.Id, a.Name, a.Type.ToString(), a.Status.ToString()))
            .ToList();

        var visibleIds = artifacts.Select(a => a.Id).ToHashSet();

        var edges = (await _relationshipRepository.ListByArtifactIdsAsync(visibleIds.ToList(), filter, ct))
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
        var filter = VisibilityFilter.ForRole(role, requestingUserId);

        var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);

        // Return not-found for missing, cross-world, or invisible artifacts so we do not
        // leak the existence of resources the caller may not see.
        if (artifact is null
            || artifact.WorldId != worldId
            || !filter.CanSee(artifact.Visibility, artifact.CreatedByUserId))
        {
            return AppResult<ArtifactDetail>.Fail(new AppError(404, "not_found", "Artifact not found."));
        }

        var facts = (await _factRepository.ListByArtifactAsync(artifactId, ct))
            .Where(f => filter.CanSee(f.Visibility, f.CreatedByUserId))
            .OrderBy(f => f.CreatedAt)
            .ToList();

        var relationships = (await _relationshipRepository.ListByArtifactAsync(artifactId, ct))
            .Where(r => filter.CanSee(r.Visibility, r.CreatedByUserId))
            .OrderBy(r => r.CreatedAt)
            .ToList();

        // Resolve the counterpart artifact for each relationship, keeping only those the
        // caller may see and that belong to the same world.
        var connectedArtifacts = await ResolveConnectedArtifactsAsync(
            artifactId, worldId, relationships, filter, ct);

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
    /// Reserved relationship type expressing storyline hierarchy: ArtifactA (child) is
    /// part of ArtifactB (parent). The timeline renders these as fork/merge connectors.
    /// </summary>
    public const string PartOfRelationshipType = "PartOf";

    public async Task<AppResult> SetStorylineParentAsync(SetStorylineParentCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult.Fail(new AppError(403, "insufficient_role", "Only GMs can restructure storylines."));
        }

        var child = await _artifactRepository.GetByIdAsync(command.ArtifactId, ct);
        if (child is null || child.WorldId != command.WorldId || child.Type != ArtifactType.Storyline)
        {
            return AppResult.Fail(new AppError(404, "not_found", "Storyline not found."));
        }

        // The child's current PartOf link, if any. A storyline holds at most one.
        var childRelationships = await _relationshipRepository.ListByArtifactAsync(child.Id, ct);
        var existing = childRelationships.FirstOrDefault(r =>
            r.Type == PartOfRelationshipType && r.ArtifactAId == child.Id);

        if (command.ParentArtifactId is not { } parentId)
        {
            if (existing is not null)
            {
                await _relationshipRepository.DeleteAsync(existing.Id, ct);
            }
            return AppResult.Success();
        }

        if (parentId == child.Id)
        {
            return AppResult.Fail(new AppError(400, "invalid_parent", "A storyline cannot be part of itself."));
        }

        var parent = await _artifactRepository.GetByIdAsync(parentId, ct);
        if (parent is null || parent.WorldId != command.WorldId || parent.Type != ArtifactType.Storyline)
        {
            return AppResult.Fail(new AppError(400, "invalid_parent", "The parent must be a storyline in this world."));
        }

        // Cycle guard: walk up from the intended parent; hitting the child means the
        // child is already an ancestor of the parent.
        var storylineIds = (await _artifactRepository.ListByWorldAsync(command.WorldId, ArtifactType.Storyline, null, ct))
            .Select(a => a.Id)
            .ToList();
        var partOfEdges = (await _relationshipRepository.ListByArtifactIdsAsync(
                storylineIds,
                VisibilityFilter.All, ct))
            .Where(r => r.Type == PartOfRelationshipType)
            .ToDictionary(r => r.ArtifactAId, r => r.ArtifactBId);

        var cursor = (Guid?)parentId;
        var hops = 0;
        while (cursor is { } current && hops++ < 100)
        {
            if (current == child.Id)
            {
                return AppResult.Fail(new AppError(409, "cycle",
                    $"\"{parent.Name}\" already sits beneath \"{child.Name}\" — that link would create a cycle."));
            }
            cursor = partOfEdges.TryGetValue(current, out var next) ? next : null;
        }

        // A structural link is only as visible as its least visible endpoint.
        var visibility = new[] { child.Visibility, parent.Visibility }.Contains(VisibilityScope.GMOnly)
            ? VisibilityScope.GMOnly
            : new[] { child.Visibility, parent.Visibility }.Contains(VisibilityScope.Private)
                ? VisibilityScope.Private
                : VisibilityScope.PartyVisible;

        // Owner for Private links: inherit the private endpoint's owner rather than the
        // acting GM — otherwise the player who owns the private storyline could never see
        // their own timeline nesting. Non-Private links carry the acting user.
        var createdByUserId = visibility == VisibilityScope.Private
            ? (child.Visibility == VisibilityScope.Private ? child.CreatedByUserId : parent.CreatedByUserId)
            : command.ActingUserId;

        if (existing is not null)
        {
            existing.ArtifactBId = parentId;
            existing.Visibility = visibility;
            existing.CreatedByUserId ??= createdByUserId;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await _relationshipRepository.UpdateAsync(existing, ct);
        }
        else
        {
            await _relationshipRepository.CreateAsync(new ArtifactRelationship
            {
                Id = Guid.NewGuid(),
                WorldId = command.WorldId,
                ArtifactAId = child.Id,
                ArtifactBId = parentId,
                Type = PartOfRelationshipType,
                TruthState = TruthState.Confirmed,
                Visibility = visibility,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct);
        }

        return AppResult.Success();
    }

    public async Task<AppResult<Artifact>> SetStatusAsync(SetArtifactStatusCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<Artifact>.Fail(new AppError(403, "insufficient_role", "Only GMs can change artifact status."));
        }

        var artifact = await _artifactRepository.GetByIdAsync(command.ArtifactId, ct);
        if (artifact is null || artifact.WorldId != command.WorldId)
        {
            return AppResult<Artifact>.Fail(new AppError(404, "not_found", "Artifact not found."));
        }

        artifact.Status = command.Status;
        artifact.UpdatedAt = DateTimeOffset.UtcNow;
        artifact = await _artifactRepository.UpdateAsync(artifact, ct);

        return AppResult<Artifact>.Success(artifact);
    }

    public async Task<AppResult<StorylineTimeline>> GetStorylineTimelineAsync(
        Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct)
    {
        var reader = new StorylineDevelopmentReader(
            _artifactRepository, _factRepository, _relationshipRepository,
            _sourceReferenceRepository, _sourceRepository);
        var data = await reader.ReadAsync(worldId, requestingUserId, role, ct);

        var storylines = data.Storylines;
        if (storylines.Count == 0)
        {
            return AppResult<StorylineTimeline>.Success(new StorylineTimeline([], [], []));
        }

        var storylineIdSet = storylines.Select(s => s.Id).ToHashSet();
        var parentByChild = data.ParentByChild;
        var sources = data.Sources;
        var developments = data.Developments;

        var links = data.Relationships
            .Where(r => r.Type != PartOfRelationshipType
                && storylineIdSet.Contains(r.ArtifactAId) && storylineIdSet.Contains(r.ArtifactBId))
            .Select(r => new TimelineLink(r.ArtifactAId, r.ArtifactBId, r.Type))
            .ToList();

        var lanes = storylines
            .Select(s =>
            {
                var points = developments
                    .Where(kv => kv.Key.StorylineId == s.Id)
                    .Select(kv => new TimelinePoint(
                        kv.Key.SourceId,
                        sources[kv.Key.SourceId].OccurredAt!.Value,
                        kv.Value))
                    .OrderBy(p => p.OccurredAt)
                    .ToList();

                // The lane's campaign is whichever campaign most of its sessions declare.
                var campaignName = points
                    .Select(p => sources[p.SourceId].Campaign?.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .GroupBy(n => n)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key;

                return new TimelineLane(
                    s.Id,
                    s.Name,
                    s.Status.ToString(),
                    points,
                    parentByChild.TryGetValue(s.Id, out var parentId) ? parentId : null,
                    campaignName);
            })
            .OrderBy(l => l.Points.Count == 0)
            .ThenBy(l => l.Points.FirstOrDefault()?.OccurredAt ?? DateTimeOffset.MaxValue)
            .ToList();

        var sessions = developments.Keys
            .GroupBy(k => k.SourceId)
            .Select(g => new TimelineSession(
                g.Key,
                sources[g.Key].Title,
                sources[g.Key].OccurredAt!.Value,
                g.Select(k => k.StorylineId).Distinct().Count()))
            .OrderBy(s => s.OccurredAt)
            .ToList();

        return AppResult<StorylineTimeline>.Success(new StorylineTimeline(sessions, lanes, links));
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
        VisibilityFilter filter,
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
                && filter.CanSee(other.Visibility, other.CreatedByUserId))
            {
                connected.Add(other);
            }
        }

        return connected;
    }
}
