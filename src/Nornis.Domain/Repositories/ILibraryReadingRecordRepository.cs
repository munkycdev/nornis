using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

/// <summary>Who opened which Bibliotaph book (feature 31, Requirement 4).</summary>
public interface ILibraryReadingRecordRepository
{
    Task AddAsync(LibraryReadingRecord record, CancellationToken cancellationToken = default);

    /// <summary>The document's most recent opens, newest first, at most <paramref name="max"/>.</summary>
    Task<IReadOnlyList<LibraryReadingRecord>> ListRecentAsync(Guid documentId, int max, CancellationToken cancellationToken = default);

    /// <summary>Deletes up to <paramref name="max"/> records opened before <paramref name="before"/>; returns how many.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset before, int max, CancellationToken cancellationToken = default);
}
