using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemorySourceRepository : ISourceRepository
{
    private readonly List<Source> _sources = [];
    private readonly List<(Guid SourceId, SourceProcessingStatus From, SourceProcessingStatus To)> _statusTransitions = [];

    public IReadOnlyList<Source> Sources => _sources.AsReadOnly();

    /// <summary>
    /// Records of all ProcessingStatus transitions made via UpdateProcessingStatusAsync.
    /// Useful for asserting correct state machine transitions in property tests.
    /// </summary>
    public IReadOnlyList<(Guid SourceId, SourceProcessingStatus From, SourceProcessingStatus To)> StatusTransitions
        => _statusTransitions.AsReadOnly();

    public void Seed(params Source[] sources) => _sources.AddRange(sources);

    public void Seed(IEnumerable<Source> sources) => _sources.AddRange(sources);

    public Task<Source> CreateAsync(Source source, CancellationToken cancellationToken = default)
    {
        _sources.Add(source);
        return Task.FromResult(source);
    }

    public Task<Source?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.Id == id);
        return Task.FromResult(source);
    }

    public Task<IReadOnlyList<Source>> ListByCampaignAsync(Guid campaignId, VisibilityScope? visibility = null, CancellationToken cancellationToken = default)
    {
        var query = _sources.Where(s => s.CampaignId == campaignId);

        if (visibility is not null)
        {
            query = query.Where(s => s.Visibility == visibility.Value);
        }

        return Task.FromResult<IReadOnlyList<Source>>(query.ToList().AsReadOnly());
    }

    public Task UpdateProcessingStatusAsync(Guid id, SourceProcessingStatus status, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.Id == id);
        if (source is not null)
        {
            _statusTransitions.Add((id, source.ProcessingStatus, status));
            source.ProcessingStatus = status;
        }
        return Task.CompletedTask;
    }

    public Task<Source> UpdateAsync(Source source, CancellationToken cancellationToken = default)
    {
        var index = _sources.FindIndex(s => s.Id == source.Id);
        if (index >= 0)
        {
            _sources[index] = source;
        }
        return Task.FromResult(source);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _sources.RemoveAll(s => s.Id == id);
        return Task.CompletedTask;
    }
}
