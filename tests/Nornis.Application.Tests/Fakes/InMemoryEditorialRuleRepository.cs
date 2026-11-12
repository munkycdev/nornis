using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryEditorialRuleRepository : IEditorialRuleRepository
{
    private readonly List<EditorialRule> _rules = [];

    public IReadOnlyList<EditorialRule> Rules => _rules.AsReadOnly();

    public void Seed(params EditorialRule[] rules) => _rules.AddRange(rules);

    public Task<EditorialRule> CreateAsync(EditorialRule rule, CancellationToken cancellationToken = default)
    {
        _rules.Add(rule);
        return Task.FromResult(rule);
    }

    public Task<EditorialRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rules.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<EditorialRule>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EditorialRule> result = _rules
            .Where(r => r.WorldId == worldId)
            .OrderBy(r => r.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _rules.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }
}
