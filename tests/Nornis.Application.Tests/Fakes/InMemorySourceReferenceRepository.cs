using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemorySourceReferenceRepository : ISourceReferenceRepository
{
    private readonly List<SourceReference> _references = [];

    public IReadOnlyList<SourceReference> References => _references.AsReadOnly();

    public Task<SourceReference> CreateAsync(SourceReference reference, CancellationToken cancellationToken = default)
    {
        _references.Add(reference);
        return Task.FromResult(reference);
    }

    public Task<IReadOnlyList<SourceReference>> ListByTargetAsync(SourceReferenceTargetType targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        var references = _references
            .Where(r => r.TargetType == targetType && r.TargetId == targetId)
            .ToList();
        return Task.FromResult<IReadOnlyList<SourceReference>>(references.AsReadOnly());
    }

    public Task<IReadOnlyList<SourceReference>> ListByTargetIdsAsync(IReadOnlyList<Guid> targetIds, CancellationToken cancellationToken = default)
    {
        if (targetIds.Count == 0)
            return Task.FromResult<IReadOnlyList<SourceReference>>([]);

        var references = _references
            .Where(r => targetIds.Contains(r.TargetId))
            .ToList();
        return Task.FromResult<IReadOnlyList<SourceReference>>(references.AsReadOnly());
    }
}
