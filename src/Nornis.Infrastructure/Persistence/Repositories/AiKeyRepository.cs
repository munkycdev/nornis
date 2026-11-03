using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class AiKeyRepository : IAiKeyRepository
{
    private readonly NornisDbContext _context;

    public AiKeyRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<UserAiKey>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _context.UserAiKeys
            .AsNoTracking()
            .Include(k => k.CompatibleModels)
            .Where(k => k.UserId == userId)
            .OrderBy(k => k.AddedAt)
            .ThenBy(k => k.Id)
            .ToListAsync(cancellationToken);

    public Task<UserAiKey?> GetAsync(Guid keyId, CancellationToken cancellationToken = default) =>
        _context.UserAiKeys
            .AsNoTracking()
            .Include(k => k.CompatibleModels)
            .FirstOrDefaultAsync(k => k.Id == keyId, cancellationToken);

    public Task<UserAiModelChoice?> GetChoiceAsync(Guid userId, AiTask task, CancellationToken cancellationToken = default) =>
        _context.UserAiModelChoices
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Task == task, cancellationToken);

    public async Task<IReadOnlyList<UserAiModelChoice>> ListChoicesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _context.UserAiModelChoices
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);

    public Task MarkCheckFailedAsync(
        Guid keyId, DateTimeOffset keyVersion, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        _context.UserAiKeys
            .Where(k => k.Id == keyId && k.UpdatedAt == keyVersion)
            .ExecuteUpdateAsync(set => set
                .SetProperty(k => k.CheckFailed, true)
                .SetProperty(k => k.CheckedAt, at), cancellationToken);
}
