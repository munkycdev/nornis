using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryArtifactFactRepository : IArtifactFactRepository
{
    private readonly List<ArtifactFact> _facts = [];

    public IReadOnlyList<ArtifactFact> Facts => _facts.AsReadOnly();

    public void Seed(params ArtifactFact[] facts) => _facts.AddRange(facts);

    public void Seed(IEnumerable<ArtifactFact> facts) => _facts.AddRange(facts);

    public Task<ArtifactFact> CreateAsync(ArtifactFact fact, CancellationToken cancellationToken = default)
    {
        _facts.Add(fact);
        return Task.FromResult(fact);
    }

    public Task<ArtifactFact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fact = _facts.FirstOrDefault(f => f.Id == id);
        return Task.FromResult(fact);
    }

    public Task<IReadOnlyList<ArtifactFact>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        var facts = _facts.Where(f => f.ArtifactId == artifactId).ToList();
        return Task.FromResult<IReadOnlyList<ArtifactFact>>(facts.AsReadOnly());
    }

    public Task<ArtifactFact> UpdateAsync(ArtifactFact fact, CancellationToken cancellationToken = default)
    {
        var index = _facts.FindIndex(f => f.Id == fact.Id);
        if (index >= 0)
        {
            _facts[index] = fact;
        }
        return Task.FromResult(fact);
    }

    public Task<IReadOnlyList<ArtifactFact>> ListByArtifactIdsAsync(
        IReadOnlyList<Guid> artifactIds,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        int maxPerArtifact,
        CancellationToken cancellationToken = default)
    {
        var results = _facts
            .Where(f => artifactIds.Contains(f.ArtifactId))
            .Where(f => allowedVisibilities.Contains(f.Visibility))
            .GroupBy(f => f.ArtifactId)
            .SelectMany(g => g.OrderByDescending(f => f.UpdatedAt).Take(maxPerArtifact))
            .ToList();
        return Task.FromResult<IReadOnlyList<ArtifactFact>>(results.AsReadOnly());
    }
}
