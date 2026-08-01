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
        if (source is null || source.WorldId != worldId || !SourceVisibilityRule.Compile(userId, role)(source))
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

        // One batch fetch for every pinned artifact, not a roundtrip per pin.
        var artifactsById = (await _artifactRepository.ListByIdsAsync(
                placemarks.Select(p => p.ArtifactId).Distinct().ToList(), ct))
            .ToDictionary(a => a.Id);

        var views = new List<MapPlacemarkView>(placemarks.Count);
        foreach (var placemark in placemarks)
        {
            if (!artifactsById.TryGetValue(placemark.ArtifactId, out var artifact)
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

    /// <summary>Where a manually added pin lands: the centre of the map, to be dragged into place.</summary>
    private const decimal CentreX = 0.5m;
    private const decimal CentreY = 0.5m;

    public async Task<AppResult<MapPlacemarkView>> CreatePlacemarkAsync(
        Guid sourceId, Guid worldId, Guid artifactId, Guid userId, WorldRole role, CancellationToken ct)
    {
        if (role == WorldRole.Observer)
        {
            return AppResult<MapPlacemarkView>.Fail(new AppError(403, "insufficient_role",
                "Observers cannot add map pins."));
        }

        var (mapError, attachment) = await FindEditableMapAsync(sourceId, worldId, userId, role, ct);
        if (mapError is not null)
        {
            return AppResult<MapPlacemarkView>.Fail(mapError);
        }

        // A location the caller can't see may as well not exist — same 404 as a bad id,
        // so the picker can never be used to probe for hidden artifacts.
        var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);
        var filter = VisibilityFilter.ForRole(role, userId);
        if (artifact is null
            || artifact.WorldId != worldId
            || artifact.Status == ArtifactStatus.Archived
            || !filter.CanSee(artifact.Visibility, artifact.CreatedByUserId))
        {
            return AppResult<MapPlacemarkView>.Fail(new AppError(404, "not_found", "Location not found."));
        }

        if (artifact.Type != ArtifactType.Location)
        {
            return AppResult<MapPlacemarkView>.Fail(new AppError(400, "invalid_artifact_type",
                "Only Location artifacts can be pinned on a map."));
        }

        // One pin per (map, artifact) — the DB enforces it with a unique index too.
        var existing = await _placemarkRepository.GetByAttachmentAndArtifactAsync(attachment!.Id, artifact.Id, ct);
        if (existing is not null)
        {
            return AppResult<MapPlacemarkView>.Fail(new AppError(409, "already_pinned",
                $"\"{artifact.Name}\" is already pinned on this map."));
        }

        var now = DateTimeOffset.UtcNow;
        var created = await _placemarkRepository.CreateAsync(new MapPlacemark
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceAttachmentId = attachment.Id,
            ArtifactId = artifact.Id,
            X = CentreX,
            Y = CentreY,
            Label = artifact.Name,
            // A human placed this pin, so there is no model confidence to record.
            Confidence = null,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);

        return AppResult<MapPlacemarkView>.Success(new MapPlacemarkView(
            created.Id, artifact.Id, artifact.Name, created.X, created.Y, created.Label, created.Confidence));
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

        var (error, placemark, artifact) = await FindEditablePlacemarkAsync(
            sourceId, worldId, placemarkId, userId, role, ct);
        if (error is not null)
        {
            return AppResult<MapPlacemarkView>.Fail(error);
        }

        placemark!.X = x;
        placemark.Y = y;
        placemark.UpdatedAt = DateTimeOffset.UtcNow;
        var updated = await _placemarkRepository.UpdateAsync(placemark, ct);

        return AppResult<MapPlacemarkView>.Success(new MapPlacemarkView(
            updated.Id, artifact!.Id, artifact.Name, updated.X, updated.Y, updated.Label, updated.Confidence));
    }

    public async Task<AppResult> RemovePlacemarkAsync(
        Guid sourceId, Guid worldId, Guid placemarkId, Guid userId, WorldRole role, CancellationToken ct)
    {
        if (role == WorldRole.Observer)
        {
            return AppResult.Fail(new AppError(403, "insufficient_role",
                "Observers cannot remove map pins."));
        }

        var (error, placemark, _) = await FindEditablePlacemarkAsync(
            sourceId, worldId, placemarkId, userId, role, ct);
        if (error is not null)
        {
            return AppResult.Fail(error);
        }

        await _placemarkRepository.DeleteAsync(placemark!.Id, ct);
        return AppResult.Success();
    }

    /// <summary>
    /// The shared move/remove gauntlet: visible source, creator-or-GM, stored map image,
    /// pin on that map, and an artifact the caller may see. A pin whose artifact is gone
    /// or hidden never rendered for this caller — editing it would be acting on something
    /// unseen, so those 404 like a missing pin.
    /// </summary>
    private async Task<(AppError? Error, MapPlacemark? Placemark, Artifact? Artifact)> FindEditablePlacemarkAsync(
        Guid sourceId, Guid worldId, Guid placemarkId, Guid userId, WorldRole role, CancellationToken ct)
    {
        var (mapError, attachment) = await FindEditableMapAsync(sourceId, worldId, userId, role, ct);
        if (mapError is not null)
        {
            return (mapError, null, null);
        }

        var placemark = (await _placemarkRepository.ListByAttachmentAsync(attachment!.Id, ct))
            .FirstOrDefault(p => p.Id == placemarkId);
        if (placemark is null)
        {
            return (new AppError(404, "not_found", "Pin not found on this map."), null, null);
        }

        var artifact = await _artifactRepository.GetByIdAsync(placemark.ArtifactId, ct);
        var filter = VisibilityFilter.ForRole(role, userId);
        if (artifact is null
            || artifact.WorldId != worldId
            || artifact.Status == ArtifactStatus.Archived
            || !filter.CanSee(artifact.Visibility, artifact.CreatedByUserId))
        {
            return (new AppError(404, "not_found", "Pin not found on this map."), null, null);
        }

        return (null, placemark, artifact);
    }

    /// <summary>
    /// The half of the gauntlet every pin edit shares, and all a create needs: a source
    /// this caller can see, that they created or GM, carrying a stored map image.
    /// </summary>
    private async Task<(AppError? Error, SourceAttachment? Attachment)> FindEditableMapAsync(
        Guid sourceId, Guid worldId, Guid userId, WorldRole role, CancellationToken ct)
    {
        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null || source.WorldId != worldId || !SourceVisibilityRule.Compile(userId, role)(source))
        {
            return (new AppError(404, "not_found", "Source not found."), null);
        }

        if (source.CreatedByUserId != userId && role != WorldRole.GM)
        {
            return (new AppError(403, "forbidden",
                "Only the source creator or a GM can edit this map's pins."), null);
        }

        var attachment = (await _attachmentRepository.ListBySourceAsync(sourceId, ct))
            .FirstOrDefault(a => a.Kind == SourceAttachmentKind.MapImage && a.Status == SourceAttachmentStatus.Stored);
        if (attachment is null)
        {
            return (new AppError(404, "no_map", "This source has no map image."), null);
        }

        return (null, attachment);
    }
}
