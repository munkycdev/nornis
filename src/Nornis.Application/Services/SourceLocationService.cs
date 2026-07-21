using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// The user-authored links between a session and the Location artifacts it took place at. Every
/// other <see cref="SourceReference"/> is authored by the extraction pipeline; these are drawn by
/// a person on the session page to correct what extraction could only guess. A link is an ordinary
/// Artifact reference, so it feeds the Locations view and the Journey trail through the same
/// "visited" derivation — no separate signal, no new entity.
/// </summary>
public class SourceLocationService : ISourceLocationService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;

    public SourceLocationService(
        ISourceRepository sourceRepository,
        IArtifactRepository artifactRepository,
        ISourceReferenceRepository sourceReferenceRepository)
    {
        _sourceRepository = sourceRepository;
        _artifactRepository = artifactRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
    }

    public async Task<AppResult<IReadOnlyList<LinkedLocation>>> ListLocationsAsync(
        Guid sourceId, Guid worldId, Guid userId, WorldRole role, CancellationToken ct)
    {
        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null || source.WorldId != worldId || !CanSeeSource(source, userId, role))
        {
            return NotFound();
        }

        return AppResult<IReadOnlyList<LinkedLocation>>.Success(
            await BuildLocationsAsync(sourceId, worldId, userId, role, ct));
    }

    public async Task<AppResult<IReadOnlyList<LinkedLocation>>> LinkLocationAsync(
        Guid sourceId, Guid worldId, Guid artifactId, Guid userId, WorldRole role, CancellationToken ct)
    {
        if (await AuthorizeEditAsync(sourceId, worldId, userId, role, ct) is { } denied)
        {
            return denied;
        }

        var filter = VisibilityFilter.ForRole(role, userId);
        var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);
        if (artifact is null
            || artifact.WorldId != worldId
            || artifact.Type != ArtifactType.Location
            || artifact.Status == ArtifactStatus.Archived
            || !filter.CanSee(artifact.Visibility, artifact.CreatedByUserId))
        {
            return AppResult<IReadOnlyList<LinkedLocation>>.Fail(
                new AppError(400, "not_a_location", "That is not a location you can link to this session."));
        }

        // Idempotent: a place already tied to this session — by hand or by extraction — stays one row.
        var existing = await _sourceReferenceRepository.ListBySourceAsync(sourceId, ct);
        var alreadyLinked = existing.Any(r =>
            r.TargetType == SourceReferenceTargetType.Artifact && r.TargetId == artifactId);
        if (!alreadyLinked)
        {
            await _sourceReferenceRepository.CreateAsync(new SourceReference
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                TargetType = SourceReferenceTargetType.Artifact,
                TargetId = artifactId,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
        }

        return AppResult<IReadOnlyList<LinkedLocation>>.Success(
            await BuildLocationsAsync(sourceId, worldId, userId, role, ct));
    }

    public async Task<AppResult<IReadOnlyList<LinkedLocation>>> UnlinkLocationAsync(
        Guid sourceId, Guid worldId, Guid artifactId, Guid userId, WorldRole role, CancellationToken ct)
    {
        if (await AuthorizeEditAsync(sourceId, worldId, userId, role, ct) is { } denied)
        {
            return denied;
        }

        // Remove-any: an editor may drop any of the session's location links, extractor- or
        // user-authored alike — the chosen model carries no origin marker on the row. Absent link → no-op.
        await _sourceReferenceRepository.DeleteBySourceAndTargetAsync(
            sourceId, SourceReferenceTargetType.Artifact, artifactId, ct);

        return AppResult<IReadOnlyList<LinkedLocation>>.Success(
            await BuildLocationsAsync(sourceId, worldId, userId, role, ct));
    }

    /// <summary>
    /// The same edit gate source update enforces: never an Observer, and only the source's creator
    /// or a GM. Returns the failure to hand back, or null when the caller may edit.
    /// </summary>
    private async Task<AppResult<IReadOnlyList<LinkedLocation>>?> AuthorizeEditAsync(
        Guid sourceId, Guid worldId, Guid userId, WorldRole role, CancellationToken ct)
    {
        if (role == WorldRole.Observer)
        {
            return AppResult<IReadOnlyList<LinkedLocation>>.Fail(
                new AppError(403, "insufficient_role", "Observers cannot modify sources."));
        }

        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null || source.WorldId != worldId)
        {
            return NotFound();
        }

        if (source.CreatedByUserId != userId && role != WorldRole.GM)
        {
            return AppResult<IReadOnlyList<LinkedLocation>>.Fail(
                new AppError(403, "forbidden", "Only the source creator or a GM can change its locations."));
        }

        return null;
    }

    /// <summary>The caller-visible, non-archived Location artifacts this source references, by name.</summary>
    private async Task<IReadOnlyList<LinkedLocation>> BuildLocationsAsync(
        Guid sourceId, Guid worldId, Guid userId, WorldRole role, CancellationToken ct)
    {
        var filter = VisibilityFilter.ForRole(role, userId);
        var references = await _sourceReferenceRepository.ListBySourceAsync(sourceId, ct);
        var artifactIds = references
            .Where(r => r.TargetType == SourceReferenceTargetType.Artifact)
            .Select(r => r.TargetId)
            .Distinct();

        var locations = new List<LinkedLocation>();
        foreach (var artifactId in artifactIds)
        {
            var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);
            if (artifact is null
                || artifact.WorldId != worldId
                || artifact.Type != ArtifactType.Location
                || artifact.Status == ArtifactStatus.Archived
                || !filter.CanSee(artifact.Visibility, artifact.CreatedByUserId))
            {
                continue; // not a place, gone, archived, or hidden from this caller
            }
            locations.Add(new LinkedLocation(artifact.Id, artifact.Name, artifact.Summary));
        }

        return locations
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AppResult<IReadOnlyList<LinkedLocation>> NotFound() =>
        AppResult<IReadOnlyList<LinkedLocation>>.Fail(new AppError(404, "not_found", "Source not found."));

    // The standard source-visibility predicate, identical to MapViewService / JourneyMapService.
    private static bool CanSeeSource(Source source, Guid userId, WorldRole role) => source.Visibility switch
    {
        VisibilityScope.PartyVisible => true,
        VisibilityScope.Private => role == WorldRole.GM || source.CreatedByUserId == userId,
        VisibilityScope.GMOnly => role == WorldRole.GM,
        _ => false
    };
}
