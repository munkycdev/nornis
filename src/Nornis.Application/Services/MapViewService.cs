using Nornis.Application.Errors;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Read model for the map viewer. Pins carry no visibility of their own — they inherit
/// the referenced artifact's, so a pin only renders when the caller may see the
/// artifact it points at (and the artifact still exists and is not archived).
/// </summary>
public class MapViewService : IMapViewService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly ISourceAttachmentRepository _attachmentRepository;
    private readonly IMapPlacemarkRepository _placemarkRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IBlobStorageService _blobStorage;

    public MapViewService(
        ISourceRepository sourceRepository,
        ISourceAttachmentRepository attachmentRepository,
        IMapPlacemarkRepository placemarkRepository,
        IArtifactRepository artifactRepository,
        IBlobStorageService blobStorage)
    {
        _sourceRepository = sourceRepository;
        _attachmentRepository = attachmentRepository;
        _placemarkRepository = placemarkRepository;
        _artifactRepository = artifactRepository;
        _blobStorage = blobStorage;
    }

    public async Task<AppResult<MapView>> GetMapAsync(
        Guid sourceId, Guid worldId, Guid userId, WorldRole role, CancellationToken ct)
    {
        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null || source.WorldId != worldId || !CanSeeSource(source, userId, role))
        {
            return AppResult<MapView>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        var attachment = (await _attachmentRepository.ListBySourceAsync(sourceId, ct))
            .FirstOrDefault(a => a.Kind == SourceAttachmentKind.MapImage && a.Status == SourceAttachmentStatus.Stored);
        if (attachment is null)
        {
            return AppResult<MapView>.Fail(new AppError(404, "no_map", "This source has no map image."));
        }

        var imageUrl = await _blobStorage.GenerateDownloadSasUrlAsync(attachment.BlobPath, ct);

        var filter = VisibilityFilter.ForRole(role, userId);
        var placemarks = await _placemarkRepository.ListByAttachmentAsync(attachment.Id, ct);

        var views = new List<MapPlacemarkView>(placemarks.Count);
        foreach (var placemark in placemarks)
        {
            var artifact = await _artifactRepository.GetByIdAsync(placemark.ArtifactId, ct);
            if (artifact is null
                || artifact.WorldId != worldId
                || artifact.Status == ArtifactStatus.Archived
                || !filter.CanSee(artifact.Visibility, artifact.CreatedByUserId))
            {
                continue; // dangling or invisible — the pin silently drops for this caller
            }

            views.Add(new MapPlacemarkView(
                placemark.Id, artifact.Id, artifact.Name,
                placemark.X, placemark.Y, placemark.Label, placemark.Confidence));
        }

        return AppResult<MapView>.Success(new MapView(attachment, imageUrl, views));
    }

    public async Task<AppResult<MapPlacemarkView>> MovePlacemarkAsync(
        Guid sourceId, Guid worldId, Guid placemarkId, decimal x, decimal y,
        Guid userId, WorldRole role, CancellationToken ct)
    {
        if (role == WorldRole.Observer)
        {
            return AppResult<MapPlacemarkView>.Fail(new AppError(403, "insufficient_role",
                "Observers cannot move map pins."));
        }

        if (x is < 0m or > 1m || y is < 0m or > 1m)
        {
            return AppResult<MapPlacemarkView>.Fail(new AppError(400, "invalid_position",
                "Pin positions are normalized: x and y must be between 0 and 1."));
        }

        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null || source.WorldId != worldId || !CanSeeSource(source, userId, role))
        {
            return AppResult<MapPlacemarkView>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        if (source.CreatedByUserId != userId && role != WorldRole.GM)
        {
            return AppResult<MapPlacemarkView>.Fail(new AppError(403, "forbidden",
                "Only the source creator or a GM can move this map's pins."));
        }

        var attachment = (await _attachmentRepository.ListBySourceAsync(sourceId, ct))
            .FirstOrDefault(a => a.Kind == SourceAttachmentKind.MapImage && a.Status == SourceAttachmentStatus.Stored);
        if (attachment is null)
        {
            return AppResult<MapPlacemarkView>.Fail(new AppError(404, "no_map", "This source has no map image."));
        }

        var placemark = (await _placemarkRepository.ListByAttachmentAsync(attachment.Id, ct))
            .FirstOrDefault(p => p.Id == placemarkId);
        if (placemark is null)
        {
            return AppResult<MapPlacemarkView>.Fail(new AppError(404, "not_found", "Pin not found on this map."));
        }

        // A pin whose artifact is gone or hidden from the mover never rendered for them —
        // moving it would be acting on something unseen.
        var artifact = await _artifactRepository.GetByIdAsync(placemark.ArtifactId, ct);
        var filter = VisibilityFilter.ForRole(role, userId);
        if (artifact is null
            || artifact.WorldId != worldId
            || artifact.Status == ArtifactStatus.Archived
            || !filter.CanSee(artifact.Visibility, artifact.CreatedByUserId))
        {
            return AppResult<MapPlacemarkView>.Fail(new AppError(404, "not_found", "Pin not found on this map."));
        }

        placemark.X = x;
        placemark.Y = y;
        placemark.UpdatedAt = DateTimeOffset.UtcNow;
        var updated = await _placemarkRepository.UpdateAsync(placemark, ct);

        return AppResult<MapPlacemarkView>.Success(new MapPlacemarkView(
            updated.Id, artifact.Id, artifact.Name, updated.X, updated.Y, updated.Label, updated.Confidence));
    }

    private static bool CanSeeSource(Source source, Guid userId, WorldRole role) => source.Visibility switch
    {
        VisibilityScope.PartyVisible => true,
        VisibilityScope.Private => role == WorldRole.GM || source.CreatedByUserId == userId,
        VisibilityScope.GMOnly => role == WorldRole.GM,
        _ => false
    };
}
