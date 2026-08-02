using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
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

    public Task<IReadOnlyList<SourceListItem>> ListSummariesByWorldAsync(
        Guid worldId,
        Guid requestingUserId,
        WorldRole role,
        Guid? campaignId = null,
        bool unassignedOnly = false,
        CancellationToken cancellationToken = default)
    {
        // Mirrors the real query: shared visibility rule, same campaign filters, newest first.
        var canSee = SourceVisibilityRule.Compile(requestingUserId, role);

        var query = _sources.Where(s => s.WorldId == worldId).Where(canSee);

        if (campaignId is not null)
        {
            query = query.Where(s => s.CampaignId == campaignId);
        }
        else if (unassignedOnly)
        {
            query = query.Where(s => s.CampaignId is null);
        }

        var result = query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Select(s => new SourceListItem(
                s.Id, s.WorldId, s.Type, s.Title, s.OccurredAt, s.CreatedAt,
                s.CreatedByUserId, s.Visibility, s.ProcessingStatus, s.CampaignId, s.Campaign?.Name))
            .ToList();

        return Task.FromResult<IReadOnlyList<SourceListItem>>(result.AsReadOnly());
    }

    public Task<bool> AnyCreatedAfterAsync(
        Guid worldId,
        DateTimeOffset after,
        SourceProcessingStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var any = _sources.Any(s => s.WorldId == worldId
            && s.CreatedAt > after
            && (status is null || s.ProcessingStatus == status.Value));

        return Task.FromResult(any);
    }

    public Task<IReadOnlyDictionary<SourceProcessingStatus, int>> CountByStatusAsync(
        Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken cancellationToken = default)
    {
        // Uses the same shared rule the real query translates to SQL, so this fake cannot
        // disagree with production about who sees what.
        var canSee = SourceVisibilityRule.Compile(requestingUserId, role);

        var counts = _sources
            .Where(s => s.WorldId == worldId)
            .Where(canSee)
            .GroupBy(s => s.ProcessingStatus)
            .ToDictionary(g => g.Key, g => g.Count());

        return Task.FromResult<IReadOnlyDictionary<SourceProcessingStatus, int>>(counts);
    }

    public Task<int> CountAwaitingExtractionAsync(CancellationToken cancellationToken = default)
    {
        // Unscoped by world and visibility, like the real query — it answers an operational
        // question, not a question about what a reader may see.
        return Task.FromResult(_sources.Count(s =>
            s.ProcessingStatus == SourceProcessingStatus.Queued
            || s.ProcessingStatus == SourceProcessingStatus.Processing));
    }

    public Task<IReadOnlyList<SourceAttribution>> ListAttributionByIdsAsync(
        IReadOnlyList<Guid> ids, Guid userId, WorldRole role, CancellationToken cancellationToken = default)
    {
        // Mirrors the real projection, including that unknown ids are simply absent — callers
        // rely on that to fail closed on a reference they cannot attribute. Visibility uses
        // the same shared rule the SQL form applies.
        var canSee = SourceVisibilityRule.Compile(userId, role);
        var result = _sources
            .Where(s => ids.Contains(s.Id) && canSee(s))
            .Select(s => new SourceAttribution(s.Id, s.Title, s.Visibility, s.CreatedByUserId))
            .ToList();

        return Task.FromResult<IReadOnlyList<SourceAttribution>>(result.AsReadOnly());
    }

    public Task<IReadOnlyList<Source>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        var result = _sources.Where(s => s.WorldId == worldId).ToList();
        return Task.FromResult<IReadOnlyList<Source>>(result.AsReadOnly());
    }

    public Task<IReadOnlyList<Source>> ListRecentSessionsAsync(Guid worldId, Guid userId, WorldRole role, int maxCount, CancellationToken cancellationToken = default)
    {
        // Mirrors SourceRepository: session-recording types (plus dated ImportedNotes),
        // gated by the shared source rule, newest first by OccurredAt ?? CreatedAt.
        SourceType[] sessionTypes = [SourceType.SessionNote, SourceType.Transcript, SourceType.SessionAudio];
        var canSee = SourceVisibilityRule.Compile(userId, role);

        var result = _sources
            .Where(s => s.WorldId == worldId
                && (sessionTypes.Contains(s.Type)
                    || (s.Type == SourceType.ImportedNote && s.OccurredAt is not null))
                && canSee(s))
            .OrderByDescending(s => s.OccurredAt ?? s.CreatedAt)
            .Take(maxCount)
            .ToList();

        return Task.FromResult<IReadOnlyList<Source>>(result.AsReadOnly());
    }

    public Task<IReadOnlyList<Source>> ListTimelineBeforeAsync(
        Guid worldId,
        Guid? campaignId,
        DateTimeOffset pivotOccurred,
        DateTimeOffset pivotCreated,
        VisibilityFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        // Mirrors SourceRepository: timeline types strictly before the pivot tuple
        // (effective date, then CreatedAt), campaign-scoped when set, nearest first.
        SourceType[] timelineTypes =
            [SourceType.SessionNote, SourceType.Transcript, SourceType.SessionAudio, SourceType.ImportedNote];

        var result = _sources
            .Where(s => s.WorldId == worldId
                && timelineTypes.Contains(s.Type)
                && (campaignId is null || s.CampaignId is null || s.CampaignId == campaignId)
                && ((s.OccurredAt ?? s.CreatedAt) < pivotOccurred
                    || ((s.OccurredAt ?? s.CreatedAt) == pivotOccurred && s.CreatedAt < pivotCreated))
                && filter.CanSee(s.Visibility, s.CreatedByUserId))
            .OrderByDescending(s => s.OccurredAt ?? s.CreatedAt)
            .ThenByDescending(s => s.CreatedAt)
            .Take(maxCount)
            .ToList();

        return Task.FromResult<IReadOnlyList<Source>>(result.AsReadOnly());
    }

    public Task<IReadOnlyList<Source>> ListExtractableAfterAsync(
        Guid worldId,
        DateTimeOffset pivotOccurred,
        DateTimeOffset pivotCreated,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var result = ExtractableAfter(worldId, pivotOccurred, pivotCreated)
            .Take(maxCount)
            .ToList();
        return Task.FromResult<IReadOnlyList<Source>>(result.AsReadOnly());
    }

    public Task<int> CountExtractableAfterAsync(
        Guid worldId,
        DateTimeOffset pivotOccurred,
        DateTimeOffset pivotCreated,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ExtractableAfter(worldId, pivotOccurred, pivotCreated).Count());
    }

    // Mirrors SourceRepository: every extractable source in a reprocessable state, strictly
    // after the pivot tuple, earliest first. No type filter — a replay walks the whole world.
    private IEnumerable<Source> ExtractableAfter(
        Guid worldId, DateTimeOffset pivotOccurred, DateTimeOffset pivotCreated)
    {
        return _sources
            .Where(s => s.WorldId == worldId
                && s.ExtractionEnabled
                && s.ProcessingStatus is SourceProcessingStatus.Processed or SourceProcessingStatus.Failed
                && ((s.OccurredAt ?? s.CreatedAt) > pivotOccurred
                    || ((s.OccurredAt ?? s.CreatedAt) == pivotOccurred && s.CreatedAt > pivotCreated)))
            .OrderBy(s => s.OccurredAt ?? s.CreatedAt)
            .ThenBy(s => s.CreatedAt);
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

    /// <summary>
    /// Simulates another worker winning the claim in the instant between this caller's read and
    /// its write — the race itself, which a single-threaded fake cannot otherwise stage.
    /// </summary>
    public bool StealNextExtractionClaim { get; set; }

    /// <summary>
    /// Enforces the same Queued-only condition the real UPDATE ... WHERE does, so a test cannot
    /// see a claim succeed here that the database would have rejected. The atomicity is not
    /// reproduced and does not need to be — the fake is single-threaded.
    /// </summary>
    public Task<bool> TryClaimForExtractionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (StealNextExtractionClaim)
        {
            StealNextExtractionClaim = false;
            var stolen = _sources.FirstOrDefault(s => s.Id == id);
            if (stolen is not null)
            {
                stolen.ProcessingStatus = SourceProcessingStatus.Processing;
            }
            return Task.FromResult(false);
        }

        var source = _sources.FirstOrDefault(s => s.Id == id);
        if (source is null || source.ProcessingStatus != SourceProcessingStatus.Queued)
        {
            return Task.FromResult(false);
        }

        _statusTransitions.Add((id, source.ProcessingStatus, SourceProcessingStatus.Processing));
        source.ProcessingStatus = SourceProcessingStatus.Processing;
        return Task.FromResult(true);
    }

    public Task UpdateVisibilityAsync(Guid id, VisibilityScope visibility, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.Id == id);
        if (source is not null)
        {
            source.Visibility = visibility;
        }
        return Task.CompletedTask;
    }

    public Task UpdateBodyAsync(Guid id, string body, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.Id == id)
            ?? throw new InvalidOperationException($"Source with id '{id}' not found.");
        source.Body = body;
        return Task.CompletedTask;
    }

    public Task UpdateDerivedTextAsync(Guid id, string? derivedText, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.Id == id)
            ?? throw new InvalidOperationException($"Source with id '{id}' not found.");
        source.DerivedText = derivedText;
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
