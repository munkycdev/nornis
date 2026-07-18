namespace Nornis.Api.Contracts.Responses;

public record MapPlacemarkResponse(
    Guid Id,
    Guid ArtifactId,
    string ArtifactName,
    decimal X,
    decimal Y,
    string? Label,
    decimal? Confidence);

/// <summary>The source's map image (short-lived SAS url) plus its visible pins.</summary>
public record MapViewResponse(
    SourceAttachmentResponse Attachment,
    string ImageUrl,
    IReadOnlyList<MapPlacemarkResponse> Placemarks);
