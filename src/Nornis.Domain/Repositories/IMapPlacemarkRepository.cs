using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IMapPlacemarkRepository
{
    Task<MapPlacemark> CreateAsync(MapPlacemark placemark, CancellationToken cancellationToken = default);

    Task<MapPlacemark?> GetByAttachmentAndArtifactAsync(Guid sourceAttachmentId, Guid artifactId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MapPlacemark>> ListByAttachmentAsync(Guid sourceAttachmentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MapPlacemark>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default);

    Task<MapPlacemark> UpdateAsync(MapPlacemark placemark, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid placemarkId, CancellationToken cancellationToken = default);

    /// <summary>Removes all pins referencing an artifact — required wherever artifacts
    /// are hard-deleted, since ArtifactId is a loose reference.</summary>
    Task DeleteByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>Removes all pins minted from a source's map attachments (join via
    /// SourceAttachments). Used by the reprocess cascade.</summary>
    Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);
}
