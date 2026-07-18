using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public sealed record MapPlacemarkView(
    Guid Id, Guid ArtifactId, string ArtifactName, decimal X, decimal Y, string? Label, decimal? Confidence);

public sealed record MapView(
    SourceAttachment Attachment, string ImageUrl, IReadOnlyList<MapPlacemarkView> Placemarks);

public interface IMapViewService
{
    /// <summary>
    /// The source's map image (fresh download SAS) plus its pins, filtered to artifacts
    /// the caller may see. 404 when the source is invisible or has no stored map.
    /// </summary>
    Task<AppResult<MapView>> GetMapAsync(Guid sourceId, Guid worldId, Guid userId, WorldRole role, CancellationToken ct);
}
