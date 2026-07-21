using Nornis.Application.Errors;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>One pinned Location on the journey's map, at a normalized 0..1 position.</summary>
public sealed record JourneyLocation(Guid ArtifactId, string Name, decimal X, decimal Y, string? Label);

/// <summary>
/// An artifact a session introduced or advanced, surfaced beside the map when its stop is
/// selected. <paramref name="FirstSeen"/> is true when this stop is the earliest visible
/// session to reference the artifact (a location's "first visit"). <paramref name="Summary"/>
/// feeds the same hover tooltip the rest of the site uses ("which one is this?").
/// </summary>
public sealed record JourneyHighlight(Guid ArtifactId, string Name, string Type, bool FirstSeen, string? Summary);

/// <summary>
/// One dated session or imported note on the timeline: the pinned locations it visited (a
/// cluster — no order is asserted among them, and it may be empty when the source touched no
/// mapped place) and the artifacts to show when it is the selected stop.
/// </summary>
public sealed record JourneyStop(
    Guid SourceId,
    string Title,
    DateTimeOffset OccurredAt,
    IReadOnlyList<Guid> VisitedLocationIds,
    IReadOnlyList<JourneyHighlight> Highlights);

/// <summary>
/// The world's journey over one map: the map image, its caller-visible location pins, and the
/// caller-visible dated sessions and imported notes, in chronological order. Every frame the
/// client renders is a projection of a stop index (or a selected pin) over this data.
/// </summary>
public sealed record JourneyMap(
    Guid MapAttachmentId,
    string ImageUrl,
    IReadOnlyList<JourneyLocation> Locations,
    IReadOnlyList<JourneyStop> Stops,
    int UndatedSessionCount);

public interface IJourneyMapService
{
    /// <summary>
    /// The journey over one map. With no <paramref name="mapSourceId"/> the world's map with the
    /// most caller-visible pins is auto-picked (ties broken by recency). 404 <c>no_map</c> when
    /// the world has no caller-visible map with pins, or the requested map is invisible/absent.
    /// </summary>
    Task<AppResult<JourneyMap>> GetJourneyAsync(
        Guid worldId, Guid? mapSourceId, Guid userId, WorldRole role, CancellationToken ct);
}
