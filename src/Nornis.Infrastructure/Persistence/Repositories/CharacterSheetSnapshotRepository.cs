using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class CharacterSheetSnapshotRepository : ICharacterSheetSnapshotRepository
{
    private readonly NornisDbContext _context;

    public CharacterSheetSnapshotRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CharacterSheetSnapshot>> ListByCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CharacterSheetSnapshots
            .AsNoTracking()
            .Where(s => s.CharacterId == characterId)
            .OrderByDescending(s => s.AsOf)
            .ThenByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<CharacterSheetSnapshot?> GetByIdAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        return await _context.CharacterSheetSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == snapshotId, cancellationToken);
    }

    public async Task<CharacterSheetSnapshot> CreateAsync(
        CharacterSheetSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        _context.CharacterSheetSnapshots.Add(snapshot);
        await _context.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    public async Task DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        await _context.DeleteWhereAsync<CharacterSheetSnapshot>(s => s.Id == snapshotId, cancellationToken);
    }

    public async Task DeleteByCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        await _context.DeleteWhereAsync<CharacterSheetSnapshot>(s => s.CharacterId == characterId, cancellationToken);
    }
}
