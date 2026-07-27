using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

/// <summary>
/// Sessions and their items live behind one interface: an item is meaningless outside its
/// session, and every read wants the session with its items attached. Writes are scoped
/// rather than whole-graph saves so an item reorder never re-saves the session, and a
/// status change never re-saves the items.
/// </summary>
public interface IImportSessionRepository
{
    Task<ImportSession> CreateAsync(ImportSession session, CancellationToken cancellationToken = default);

    /// <summary>The session with its items, or null.</summary>
    Task<ImportSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The world's non-terminal (Draft or InProgress) session with its items, or
    /// null. At most one exists at a time.</summary>
    Task<ImportSession?> GetNonTerminalByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    /// <summary>Scoped write of the session's status and timestamp — the only way its status moves.</summary>
    Task UpdateAsync(Guid id, ImportSessionStatus status, DateTimeOffset updatedAt, CancellationToken cancellationToken = default);

    /// <summary>Bumps <see cref="ImportSession.UpdatedAt"/> alone. Deliberately not
    /// <see cref="UpdateAsync"/> with the caller's status: an item change that raced an abandon
    /// would otherwise write a stale status back and resurrect the session.</summary>
    Task TouchAsync(Guid id, DateTimeOffset updatedAt, CancellationToken cancellationToken = default);

    Task<ImportSessionItem> AddItemAsync(ImportSessionItem item, CancellationToken cancellationToken = default);

    /// <summary>Appends several items at once — staging a backlog of existing sources is one
    /// action to the GM and should be one round trip.</summary>
    Task AddItemsAsync(IReadOnlyList<ImportSessionItem> items, CancellationToken cancellationToken = default);

    /// <summary>Stamps the moment the walk sent this item for extraction. Until this is set
    /// the item reads as Waiting whatever its source's status says.</summary>
    Task SetItemDispatchedAsync(Guid itemId, DateTimeOffset dispatchedAt, CancellationToken cancellationToken = default);

    /// <summary>Removes the item row only. The source is never touched here — dropping a
    /// note from the run is a queue edit, not a deletion.</summary>
    Task DeleteItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Scoped write of item positions — the reorder.</summary>
    Task SetItemPositionsAsync(IReadOnlyList<(Guid ItemId, int Position)> positions, CancellationToken cancellationToken = default);

    Task SetItemSkippedAsync(Guid itemId, bool skipped, CancellationToken cancellationToken = default);
}
