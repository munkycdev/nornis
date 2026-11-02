using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class BibliotaphConnectionRepository : IBibliotaphConnectionRepository
{
    private readonly NornisDbContext _context;

    public BibliotaphConnectionRepository(NornisDbContext context)
    {
        _context = context;
    }

    public Task<BibliotaphConnection?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _context.BibliotaphConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public async Task<BibliotaphConnection> UpsertAsync(
        Guid userId, string protectedRefreshToken, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var connection = await _context.BibliotaphConnections
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (connection is null)
        {
            connection = new BibliotaphConnection { Id = Guid.NewGuid(), UserId = userId };
            _context.BibliotaphConnections.Add(connection);
        }

        // A fresh grant is a fresh connection: new since, never used, Connected.
        connection.ProtectedRefreshToken = protectedRefreshToken;
        connection.Status = BibliotaphConnectionStatus.Connected;
        connection.ConnectedAt = now;
        connection.StatusChangedAt = now;
        connection.LastUsedAt = null;

        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(connection).State = EntityState.Detached;
        return connection;
    }

    public async Task MarkNeedsReconnectAsync(Guid connectionId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var connection = await _context.BibliotaphConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection is null || connection.Status == BibliotaphConnectionStatus.NeedsReconnect)
        {
            return;
        }

        connection.Status = BibliotaphConnectionStatus.NeedsReconnect;
        connection.StatusChangedAt = now;
        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(connection).State = EntityState.Detached;
    }

    public Task TouchLastUsedAsync(
        Guid connectionId, DateTimeOffset now, DateTimeOffset writeIfOlderThan, CancellationToken cancellationToken = default) =>
        _context.SetWhereAsync<BibliotaphConnection, DateTimeOffset?>(
            c => c.Id == connectionId && (c.LastUsedAt == null || c.LastUsedAt < writeIfOlderThan),
            c => c.LastUsedAt,
            now,
            cancellationToken);

    public Task DeleteAsync(Guid connectionId, CancellationToken cancellationToken = default) =>
        _context.DeleteWhereAsync<BibliotaphConnection>(c => c.Id == connectionId, cancellationToken);
}
