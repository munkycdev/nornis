using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class BibliotaphProviderRepository : IBibliotaphProviderRepository
{
    private readonly NornisDbContext _context;

    public BibliotaphProviderRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<World>> ListWorldsAsync(
        Guid userId, Guid? afterId, int take, CancellationToken cancellationToken = default) =>
        await WorldsOf(userId)
            .Where(w => afterId == null || w.Id > afterId.Value)
            .OrderBy(w => w.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<World?> GetWorldAsync(Guid userId, Guid worldId, CancellationToken cancellationToken = default) =>
        WorldsOf(userId).FirstOrDefaultAsync(w => w.Id == worldId, cancellationToken);

    public async Task<IReadOnlyList<LibraryDocument>> ListFilesAsync(
        Guid worldId, Guid? afterId, int take, CancellationToken cancellationToken = default) =>
        await _context.LibraryDocuments
            .AsNoTracking()
            .Where(d => d.WorldId == worldId)
            .Where(BibliotaphExposure.Rule)
            .Where(d => afterId == null || d.Id > afterId.Value)
            .OrderBy(d => d.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<LibraryDocument?> GetFileAsync(Guid userId, Guid fileId, CancellationToken cancellationToken = default) =>
        _context.LibraryDocuments
            .AsNoTracking()
            .Where(BibliotaphExposure.Rule)
            .Where(d => WorldsOf(userId).Any(w => w.Id == d.WorldId))
            .FirstOrDefaultAsync(d => d.Id == fileId, cancellationToken);

    private IQueryable<World> WorldsOf(Guid userId) =>
        _context.Worlds
            .AsNoTracking()
            .Where(w => w.CreatedByUserId == userId && !w.IsDemo && !w.IsTemplate);
}
