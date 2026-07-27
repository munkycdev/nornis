using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class PushSubscriptionRepository : IPushSubscriptionRepository
{
    private readonly NornisDbContext _context;

    public PushSubscriptionRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<PushSubscription> UpsertAsync(
        PushSubscription subscription, CancellationToken cancellationToken = default)
    {
        var existing = await _context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == subscription.Endpoint, cancellationToken);

        if (existing is null)
        {
            _context.PushSubscriptions.Add(subscription);
            await _context.SaveChangesAsync(cancellationToken);
            return subscription;
        }

        // Same browser, new keys — and possibly a different user, if two people share a machine
        // and the second one subscribes. Whoever subscribed last owns the endpoint.
        existing.UserId = subscription.UserId;
        existing.P256dh = subscription.P256dh;
        existing.Auth = subscription.Auth;
        existing.Label = subscription.Label;
        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<IReadOnlyList<PushSubscription>> ListByUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.PushSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PushSubscription>> ListByUsersAsync(
        IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        return await _context.PushSubscriptions
            .AsNoTracking()
            .Where(s => userIds.Contains(s.UserId))
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteByEndpointAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var existing = await _context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);
        if (existing is null)
        {
            return;
        }

        _context.PushSubscriptions.Remove(existing);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task TouchAsync(Guid id, DateTimeOffset succeededAt, CancellationToken cancellationToken = default)
    {
        var existing = await _context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (existing is null)
        {
            return;
        }

        existing.LastSucceededAt = succeededAt;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
