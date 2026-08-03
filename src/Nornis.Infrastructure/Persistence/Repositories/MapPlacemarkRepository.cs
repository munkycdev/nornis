using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class MapPlacemarkRepository : IMapPlacemarkRepository
{
    private readonly NornisDbContext _context;

    public MapPlacemarkRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<MapPlacemark> CreateAsync(MapPlacemark placemark, CancellationToken cancellationToken = default)
    {
        _context.MapPlacemarks.Add(placemark);
        await _context.SaveChangesAsync(cancellationToken);
        return placemark;
    }

    public async Task<MapPlacemark?> GetByAttachmentAndArtifactAsync(Guid sourceAttachmentId, Guid artifactId, CancellationToken cancellationToken = default)
    {
        return await _context.MapPlacemarks
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.SourceAttachmentId == sourceAttachmentId && p.ArtifactId == artifactId, cancellationToken);
    }

    public async Task<IReadOnlyList<MapPlacemark>> ListByAttachmentAsync(Guid sourceAttachmentId, CancellationToken cancellationToken = default)
    {
        return await _context.MapPlacemarks
            .AsNoTracking()
            .Where(p => p.SourceAttachmentId == sourceAttachmentId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MapPlacemark>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        return await _context.MapPlacemarks
            .AsNoTracking()
            .Where(p => p.ArtifactId == artifactId)
            .ToListAsync(cancellationToken);
    }

    public async Task<MapPlacemark> UpdateAsync(MapPlacemark placemark, CancellationToken cancellationToken = default)
    {
        await _context.SaveAndDetachAsync(placemark, cancellationToken);
        return placemark;
    }

    public async Task UpdateRangeAsync(IReadOnlyList<MapPlacemark> placemarks, CancellationToken cancellationToken = default)
    {
        if (placemarks.Count == 0)
        {
            return;
        }

        await _context.SaveAndDetachRangeAsync(placemarks, cancellationToken);
    }

    public Task DeleteAsync(Guid placemarkId, CancellationToken cancellationToken = default) =>
        _context.DeleteWhereAsync<MapPlacemark>(p => p.Id == placemarkId, cancellationToken);

    public Task DeleteByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default) =>
        _context.DeleteWhereAsync<MapPlacemark>(p => p.ArtifactId == artifactId, cancellationToken);

    public Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        _context.DeleteWhereAsync<MapPlacemark>(
            p => _context.SourceAttachments
                .Any(a => a.Id == p.SourceAttachmentId && a.SourceId == sourceId),
            cancellationToken);
}
