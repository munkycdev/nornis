using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IPushSubscriptionRepository
{
    /// <summary>Stores a browser's subscription, replacing any existing row for the same
    /// endpoint — a re-subscribe rotates the encryption keys and must not leave a stale
    /// duplicate that every send then fails against.</summary>
    Task<PushSubscription> UpsertAsync(PushSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>Every browser this user has enabled notifications on.</summary>
    Task<IReadOnlyList<PushSubscription>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Subscriptions for several users in one read — a world's GMs, say.</summary>
    Task<IReadOnlyList<PushSubscription>> ListByUsersAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>Forgets one browser, by the endpoint it identifies itself with. Used both when
    /// a user turns notifications off and when the push service reports the endpoint gone.</summary>
    Task DeleteByEndpointAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>Records that a send to this endpoint worked.</summary>
    Task TouchAsync(Guid id, DateTimeOffset succeededAt, CancellationToken cancellationToken = default);
}
