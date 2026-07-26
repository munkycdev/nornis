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

    /// <summary>
    /// Pins an existing Location artifact on this source's map at the centre (0.5, 0.5),
    /// for the places extraction missed. The caller drags it into position afterwards.
    /// Source creator or GM only; 409 when the artifact is already pinned on this map.
    /// </summary>
    Task<AppResult<MapPlacemarkView>> CreatePlacemarkAsync(
        Guid sourceId, Guid worldId, Guid artifactId, Guid userId, WorldRole role, CancellationToken ct);

    /// <summary>
    /// Moves a pin to a new normalized position — the manual correction for extraction's
    /// imprecise coordinates. Source creator or GM only; x/y must be within 0..1.
    /// </summary>
    Task<AppResult<MapPlacemarkView>> MovePlacemarkAsync(
        Guid sourceId, Guid worldId, Guid placemarkId, decimal x, decimal y,
        Guid userId, WorldRole role, CancellationToken ct);

    /// <summary>
    /// Deletes a pin from the map. The pinned Location artifact is untouched — this is
    /// for maps where extraction pinned a place that isn't on them. Source creator or GM only.
    /// </summary>
    Task<AppResult> RemovePlacemarkAsync(
        Guid sourceId, Guid worldId, Guid placemarkId,
        Guid userId, WorldRole role, CancellationToken ct);
}
