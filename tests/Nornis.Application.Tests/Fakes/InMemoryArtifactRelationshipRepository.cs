using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryArtifactRelationshipRepository : IArtifactRelationshipRepository
{
    private readonly List<ArtifactRelationship> _relationships = [];

    public IReadOnlyList<ArtifactRelationship> Relationships => _relationships.AsReadOnly();

    public void Seed(params ArtifactRelationship[] relationships) => _relationships.AddRange(relationships);

    public void Seed(IEnumerable<ArtifactRelationship> relationships) => _relationships.AddRange(relationships);

    public Task<ArtifactRelationship> CreateAsync(ArtifactRelationship relationship, CancellationToken cancellationToken = default)
    {
        _relationships.Add(relationship);
        return Task.FromResult(relationship);
    }

    public Task<ArtifactRelationship?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var relationship = _relationships.FirstOrDefault(r => r.Id == id);
        return Task.FromResult(relationship);
    }

    public Task<IReadOnlyList<ArtifactRelationship>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        var results = _relationships
            .Where(r => r.ArtifactAId == artifactId || r.ArtifactBId == artifactId)
            .ToList();
        return Task.FromResult<IReadOnlyList<ArtifactRelationship>>(results.AsReadOnly());
    }

    public Task<IReadOnlyList<ArtifactRelationship>> ListByArtifactIdsAsync(
        IReadOnlyList<Guid> artifactIds,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        CancellationToken cancellationToken = default)
    {
        if (artifactIds.Count == 0)
            return Task.FromResult<IReadOnlyList<ArtifactRelationship>>([]);

        var results = _relationships
            .Where(r =>
                (artifactIds.Contains(r.ArtifactAId) || artifactIds.Contains(r.ArtifactBId))
                && allowedVisibilities.Contains(r.Visibility))
            .ToList();
        return Task.FromResult<IReadOnlyList<ArtifactRelationship>>(results.AsReadOnly());
    }

    public Task DeleteAsync(Guid relationshipId, CancellationToken cancellationToken = default)
    {
        _relationships.RemoveAll(r => r.Id == relationshipId);
        return Task.CompletedTask;
    }

    public Task<ArtifactRelationship> UpdateAsync(ArtifactRelationship relationship, CancellationToken cancellationToken = default)
    {
        var index = _relationships.FindIndex(r => r.Id == relationship.Id);
        if (index >= 0)
        {
            _relationships[index] = relationship;
        }
        return Task.FromResult(relationship);
    }
}
