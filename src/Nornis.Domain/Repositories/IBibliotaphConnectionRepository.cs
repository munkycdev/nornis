using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IBibliotaphConnectionRepository
{
    /// <summary>Null when the user hasn't connected.</summary>
    Task<BibliotaphConnection?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Stores the grant as the user's one connection, replacing any earlier one, as Connected.</summary>
    Task<BibliotaphConnection> UpsertAsync(
        Guid userId, string protectedRefreshToken, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Marks the connection Needs reconnect. Does nothing if it is gone or already marked.</summary>
    Task MarkNeedsReconnectAsync(Guid connectionId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Writes the last-used time when the stored one is older than <paramref name="writeIfOlderThan"/>, or null.</summary>
    Task TouchLastUsedAsync(
        Guid connectionId, DateTimeOffset now, DateTimeOffset writeIfOlderThan, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid connectionId, CancellationToken cancellationToken = default);
}
