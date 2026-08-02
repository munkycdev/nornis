using Microsoft.Extensions.DependencyInjection;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lets a singleton read a flag without capturing a scoped DbContext.
///
/// <see cref="Services.AiPauseGate"/> has to be a singleton — its whole value is a cache
/// shared across requests, and a scoped one would read the database on every paid AI call,
/// which is what the cache exists to prevent. But a singleton that holds a scoped repository
/// is the classic captive dependency: it pins one DbContext for the life of the process,
/// which is both a leak and a source of stale reads.
///
/// One scope per read, opened and closed around a single query, costs nothing at the
/// once-a-minute rate the gate actually reads at.
/// </summary>
public sealed class ScopedOperationalFlagReader : IOperationalFlagRepository
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopedOperationalFlagReader(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<OperationalFlag?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOperationalFlagRepository>();
        return await repository.GetAsync(name, cancellationToken);
    }
}
