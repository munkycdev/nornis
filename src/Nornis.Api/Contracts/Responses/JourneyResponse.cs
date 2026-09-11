namespace Nornis.Api.Contracts.Responses;

public record JourneyLocationResponse(Guid ArtifactId, string Name, decimal X, decimal Y, string? Label, string? ArtifactSlug = null);

public record JourneyHighlightResponse(Guid ArtifactId, string Name, string Type, bool FirstSeen, string? Summary, string? ArtifactSlug = null);

public record JourneyStopResponse(
    Guid SourceId,
    string Title,
    DateTimeOffset OccurredAt,
    IReadOnlyList<Guid> VisitedLocationIds,
    IReadOnlyList<JourneyHighlightResponse> Highlights,
    string? SourceSlug = null);

/// <summary>
/// The world's journey over one map: the map image (short-lived SAS url), its visible location
/// pins, and the visible dated sessions that visited them, in order. MapSourceId is the map's
/// owning source, so clients can link back to the page where pins are edited.
/// </summary>
public record JourneyResponse(
    Guid MapAttachmentId,
    Guid MapSourceId,
    string ImageUrl,
    IReadOnlyList<JourneyLocationResponse> Locations,
    IReadOnlyList<JourneyStopResponse> Stops,
    int UndatedSessionCount,
    string? MapSourceSlug = null);
