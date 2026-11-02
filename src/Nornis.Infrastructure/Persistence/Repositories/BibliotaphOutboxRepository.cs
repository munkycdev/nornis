using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class BibliotaphOutboxRepository : IBibliotaphOutboxRepository
{
    private readonly NornisDbContext _context;

    public BibliotaphOutboxRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<int> QueueCreatedForWorldsOfAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var worlds = await _context.Worlds
            .AsNoTracking()
            .Where(w => w.CreatedByUserId == userId && !w.IsDemo && !w.IsTemplate)
            .OrderBy(w => w.CreatedAt)
            .Select(w => new { w.Id, w.Name })
            .ToListAsync(cancellationToken);

        foreach (var world in worlds)
        {
            _context.BibliotaphWorldEvents.Add(BibliotaphOutboxEvents.New(
                userId, world.Id, world.Name, BibliotaphWorldEventKind.Created, now));
        }

        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
        return worlds.Count;
    }

    public Task DeleteUndeliveredAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _context.DeleteWhereAsync<BibliotaphWorldEvent>(
            e => e.UserId == userId && e.DeliveredAt == null && e.FailedAt == null, cancellationToken);

    public async Task<IReadOnlyList<BibliotaphWorldEvent>> ListDueAsync(DateTimeOffset now, int max, CancellationToken cancellationToken = default) =>
        await _context.BibliotaphWorldEvents
            .AsNoTracking()
            .Where(e => e.DeliveredAt == null && e.FailedAt == null && e.NextAttemptAt <= now)
            .OrderBy(e => e.OccurredAt)
            .Take(max)
            .ToListAsync(cancellationToken);

    public Task MarkDeliveredAsync(IReadOnlyList<Guid> eventIds, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        UpdateAsync(eventIds, e =>
        {
            e.DeliveredAt = now;
            e.LastError = null;
        }, cancellationToken);

    public Task MarkAttemptFailedAsync(
        IReadOnlyList<Guid> eventIds, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken = default) =>
        UpdateAsync(eventIds, e =>
        {
            e.Attempts++;
            e.NextAttemptAt = nextAttemptAt;
            e.LastError = error.Length > 200 ? error[..200] : error;
        }, cancellationToken);

    public Task MarkFailedAsync(IReadOnlyList<Guid> eventIds, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        UpdateAsync(eventIds, e => e.FailedAt = now, cancellationToken);

    public async Task<int> PurgeSettledAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        var settled = _context.BibliotaphWorldEvents
            .Where(e => (e.DeliveredAt != null && e.DeliveredAt < before) || (e.FailedAt != null && e.FailedAt < before));

        if (_context.Database.IsRelational())
        {
            return await settled.ExecuteDeleteAsync(cancellationToken);
        }

        var rows = await settled.ToListAsync(cancellationToken);
        _context.BibliotaphWorldEvents.RemoveRange(rows);
        await _context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    private async Task UpdateAsync(IReadOnlyList<Guid> eventIds, Action<BibliotaphWorldEvent> change, CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0)
        {
            return;
        }

        var rows = await _context.BibliotaphWorldEvents
            .Where(e => eventIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            change(row);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }
}

/// <summary>How an outbox row is made, so the backfill, the capture and the delete paths agree.</summary>
internal static class BibliotaphOutboxEvents
{
    public static BibliotaphWorldEvent New(
        Guid userId, Guid worldId, string worldName, BibliotaphWorldEventKind kind, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorldId = worldId,
            Kind = kind,
            WorldName = worldName.Length > BibliotaphWorldEvent.MaxWorldNameLength
            ? worldName[..BibliotaphWorldEvent.MaxWorldNameLength]
            : worldName,
            OccurredAt = now,
            NextAttemptAt = now,
        };
}
