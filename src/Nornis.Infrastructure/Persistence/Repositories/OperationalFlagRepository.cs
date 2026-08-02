using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class OperationalFlagRepository : IOperationalFlagRepository
{
    private readonly NornisDbContext _context;

    public OperationalFlagRepository(NornisDbContext context)
    {
        _context = context;
    }

    public Task<OperationalFlag?> GetAsync(string name, CancellationToken cancellationToken = default) =>
        _context.Set<OperationalFlag>()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Name == name, cancellationToken);
}
