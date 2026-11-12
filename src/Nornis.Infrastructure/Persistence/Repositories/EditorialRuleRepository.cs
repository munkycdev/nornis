using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class EditorialRuleRepository : IEditorialRuleRepository
{
    private readonly NornisDbContext _context;

    public EditorialRuleRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<EditorialRule> CreateAsync(EditorialRule rule, CancellationToken cancellationToken = default)
    {
        _context.EditorialRules.Add(rule);
        await _context.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<EditorialRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.EditorialRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<EditorialRule>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.EditorialRules
            .AsNoTracking()
            .Where(r => r.WorldId == worldId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.DeleteWhereAsync<EditorialRule>(r => r.Id == id, cancellationToken);
}
