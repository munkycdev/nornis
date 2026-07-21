using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Read model for the Journey view. It crosses two axes the record already stores but never
/// joins: <em>where</em> places sit (map pins, via <see cref="IMapViewService"/>) and
/// <em>when</em> the party played — its sessions and imported notes on the calendar
/// (<see cref="Source.OccurredAt"/>), each carrying the pins it touched (<see cref="SourceReference"/>).
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
        // One load of the world's sources, reused for canvas discovery and the timeline walk.
        var allSources = await _sourceRepository.ListByWorldAsync(worldId, cancellationToken: ct);

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

        var pinnedSet = locations.Select(l => l.ArtifactId).ToHashSet();
        var filter = VisibilityFilter.ForRole(role, userId);

        // 2. The timeline is the world's play record: every caller-visible session and imported
        //    note (never a map, upload, reveal, or GM aside). Dated ones become stops in
        //    OccurredAt order; undated ones can't be placed but are counted, so their absence is
        //    visible rather than silent.
        var undatedCount = 0;
        var timeline = new List<Source>();
        foreach (var source in allSources)
        {
            if (source.Type is not (SourceType.SessionNote or SourceType.ImportedNote)
                || !CanSeeSource(source, userId, role))
            {
                continue;
            }
            if (source.OccurredAt is null)
            {
                undatedCount++;
                continue;
            }
            timeline.Add(source);
        }

        var ordered = timeline
            .OrderBy(s => s.OccurredAt!.Value)
            .ThenBy(s => s.CreatedAt)
            .ThenBy(s => s.Id)
            .ToList();

        // 3. Walk each stop's references once: the pins it touched are its visits (deduped —
        //    Property 5), and the caller-visible artifacts it introduced or advanced are its
        //    highlights. FirstSeen marks the earliest stop referencing an artifact (a location's
        //    "first visit"). A stop that touched no mapped place keeps an empty visit list.
        var earliestStopByArtifact = new Dictionary<Guid, int>();
        var visitsByStop = new List<List<Guid>>(ordered.Count);
        var artifactsByStop = new List<List<Artifact>>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var sourceRefs = await _sourceReferenceRepository.ListBySourceAsync(ordered[i].Id, ct);
            var artifactIds = sourceRefs
                .Where(r => r.TargetType == SourceReferenceTargetType.Artifact)
                .Select(r => r.TargetId)
                .Distinct() // one membership per artifact even if referenced twice (Property 5)
                .ToList();

            var visits = new List<Guid>();
            var visible = new List<Artifact>();
            foreach (var artifactId in artifactIds)
            {
                if (pinnedSet.Contains(artifactId))
                {
                    visits.Add(artifactId);
                }
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
            visitsByStop.Add(visits);
            artifactsByStop.Add(visible);
        }

        var stops = new List<JourneyStop>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var source = ordered[i];
            var highlights = artifactsByStop[i]
                .OrderBy(a => TypeRank(a.Type))
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(a => new JourneyHighlight(
                    a.Id, a.Name, a.Type.ToString(), earliestStopByArtifact[a.Id] == i, a.Summary))
                .ToList();

            stops.Add(new JourneyStop(
                source.Id,
                source.Title,
                source.OccurredAt!.Value,
                visitsByStop[i],
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
