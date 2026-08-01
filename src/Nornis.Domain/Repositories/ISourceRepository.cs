using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

public interface ISourceRepository
{
    Task<Source> CreateAsync(Source source, CancellationToken cancellationToken = default);

    Task<Source?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Source>> ListByWorldAsync(Guid worldId, VisibilityScope? visibility = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// The world's sources as a list shows them — visibility applied in SQL, newest first, and
    /// without the unbounded <c>Body</c>/<c>DerivedText</c> columns no list view reads. See
    /// <see cref="SourceListItem"/>.
    /// </summary>
    /// <param name="campaignId">When set, only sources in that campaign.</param>
    /// <param name="unassignedOnly">When true, only sources with no campaign. Ignored if
    /// <paramref name="campaignId"/> is set.</param>
    Task<IReadOnlyList<SourceListItem>> ListSummariesByWorldAsync(
        Guid worldId,
        Guid requestingUserId,
        WorldRole role,
        Guid? campaignId = null,
        bool unassignedOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the world has any source created strictly after <paramref name="after"/>,
    /// optionally restricted to one processing status. Backs the onboarding checklist, which is
    /// polled every 15 seconds while a new user works through it — the previous shape loaded
    /// every source in the world (bodies included) to answer a boolean.
    ///
    /// Unfiltered by visibility on purpose: the checklist reports on the tutorial world's own
    /// progress, not on what any particular reader may see.
    /// </summary>
    Task<bool> AnyCreatedAfterAsync(
        Guid worldId,
        DateTimeOffset after,
        SourceProcessingStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many sources sit in each processing status, counted in SQL for the caller's
    /// visibility. Backs the nav activity badge, which is polled continuously from every open
    /// tab: the previous shape loaded every source row in the world — <c>Body</c>, transcripts
    /// and all — to group them in memory and return a handful of integers.
    ///
    /// Statuses with no rows are absent from the result rather than present as zero.
    /// </summary>
    Task<IReadOnlyDictionary<SourceProcessingStatus, int>> CountByStatusAsync(
        Guid worldId,
        Guid requestingUserId,
        WorldRole role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Just enough of each visible source to name it. Use this instead of looping
    /// <see cref="GetByIdAsync"/> when displaying provenance: it is one round trip
    /// rather than one per source, and it leaves <c>Body</c>/<c>DerivedText</c> in the database
    /// instead of dragging every cited transcript across the wire to read a title.
    /// Visibility is <see cref="SourceVisibilityRule"/>, applied in SQL — rows the reader may
    /// not see are absent from the result, exactly like ids that no longer exist.
    /// </summary>
    Task<IReadOnlyList<SourceAttribution>> ListAttributionByIdsAsync(
        IReadOnlyList<Guid> ids,
        Guid userId,
        WorldRole role,
        CancellationToken cancellationToken = default);

    /// <summary>The world's most recent play sessions (session-recording source types),
    /// ordered by when they happened (OccurredAt ?? CreatedAt) descending. Visibility is
    /// <see cref="SourceVisibilityRule"/> — Draft gate and anonymous-identity guard included,
    /// because these rows' full text feeds the Ask context.</summary>
    Task<IReadOnlyList<Source>> ListRecentSessionsAsync(Guid worldId, Guid userId, WorldRole role, int maxCount, CancellationToken cancellationToken = default);

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
