using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
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
        // An upload Bibliotaph lists is about to vanish from that listing (feature 31). Staged
        // and saved first: a delete that then fails costs Bibliotaph one needless rescan, while
        // the other order could lose the event.
        var document = await _context.LibraryDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is not null && BibliotaphExposure.IsExposed(document)
            && await BibliotaphOutboxCapture.QueueAsync(
                _context, document.WorldId, BibliotaphWorldEventKind.FilesChanged, DateTimeOffset.UtcNow, cancellationToken))
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        // Excerpts filed from this document are sources in their own right — their text was
        // copied at filing — and they outlive it. The FK is Restrict (Worlds already cascades
        // to both tables), so they are detached here rather than by the database.
        await _context.SetWhereAsync<Source, Guid?>(
            s => s.LibraryDocumentId == id, s => s.LibraryDocumentId, null, cancellationToken);

        // An Adventure printed to this document forgets it: null means "never printed, or the
        // print was deleted", which is what the Prep page shows.
        await _context.SetWhereAsync<Adventure, Guid?>(
            a => a.LastPrintedDocumentId == id, a => a.LastPrintedDocumentId, null, cancellationToken);

        // Who opened it goes with it; the FK cascades too, but nothing here relies on that.
        await _context.DeleteWhereAsync<LibraryReadingRecord>(r => r.LibraryDocumentId == id, cancellationToken);

        await _context.DeleteWhereAsync<LibraryDocument>(d => d.Id == id, cancellationToken);
    }

    public async Task SaveBibliotaphReconcileAsync(
        IReadOnlyList<LibraryDocument> added, IReadOnlyList<LibraryDocument> changed, CancellationToken cancellationToken = default)
    {
        _context.LibraryDocuments.AddRange(added);
        _context.LibraryDocuments.UpdateRange(changed);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
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
