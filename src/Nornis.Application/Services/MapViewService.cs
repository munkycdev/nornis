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

    private static bool CanSeeSource(Source source, Guid userId, WorldRole role) => source.Visibility switch
    {
        VisibilityScope.PartyVisible => true,
        VisibilityScope.Private => role == WorldRole.GM || source.CreatedByUserId == userId,
        VisibilityScope.GMOnly => role == WorldRole.GM,
        _ => false
    };
}
