using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Read model for the Journey view. It crosses two axes the record already stores but never
/// joins: <em>where</em> places sit (map pins, via <see cref="IMapViewService"/>) and
/// <em>when</em> sessions touched them (<see cref="SourceReference"/> → <see cref="Source.OccurredAt"/>).
/// Pure assembly — it writes nothing and owns no visibility rules of its own: the pin layer is
/// <see cref="IMapViewService"/> verbatim, and stops/highlights reuse <see cref="VisibilityFilter"/>
/// and the same source-visibility predicate the map viewer uses.
/// </summary>
public class JourneyMapService : IJourneyMapService
{
    private readonly IMapViewService _mapViewService;
    private readonly ISourceRepository _sourceRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IArtifactRepository _artifactRepository;

    public JourneyMapService(
        IMapViewService mapViewService,
        ISourceRepository sourceRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IArtifactRepository artifactRepository)
    {
        _mapViewService = mapViewService;
        _sourceRepository = sourceRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _artifactRepository = artifactRepository;
    }

    public async Task<AppResult<JourneyMap>> GetJourneyAsync(
        Guid worldId, Guid? mapSourceId, Guid userId, WorldRole role, CancellationToken ct)
    {
        // One load of the world's sources, reused for canvas discovery and stop lookup.
        var allSources = await _sourceRepository.ListByWorldAsync(worldId, cancellationToken: ct);
        var sourceById = allSources.ToDictionary(s => s.Id);

        // 1. Resolve the canvas — the map image + its caller-visible pins — via MapViewService.
        MapView canvas;
        if (mapSourceId is { } requested)
        {
            var mapResult = await _mapViewService.GetMapAsync(requested, worldId, userId, role, ct);
            if (!mapResult.IsSuccess)
            {
                return AppResult<JourneyMap>.Fail(mapResult.Error!); // 404 not_found / no_map, verbatim
            }
            canvas = mapResult.Value!;
        }
        else
        {
            var candidates = allSources
                .Where(s => s.Type == SourceType.Map && CanSeeSource(s, userId, role));

            (Source Source, MapView View)? best = null;
            foreach (var candidate in candidates)
            {
                var mr = await _mapViewService.GetMapAsync(candidate.Id, worldId, userId, role, ct);
                if (!mr.IsSuccess || mr.Value!.Placemarks.Count == 0)
                {
                    continue; // draft/invisible/no-pins maps can't anchor a journey
                }
                if (best is null || IsRicher(candidate, mr.Value!, best.Value.Source, best.Value.View))
                {
                    best = (candidate, mr.Value!);
                }
            }

            if (best is null)
            {
                return AppResult<JourneyMap>.Fail(new AppError(
                    404, "no_map", "This world has no map with visible pins to chart a journey on yet."));
            }
            canvas = best.Value.View;
        }

        var locations = canvas.Placemarks
            .Select(p => new JourneyLocation(p.ArtifactId, p.ArtifactName, p.X, p.Y, p.Label))
            .ToList();

        if (locations.Count == 0)
        {
            // An explicitly-requested map may have no visible pins — return it statically.
            return AppResult<JourneyMap>.Success(
                new JourneyMap(canvas.Attachment.Id, canvas.ImageUrl, locations, [], 0));
        }

        var pinnedIds = locations.Select(l => l.ArtifactId).ToList();
        var pinnedSet = pinnedIds.ToHashSet();
        var filter = VisibilityFilter.ForRole(role, userId);

        // 2. Session walk: which sessions referenced each pinned location (the visit signal).
        var refs = await _sourceReferenceRepository.ListByTargetIdsAsync(pinnedIds, ct);
        var visitsBySession = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var reference in refs)
        {
            if (reference.TargetType != SourceReferenceTargetType.Artifact
                || !pinnedSet.Contains(reference.TargetId))
            {
                continue;
            }
            if (!visitsBySession.TryGetValue(reference.SourceId, out var set))
            {
                visitsBySession[reference.SourceId] = set = [];
            }
            set.Add(reference.TargetId); // HashSet dedups repeat references (Property 5)
        }

        // 3. Keep visible, dated sessions; count the visible-but-undated ones (they can't be placed).
        var undatedCount = 0;
        var dated = new List<(Source Source, HashSet<Guid> Visits)>();
        foreach (var (sessionId, visits) in visitsBySession)
        {
            if (!sourceById.TryGetValue(sessionId, out var session) || !CanSeeSource(session, userId, role))
            {
                continue; // outside the world, or hidden from this caller
            }
            if (session.OccurredAt is null)
            {
                undatedCount++;
                continue;
            }
            dated.Add((session, visits));
        }

        var ordered = dated
            .OrderBy(x => x.Source.OccurredAt!.Value)
            .ThenBy(x => x.Source.CreatedAt)
            .ThenBy(x => x.Source.Id)
            .ToList();

        // 4. Highlights per stop, with FirstSeen = earliest visible stop referencing the artifact.
        var earliestStopByArtifact = new Dictionary<Guid, int>();
        var artifactsByStop = new List<List<Artifact>>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var sessionRefs = await _sourceReferenceRepository.ListBySourceAsync(ordered[i].Source.Id, ct);
            var artifactIds = sessionRefs
                .Where(r => r.TargetType == SourceReferenceTargetType.Artifact)
                .Select(r => r.TargetId)
                .Distinct();

            var visible = new List<Artifact>();
            foreach (var artifactId in artifactIds)
            {
                var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);
                if (artifact is null
                    || artifact.WorldId != worldId
                    || artifact.Status == ArtifactStatus.Archived
                    || !filter.CanSee(artifact.Visibility, artifact.CreatedByUserId))
                {
                    continue;
                }
                visible.Add(artifact);
                earliestStopByArtifact.TryAdd(artifact.Id, i); // ordered walk → first add is earliest
            }
            artifactsByStop.Add(visible);
        }

        var stops = new List<JourneyStop>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var (session, visits) = ordered[i];
            var highlights = artifactsByStop[i]
                .OrderBy(a => TypeRank(a.Type))
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(a => new JourneyHighlight(a.Id, a.Name, a.Type.ToString(), earliestStopByArtifact[a.Id] == i))
                .ToList();

            stops.Add(new JourneyStop(
                session.Id,
                session.Title,
                session.OccurredAt!.Value,
                visits.ToList(),
                highlights));
        }

        return AppResult<JourneyMap>.Success(
            new JourneyMap(canvas.Attachment.Id, canvas.ImageUrl, locations, stops, undatedCount));
    }

    /// <summary>Deterministic canvas order: most visible pins, then most recent, then id.</summary>
    private static bool IsRicher(Source candidate, MapView candidateView, Source current, MapView currentView)
    {
        if (candidateView.Placemarks.Count != currentView.Placemarks.Count)
        {
            return candidateView.Placemarks.Count > currentView.Placemarks.Count;
        }
        var candidateRecency = candidate.OccurredAt ?? candidate.CreatedAt;
        var currentRecency = current.OccurredAt ?? current.CreatedAt;
        if (candidateRecency != currentRecency)
        {
            return candidateRecency > currentRecency;
        }
        return candidate.Id.CompareTo(current.Id) < 0;
    }

    // Ledger reading order: places first, then what happened, then the things and people.
    private static int TypeRank(ArtifactType type) => type switch
    {
        ArtifactType.Location => 0,
        ArtifactType.Event => 1,
        ArtifactType.Item => 2,
        ArtifactType.Character => 3,
        ArtifactType.Faction => 4,
        ArtifactType.Storyline => 5,
        ArtifactType.Concept => 6,
        _ => 7
    };

    // The standard source-visibility predicate, identical to MapViewService's gate.
    private static bool CanSeeSource(Source source, Guid userId, WorldRole role) => source.Visibility switch
    {
        VisibilityScope.PartyVisible => true,
        VisibilityScope.Private => role == WorldRole.GM || source.CreatedByUserId == userId,
        VisibilityScope.GMOnly => role == WorldRole.GM,
        _ => false
    };
}
