using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

/// <summary>The Bibliotaph world-event outbox (feature 31, Requirement 2).</summary>
public interface IBibliotaphOutboxRepository
{
    /// <summary>
    /// Queues a <c>created</c> event for every world the user created, never a demo or template
    /// world, in one save: the backfill on connect and the Resync. Returns how many were queued.
    /// </summary>
    Task<int> QueueCreatedForWorldsOfAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Drops the user's undelivered events, on disconnect.</summary>
    Task DeleteUndeliveredAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Undelivered, unfailed events due by <paramref name="now"/>, oldest first, at most <paramref name="max"/>.</summary>
    Task<IReadOnlyList<BibliotaphWorldEvent>> ListDueAsync(DateTimeOffset now, int max, CancellationToken cancellationToken = default);

    Task MarkDeliveredAsync(IReadOnlyList<Guid> eventIds, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Records a failed attempt: the attempt count, when to try next, and why (a code, never a body).</summary>
    Task MarkAttemptFailedAsync(
        IReadOnlyList<Guid> eventIds, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(IReadOnlyList<Guid> eventIds, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Deletes delivered and failed events settled before <paramref name="before"/>. Returns how many.</summary>
    Task<int> PurgeSettledAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}
