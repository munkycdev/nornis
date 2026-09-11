using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// <inheritdoc cref="ISlugBackfiller"/>
///
/// The work itself is <see cref="SlugAssigner"/>: this only loads the slugless rows of each kind
/// and marks the column modified, so the save path assigns exactly as it would for a new row.
/// One kind per save, so a unique-index race with another host loses at most one kind and the
/// next start picks it up.
/// </summary>
public class SlugBackfiller : ISlugBackfiller
{
    private readonly NornisDbContext _context;

    public SlugBackfiller(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<int> BackfillAsync(CancellationToken cancellationToken = default)
    {
        var assigned = 0;
        assigned += await BackfillAsync<Artifact>(cancellationToken);
        assigned += await BackfillAsync<Campaign>(cancellationToken);
        assigned += await BackfillAsync<Character>(cancellationToken);
        assigned += await BackfillAsync<Source>(cancellationToken);
        assigned += await BackfillAsync<LibraryDocument>(cancellationToken);
        return assigned;
    }

    private async Task<int> BackfillAsync<TEntity>(CancellationToken cancellationToken)
        where TEntity : class, ISlugged
    {
        var rows = await _context.Set<TEntity>()
            .Where(e => e.Slug == null)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            _context.Entry(row).Property(e => e.Slug).IsModified = true;
        }

        if (rows.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return rows.Count;
    }
}
