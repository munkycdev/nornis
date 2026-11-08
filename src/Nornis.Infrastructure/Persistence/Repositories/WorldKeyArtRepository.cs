using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class WorldKeyArtRepository : IWorldKeyArtRepository
{
    private readonly NornisDbContext _context;

    public WorldKeyArtRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task SetKeyArtAsync(Guid worldId, WorldKeyArt? keyArt, CancellationToken cancellationToken = default)
    {
        var world = await _context.LoadForUpdateAsync<World>(worldId, cancellationToken);

        // A copy, so the caller's instance never becomes this row's tracked owned entity.
        world.KeyArt = keyArt is null ? null : keyArt with { };
        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(world).State = EntityState.Detached;
    }

    public async Task SaveKeyArtWorkAsync(
        Guid worldId, string? brief, IReadOnlyList<Guid> optionIds, CancellationToken cancellationToken = default)
    {
        var world = await _context.LoadForUpdateAsync<World>(worldId, cancellationToken);
        world.KeyArtBrief = brief;
        world.KeyArtOptionIds = [.. optionIds];
        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(world).State = EntityState.Detached;
    }

    public async Task<bool> TryCountKeyArtImagesAsync(
        Guid worldId, string month, int count, int cap, CancellationToken cancellationToken = default)
    {
        if (count > cap)
        {
            return false;
        }

        // One conditional UPDATE: the month check and the cap check are the WHERE, and the reset
        // for a new month is the SET, so two generations at once can't both pass the cap and a
        // new month can't be counted on top of the old one.
        if (_context.Database.IsRelational())
        {
            var updated = await _context.Worlds
                .Where(w => w.Id == worldId
                            && (w.KeyArtImagesMonth != month || w.KeyArtImagesMonth == null || w.KeyArtImagesGenerated + count <= cap))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(w => w.KeyArtImagesGenerated, w => w.KeyArtImagesMonth == month ? w.KeyArtImagesGenerated + count : count)
                    .SetProperty(w => w.KeyArtImagesMonth, month), cancellationToken);
            return updated == 1;
        }

        // InMemory (API integration tests) has no ExecuteUpdate. Single-threaded there, so a
        // read-modify-write keeps the observable contract without the atomicity.
        var world = await _context.LoadForUpdateAsync<World>(worldId, cancellationToken);
        var spent = world.KeyArtImagesMonth == month ? world.KeyArtImagesGenerated : 0;
        var fits = spent + count <= cap;
        if (fits)
        {
            world.KeyArtImagesMonth = month;
            world.KeyArtImagesGenerated = spent + count;
            await _context.SaveChangesAsync(cancellationToken);
        }

        _context.Entry(world).State = EntityState.Detached;
        return fits;
    }

    public async Task ReleaseKeyArtImagesAsync(Guid worldId, string month, int count, CancellationToken cancellationToken = default)
    {
        // Only within the month they were counted in: a call that straddles midnight on the last
        // day must not take images off the new month's count.
        if (_context.Database.IsRelational())
        {
            await _context.Worlds
                .Where(w => w.Id == worldId && w.KeyArtImagesMonth == month)
                .ExecuteUpdateAsync(set => set.SetProperty(
                    w => w.KeyArtImagesGenerated, w => w.KeyArtImagesGenerated < count ? 0 : w.KeyArtImagesGenerated - count), cancellationToken);
            return;
        }

        var world = await _context.LoadForUpdateAsync<World>(worldId, cancellationToken);
        if (world.KeyArtImagesMonth == month)
        {
            world.KeyArtImagesGenerated = Math.Max(0, world.KeyArtImagesGenerated - count);
            await _context.SaveChangesAsync(cancellationToken);
        }

        _context.Entry(world).State = EntityState.Detached;
    }
}
