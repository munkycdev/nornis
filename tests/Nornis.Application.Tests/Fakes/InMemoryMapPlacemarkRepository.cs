using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryMapPlacemarkRepository : IMapPlacemarkRepository
{
    private readonly List<MapPlacemark> _placemarks = [];
    private readonly InMemorySourceAttachmentRepository? _attachmentRepository;

    public IReadOnlyList<MapPlacemark> Placemarks => _placemarks.AsReadOnly();

    public InMemoryMapPlacemarkRepository()
    {
    }

    /// <summary>With an attachment repo, DeleteBySourceAsync can resolve the join.</summary>
    public InMemoryMapPlacemarkRepository(InMemorySourceAttachmentRepository attachmentRepository)
    {
        _attachmentRepository = attachmentRepository;
    }

    public void Seed(params MapPlacemark[] placemarks) => _placemarks.AddRange(placemarks);

    public Task<MapPlacemark> CreateAsync(MapPlacemark placemark, CancellationToken cancellationToken = default)
    {
        _placemarks.Add(placemark);
        return Task.FromResult(placemark);
    }

    public Task<MapPlacemark?> GetByAttachmentAndArtifactAsync(Guid sourceAttachmentId, Guid artifactId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_placemarks.FirstOrDefault(
            p => p.SourceAttachmentId == sourceAttachmentId && p.ArtifactId == artifactId));
    }

    public Task<IReadOnlyList<MapPlacemark>> ListByAttachmentAsync(Guid sourceAttachmentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MapPlacemark>>(
            _placemarks.Where(p => p.SourceAttachmentId == sourceAttachmentId).ToList().AsReadOnly());
    }

    public Task<IReadOnlyList<MapPlacemark>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MapPlacemark>>(
            _placemarks.Where(p => p.ArtifactId == artifactId).ToList().AsReadOnly());
    }

    public Task<MapPlacemark> UpdateAsync(MapPlacemark placemark, CancellationToken cancellationToken = default)
    {
        var index = _placemarks.FindIndex(p => p.Id == placemark.Id);
        if (index >= 0)
        {
            _placemarks[index] = placemark;
        }
        return Task.FromResult(placemark);
    }

    public Task DeleteAsync(Guid placemarkId, CancellationToken cancellationToken = default)
    {
        _placemarks.RemoveAll(p => p.Id == placemarkId);
        return Task.CompletedTask;
    }

    public Task DeleteByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        _placemarks.RemoveAll(p => p.ArtifactId == artifactId);
        return Task.CompletedTask;
    }

    public async Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        if (_attachmentRepository is null)
        {
            return;
        }

        var attachmentIds = (await _attachmentRepository.ListBySourceAsync(sourceId, cancellationToken))
            .Select(a => a.Id)
            .ToHashSet();
        _placemarks.RemoveAll(p => attachmentIds.Contains(p.SourceAttachmentId));
    }
}
