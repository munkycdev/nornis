using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
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

    public Task<IReadOnlyList<Artifact>> ListByCampaignAsync(
        Guid campaignId,
        ArtifactType? type = null,
        VisibilityScope? visibility = null,
        CancellationToken cancellationToken = default)
    {
        var query = _artifacts.Where(a => a.CampaignId == campaignId);
        if (type.HasValue)
            query = query.Where(a => a.Type == type.Value);
        if (visibility.HasValue)
            query = query.Where(a => a.Visibility == visibility.Value);

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

    public Task<IReadOnlyList<Artifact>> SearchByNameAsync(
        Guid campaignId,
        string searchTerm,
        CancellationToken cancellationToken = default)
    {
        var results = _artifacts
            .Where(a => a.CampaignId == campaignId &&
                        a.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult<IReadOnlyList<Artifact>>(results.AsReadOnly());
    }

    public Task<IReadOnlyList<Artifact>> ListRecentByCampaignAsync(
        Guid campaignId,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var results = _artifacts
            .Where(a => a.CampaignId == campaignId && allowedVisibilities.Contains(a.Visibility))
            .OrderByDescending(a => a.UpdatedAt)
            .Take(maxCount)
            .ToList();
        return Task.FromResult<IReadOnlyList<Artifact>>(results.AsReadOnly());
    }

    public Task<IReadOnlyList<Artifact>> ListByNamesInTextAsync(
        Guid campaignId,
        string text,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        CancellationToken cancellationToken = default)
    {
        var results = _artifacts
            .Where(a => a.CampaignId == campaignId &&
                        allowedVisibilities.Contains(a.Visibility) &&
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
