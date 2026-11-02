using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class LibraryReadingRecordRepository : ILibraryReadingRecordRepository
{
    private readonly NornisDbContext _context;

    public LibraryReadingRecordRepository(NornisDbContext context)
    {
        _context = context;
    }

    public Task AddAsync(LibraryReadingRecord record, CancellationToken cancellationToken = default) =>
        _context.AddAndDetachAsync(record, cancellationToken);

    public async Task<IReadOnlyList<LibraryReadingRecord>> ListRecentAsync(
        Guid documentId, int max, CancellationToken cancellationToken = default) =>
        await _context.LibraryReadingRecords
            .AsNoTracking()
            .Where(r => r.LibraryDocumentId == documentId)
            .OrderByDescending(r => r.OpenedAt)
            .Take(max)
            .ToListAsync(cancellationToken);

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset before, int max, CancellationToken cancellationToken = default)
    {
        var ids = await _context.LibraryReadingRecords
            .AsNoTracking()
            .Where(r => r.OpenedAt < before)
            .OrderBy(r => r.OpenedAt)
            .Select(r => r.Id)
            .Take(max)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        await _context.DeleteWhereAsync<LibraryReadingRecord>(r => ids.Contains(r.Id), cancellationToken);
        return ids.Count;
    }
}
