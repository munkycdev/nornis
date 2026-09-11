using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence;

/// <inheritdoc cref="ISlugResolver"/>
public class SlugResolver : ISlugResolver
{
    private readonly NornisDbContext _context;

    public SlugResolver(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<Guid?> ResolveAsync<TEntity>(Guid worldId, string key, CancellationToken cancellationToken = default)
        where TEntity : class, ISlugged
    {
        if (Guid.TryParse(key, out var id))
        {
            return id;
        }

        // Slugs are stored lowercase; a pasted link with a capital in it should still land.
        var slug = key.Trim().ToLowerInvariant();
        if (slug.Length == 0 || slug.Length > Slug.MaxStoredLength)
        {
            return null;
        }

        return await _context.Set<TEntity>()
            .AsNoTracking()
            .Where(e => e.WorldId == worldId && e.Slug == slug)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
