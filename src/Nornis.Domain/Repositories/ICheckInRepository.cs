using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

/// <summary>
/// Check-ins and their questions (feature 44). A check-in is written whole, with its questions;
/// afterwards only its questions change (answers) and its <c>ClosedAt</c> (a later check-in).
/// Missing-row contract: the scoped writes throw when the row is gone.
/// </summary>
public interface ICheckInRepository
{
    /// <summary>Writes the check-in and every question on it in one save.</summary>
    Task<CheckIn> CreateAsync(CheckIn checkIn, CancellationToken cancellationToken = default);

    /// <summary>The world's open check-in with its questions, or null when none is open.</summary>
    Task<CheckIn?> GetOpenByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    /// <summary>The world's newest check-ins with their questions, open or closed, newest first.</summary>
    Task<IReadOnlyList<CheckIn>> ListRecentByWorldAsync(Guid worldId, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>A question with its check-in loaded, or null.</summary>
    Task<CheckInQuestion?> GetQuestionAsync(Guid questionId, CancellationToken cancellationToken = default);

    Task UpdateQuestionAsync(CheckInQuestion question, CancellationToken cancellationToken = default);

    /// <summary>Stamps <c>ClosedAt</c> on the check-in. Throws when the row is gone.</summary>
    Task CloseAsync(Guid checkInId, DateTimeOffset closedAt, CancellationToken cancellationToken = default);
}
