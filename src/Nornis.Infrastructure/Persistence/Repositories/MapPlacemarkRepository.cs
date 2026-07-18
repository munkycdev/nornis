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
        _context.MapPlacemarks.Update(placemark);
        await _context.SaveChangesAsync(cancellationToken);
        return placemark;
    }

    public async Task DeleteAsync(Guid placemarkId, CancellationToken cancellationToken = default)
    {
        var placemark = await _context.MapPlacemarks
            .FirstOrDefaultAsync(p => p.Id == placemarkId, cancellationToken);

        if (placemark is null)
        {
            return;
        }

        _context.MapPlacemarks.Remove(placemark);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        // Tracked-load delete: the InMemory provider used in tests lacks ExecuteDelete.
        var placemarks = await _context.MapPlacemarks
            .Where(p => p.ArtifactId == artifactId)
            .ToListAsync(cancellationToken);

        if (placemarks.Count == 0)
        {
            return;
        }

        _context.MapPlacemarks.RemoveRange(placemarks);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var placemarks = await _context.MapPlacemarks
            .Where(p => _context.SourceAttachments
                .Any(a => a.Id == p.SourceAttachmentId && a.SourceId == sourceId))
            .ToListAsync(cancellationToken);

        if (placemarks.Count == 0)
        {
            return;
        }

        _context.MapPlacemarks.RemoveRange(placemarks);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
