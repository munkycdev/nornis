using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryArtifactRepository : IArtifactRepository
{
    private readonly List<Artifact> _artifacts = [];

    public IReadOnlyList<Artifact> Artifacts => _artifacts.AsReadOnly();

    public void Seed(params Artifact[] artifacts) => _artifacts.AddRange(artifacts);

    public void Seed(IEnumerable<Artifact> artifacts) => _artifacts.AddRange(artifacts);

    public Task<Artifact> CreateAsync(Artifact artifact, CancellationToken cancellationToken = default)
    {
        _artifacts.Add(artifact);
        return Task.FromResult(artifact);
    }

    public Task<Artifact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var artifact = _artifacts.FirstOrDefault(a => a.Id == id);
        return Task.FromResult(artifact);
    }

    public Task<IReadOnlyList<Artifact>> ListByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        var results = _artifacts.Where(a => ids.Contains(a.Id)).ToList();
        return Task.FromResult<IReadOnlyList<Artifact>>(results.AsReadOnly());
    }

    public Task<IReadOnlyList<Artifact>> ListByWorldAsync(
        Guid worldId,
        ArtifactType? type = null,
        CancellationToken cancellationToken = default)
    {
        var query = _artifacts.Where(a => a.WorldId == worldId);
        if (type.HasValue)
            query = query.Where(a => a.Type == type.Value);

        return Task.FromResult<IReadOnlyList<Artifact>>(query.ToList().AsReadOnly());
    }

    public Task<Artifact> UpdateAsync(Artifact artifact, CancellationToken cancellationToken = default)
    {
        var index = _artifacts.FindIndex(a => a.Id == artifact.Id);
        if (index >= 0)
        {
            _artifacts[index] = artifact;
        }
        return Task.FromResult(artifact);
    }

    public Task UpdateSummaryAsync(Guid id, string? summary, DateTimeOffset refreshedAt, CancellationToken cancellationToken = default)
    {
        var artifact = _artifacts.FirstOrDefault(a => a.Id == id)
            ?? throw new InvalidOperationException($"{nameof(Artifact)} with id '{id}' not found.");

        if (summary is not null)
        {
            artifact.Summary = summary;
            artifact.UpdatedAt = refreshedAt;
        }

        artifact.SummaryRefreshedAt = refreshedAt;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        _artifacts.RemoveAll(a => a.Id == artifactId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Artifact>> ListByEquivalentNameAsync(
        Guid worldId,
        string name,
        VisibilityFilter filter,
        CancellationToken cancellationToken = default)
    {
        var results = _artifacts
            .Where(a => a.WorldId == worldId &&
                        ArtifactNameKey.AreEquivalent(a.Name, name) &&
                        a.Status != ArtifactStatus.Archived &&
                        filter.CanSee(a.Visibility, a.CreatedByUserId))
            .ToList();
        return Task.FromResult<IReadOnlyList<Artifact>>(results.AsReadOnly());
    }

    /// <summary>How many times the by-type listing was actually queried. Read by the tests that
    /// assert the applicator reads the dedup candidate set once per batch rather than per create.</summary>
    public int ListByTypeCallCount { get; set; }

    public Task<IReadOnlyList<Artifact>> ListByTypeAsync(
        Guid worldId,
        ArtifactType type,
        VisibilityFilter filter,
        CancellationToken cancellationToken = default)
    {
        ListByTypeCallCount++;

        var results = _artifacts
            .Where(a => a.WorldId == worldId &&
                        a.Type == type &&
                        a.Status != ArtifactStatus.Archived &&
                        filter.CanSee(a.Visibility, a.CreatedByUserId))
            .OrderBy(a => a.Name)
            .Select(Detach)
            .ToList();
        return Task.FromResult<IReadOnlyList<Artifact>>(results.AsReadOnly());
    }

    /// <summary>
    /// The real query is <c>AsNoTracking</c>, so its results are detached snapshots: a later
    /// update through the repository does not reach back and change them. Returning the stored
    /// instances instead would let a caller that holds onto a listing see edits it could never
    /// see in production — which is exactly the staleness any cache over this listing has to
    /// handle, so a fake that hides it would make those tests pass for the wrong reason.
    /// </summary>
    private static Artifact Detach(Artifact a) => new()
    {
        Id = a.Id,
        WorldId = a.WorldId,
        Type = a.Type,
        Name = a.Name,
        Summary = a.Summary,
        Visibility = a.Visibility,
        Confidence = a.Confidence,
        Status = a.Status,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
        CreatedByUserId = a.CreatedByUserId,
        RowVersion = a.RowVersion
    };

    public Task<IReadOnlyList<Artifact>> ListRecentByWorldAsync(
        Guid worldId,
        VisibilityFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var results = _artifacts
            .Where(a => a.WorldId == worldId &&
                        a.Status != ArtifactStatus.Archived &&
                        filter.CanSee(a.Visibility, a.CreatedByUserId))
            .OrderByDescending(a => a.UpdatedAt)
            .Take(maxCount)
            .ToList();
        return Task.FromResult<IReadOnlyList<Artifact>>(results.AsReadOnly());
    }

    public Task<IReadOnlyList<Artifact>> ListByNamesInTextAsync(
        Guid worldId,
        string text,
        VisibilityFilter filter,
        CancellationToken cancellationToken = default)
    {
        var results = _artifacts
            .Where(a => a.WorldId == worldId &&
                        a.Status != ArtifactStatus.Archived &&
                        filter.CanSee(a.Visibility, a.CreatedByUserId) &&
                        ContainsWholeWord(text, a.Name))
            .ToList();
        return Task.FromResult<IReadOnlyList<Artifact>>(results.AsReadOnly());
    }

    private static bool ContainsWholeWord(string text, string word)
    {
        if (string.IsNullOrEmpty(word))
            return false;

        var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + word.Length;
            var after = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);

            if (before && after)
                return true;

            index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
