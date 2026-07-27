using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

public interface ISourceRepository
{
    Task<Source> CreateAsync(Source source, CancellationToken cancellationToken = default);

    Task<Source?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Source>> ListByWorldAsync(Guid worldId, VisibilityScope? visibility = null, CancellationToken cancellationToken = default);

    /// <summary>The world's most recent play sessions (session-recording source types),
    /// visibility-filtered, ordered by when they happened (OccurredAt ?? CreatedAt) descending.</summary>
    Task<IReadOnlyList<Source>> ListRecentSessionsAsync(Guid worldId, VisibilityFilter filter, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>The timeline sources (session-recording types plus imported notes) that sit
    /// strictly before a pivot moment on the play timeline, nearest first. The pivot is the
    /// moment of the source being anchored — its OccurredAt ?? CreatedAt plus its CreatedAt as
    /// tiebreak — so a backfilled import looks back from its place in the story, never from
    /// "now". When <paramref name="campaignId"/> is set, sources from other campaigns are
    /// excluded (campaign-less sources still match: single-campaign worlds tag inconsistently).</summary>
    Task<IReadOnlyList<Source>> ListTimelineBeforeAsync(
        Guid worldId,
        Guid? campaignId,
        DateTimeOffset pivotOccurred,
        DateTimeOffset pivotCreated,
        VisibilityFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default);

    /// <summary>The replay queue: every source eligible for re-extraction (extraction
    /// enabled, Processed or Failed) strictly after a pivot moment, earliest first.
    /// World-wide and unfiltered by visibility — a replay is GM-driven and walks the whole
    /// record. Same pivot tuple convention as <see cref="ListTimelineBeforeAsync"/>.
    ///
    /// Deliberately NOT restricted to timeline types. A world's knowledge lives in its GM
    /// notes, lore documents, uploads and maps as much as in its session notes, and a
    /// re-extraction that silently skipped them left those sources empty in a way that
    /// looked like a bug. ExtractionEnabled is the switch for "this source does not get
    /// extracted" — the type is not. Undated sources order by CreatedAt, which for a bulk
    /// import is upload order.</summary>
    Task<IReadOnlyList<Source>> ListExtractableAfterAsync(
        Guid worldId,
        DateTimeOffset pivotOccurred,
        DateTimeOffset pivotCreated,
        int maxCount,
        CancellationToken cancellationToken = default);

    /// <summary>How many sources <see cref="ListExtractableAfterAsync"/> would return
    /// unbounded — the replay's "remaining" count for progress display.</summary>
    Task<int> CountExtractableAfterAsync(
        Guid worldId,
        DateTimeOffset pivotOccurred,
        DateTimeOffset pivotCreated,
        CancellationToken cancellationToken = default);

    /// <summary>Scoped Body write — used by the worker to persist a vision transcription.</summary>
    Task UpdateBodyAsync(Guid id, string body, CancellationToken cancellationToken = default);

    /// <summary>Scoped DerivedText write — the worker persists derived attachment text
    /// before extracting (so redelivery never re-buys it); the attachment service clears
    /// it (null) when derivation inputs change.</summary>
    Task UpdateDerivedTextAsync(Guid id, string? derivedText, CancellationToken cancellationToken = default);

    Task UpdateProcessingStatusAsync(Guid id, SourceProcessingStatus status, CancellationToken cancellationToken = default);

    /// <summary>Scoped Visibility write — the sanctioned reveal path lifts a GM-only source to
    /// PartyVisible without routing through the general update, which locks visibility after
    /// extraction.</summary>
    Task UpdateVisibilityAsync(Guid id, VisibilityScope visibility, CancellationToken cancellationToken = default);

    Task<Source> UpdateAsync(Source source, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
