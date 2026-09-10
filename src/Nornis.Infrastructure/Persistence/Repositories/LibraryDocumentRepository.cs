using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class LibraryDocumentRepository : ILibraryDocumentRepository
{
    private readonly NornisDbContext _context;

    public LibraryDocumentRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<LibraryDocument> CreateAsync(LibraryDocument document, CancellationToken cancellationToken = default)
    {
        _context.LibraryDocuments.Add(document);
        await _context.SaveChangesAsync(cancellationToken);
        return document;
    }

    public async Task<LibraryDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.LibraryDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<LibraryDocument>> ListByWorldAsync(
        Guid worldId,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        CancellationToken cancellationToken = default)
    {
        return await _context.LibraryDocuments
            .AsNoTracking()
            .Where(d => d.WorldId == worldId)
            .Where(d => allowedVisibilities.Contains(d.Visibility))
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyIndexedAsync(
        Guid worldId,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        CancellationToken cancellationToken = default)
    {
        return await _context.LibraryDocuments
            .AsNoTracking()
            .AnyAsync(d => d.WorldId == worldId
                && d.Status == LibraryDocumentStatus.Indexed
                && allowedVisibilities.Contains(d.Visibility), cancellationToken);
    }

    public async Task<LibraryDocument> UpdateAsync(LibraryDocument document, CancellationToken cancellationToken = default)
    {
        await _context.SaveAndDetachAsync(document, cancellationToken);
        return document;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Excerpts filed from this document are sources in their own right — their text was
        // copied at filing — and they outlive it. The FK is Restrict (Worlds already cascades
        // to both tables), so they are detached here rather than by the database.
        await _context.SetWhereAsync<Source, Guid?>(
            s => s.LibraryDocumentId == id, s => s.LibraryDocumentId, null, cancellationToken);

        await _context.DeleteWhereAsync<LibraryDocument>(d => d.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<LibraryDocument>> ListAbandonedPendingUploadsAsync(
        DateTimeOffset createdBefore,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _context.LibraryDocuments
            .AsNoTracking()
            .Where(d => d.Status == LibraryDocumentStatus.PendingUpload && d.CreatedAt < createdBefore)
            .OrderBy(d => d.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
