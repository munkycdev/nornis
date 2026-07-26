using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryContinuityDismissalRepository : IContinuityDismissalRepository
{
    private readonly List<ContinuityDismissal> _dismissals = [];

    public IReadOnlyList<ContinuityDismissal> Dismissals => _dismissals.AsReadOnly();

    public void Seed(params ContinuityDismissal[] dismissals) => _dismissals.AddRange(dismissals);

    public Task<ContinuityDismissal> CreateAsync(
        ContinuityDismissal dismissal, CancellationToken cancellationToken = default)
    {
        _dismissals.Add(dismissal);
        return Task.FromResult(dismissal);
    }

    public Task<IReadOnlyList<ContinuityDismissal>> ListByWorldAsync(
        Guid worldId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ContinuityDismissal> result = _dismissals
            .Where(d => d.WorldId == worldId)
            .OrderBy(d => d.DismissedAtUtc)
            .ToList();

        return Task.FromResult(result);
    }
}
