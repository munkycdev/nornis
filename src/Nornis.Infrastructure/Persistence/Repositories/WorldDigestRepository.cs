using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class WorldDigestRepository : IWorldDigestRepository
{
    private readonly NornisDbContext _context;

    public WorldDigestRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<WorldDigest?> GetByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.WorldDigests
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.WorldId == worldId, cancellationToken);
    }

    public async Task UpsertAsync(WorldDigest digest, CancellationToken cancellationToken = default)
    {
        var existing = await _context.WorldDigests
            .FirstOrDefaultAsync(d => d.WorldId == digest.WorldId, cancellationToken);

        if (existing is null)
        {
            _context.WorldDigests.Add(digest);
        }
        else
        {
            existing.GmContentMarkdown = digest.GmContentMarkdown;
            existing.PartyContentMarkdown = digest.PartyContentMarkdown;
            existing.Model = digest.Model;
            existing.GeneratedAt = digest.GeneratedAt;
            existing.GeneratedByUserId = digest.GeneratedByUserId;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two generations raced on the insert; the unique index let one through. The
            // loser's digest is a regenerable read-model built seconds apart from the
            // winner's — losing it quietly is correct (same shape as WorkerHeartbeat).
            _context.ChangeTracker.Clear();
        }
    }
}
