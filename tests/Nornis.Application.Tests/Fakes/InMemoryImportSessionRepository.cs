using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryImportSessionRepository : IImportSessionRepository
{
    private readonly List<ImportSession> _sessions = [];
    private readonly List<ImportSessionItem> _items = [];

    public IReadOnlyList<ImportSession> Sessions => _sessions.AsReadOnly();

    public IReadOnlyList<ImportSessionItem> Items => _items.AsReadOnly();

    public void Seed(params ImportSession[] sessions) => _sessions.AddRange(sessions);

    public void SeedItems(params ImportSessionItem[] items) => _items.AddRange(items);

    public Task<ImportSession> CreateAsync(ImportSession session, CancellationToken cancellationToken = default)
    {
        _sessions.Add(session);
        return Task.FromResult(session);
    }

    public Task<ImportSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Hydrate(_sessions.FirstOrDefault(s => s.Id == id)));
    }

    public Task<ImportSession?> GetNonTerminalByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        var session = _sessions.FirstOrDefault(
            s => s.WorldId == worldId
                && s.Status is ImportSessionStatus.Draft or ImportSessionStatus.InProgress);
        return Task.FromResult(Hydrate(session));
    }

    public Task UpdateAsync(
        Guid id, ImportSessionStatus status, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        var session = RequiredSession(id);
        session.Status = status;
        session.UpdatedAt = updatedAt;
        return Task.CompletedTask;
    }

    public Task TouchAsync(Guid id, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        RequiredSession(id).UpdatedAt = updatedAt;
        return Task.CompletedTask;
    }

    /// <summary>
    /// The scoped writers throw on a missing row because the real repository does — see the
    /// missing-row contract on <see cref="IImportSessionRepository"/>. A fake that quietly
    /// no-ops where production throws is how a service passes its tests and fails in the world.
    /// </summary>
    private ImportSession RequiredSession(Guid id) =>
        _sessions.FirstOrDefault(s => s.Id == id)
            ?? throw new InvalidOperationException($"ImportSession with id '{id}' not found.");

    private ImportSessionItem RequiredItem(Guid itemId) =>
        _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"ImportSessionItem with id '{itemId}' not found.");

    public Task<ImportSessionItem> AddItemAsync(ImportSessionItem item, CancellationToken cancellationToken = default)
    {
        _items.Add(item);
        return Task.FromResult(item);
    }

    public Task AddItemsAsync(IReadOnlyList<ImportSessionItem> items, CancellationToken cancellationToken = default)
    {
        _items.AddRange(items);
        return Task.CompletedTask;
    }

    public Task SetItemDispatchedAsync(
        Guid itemId, DateTimeOffset dispatchedAt, CancellationToken cancellationToken = default)
    {
        RequiredItem(itemId).DispatchedAt = dispatchedAt;
        return Task.CompletedTask;
    }

    public Task DeleteItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        // Mirrors the EF repository: the item row goes, the source is untouched.
        _items.RemoveAll(i => i.Id == itemId);
        return Task.CompletedTask;
    }

    public Task SetItemPositionsAsync(
        IReadOnlyList<(Guid ItemId, int Position)> positions, CancellationToken cancellationToken = default)
    {
        foreach (var (itemId, position) in positions)
        {
            var item = _items.FirstOrDefault(i => i.Id == itemId);
            if (item is not null)
            {
                item.Position = position;
            }
        }
        return Task.CompletedTask;
    }

    public Task SetItemSkippedAsync(Guid itemId, bool skipped, CancellationToken cancellationToken = default)
    {
        RequiredItem(itemId).Skipped = skipped;
        return Task.CompletedTask;
    }

    // The EF repository Includes the items on every read; a fresh list each time also keeps
    // callers from mutating the store through the navigation property.
    private ImportSession? Hydrate(ImportSession? session)
    {
        if (session is null)
        {
            return null;
        }

        session.Items = _items.Where(i => i.ImportSessionId == session.Id).ToList();
        return session;
    }
}
