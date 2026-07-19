namespace Nornis.Api.Contracts.Responses;

public record JourneyLocationResponse(Guid ArtifactId, string Name, decimal X, decimal Y, string? Label);

public record JourneyHighlightResponse(Guid ArtifactId, string Name, string Type, bool FirstSeen);

public record JourneyStopResponse(
    Guid SourceId,
    string Title,
    DateTimeOffset OccurredAt,
    IReadOnlyList<Guid> VisitedLocationIds,
    IReadOnlyList<JourneyHighlightResponse> Highlights);

/// <summary>
/// The world's journey over one map: the map image (short-lived SAS url), its visible location
/// pins, and the visible dated sessions that visited them, in order.
/// </summary>
public record JourneyResponse(
    Guid MapAttachmentId,
    string ImageUrl,
    IReadOnlyList<JourneyLocationResponse> Locations,
    IReadOnlyList<JourneyStopResponse> Stops,
    int UndatedSessionCount);
