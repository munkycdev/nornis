using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// The key-art writes over <see cref="InMemoryWorldRepository"/>'s worlds, with the real
/// repository's contract: the month resets on a new month, the cap is a hard stop, and a give-back
/// only touches the month it was counted in. The atomicity is SQL's, tested in Infrastructure.
/// </summary>
public sealed class InMemoryWorldKeyArtRepository : IWorldKeyArtRepository
{
    private readonly InMemoryWorldRepository _worlds;

    public InMemoryWorldKeyArtRepository(InMemoryWorldRepository worlds)
    {
        _worlds = worlds;
    }

    private Nornis.Domain.Entities.World World(Guid id) => _worlds.Worlds.Single(w => w.Id == id);

    public Task SetKeyArtAsync(Guid worldId, WorldKeyArt? keyArt, CancellationToken cancellationToken = default)
    {
        World(worldId).KeyArt = keyArt;
        return Task.CompletedTask;
    }

    public Task SaveKeyArtWorkAsync(Guid worldId, string? brief, IReadOnlyList<Guid> optionIds, CancellationToken cancellationToken = default)
    {
        var world = World(worldId);
        world.KeyArtBrief = brief;
        world.KeyArtOptionIds = [.. optionIds];
        return Task.CompletedTask;
    }

    public Task<bool> TryCountKeyArtImagesAsync(Guid worldId, string month, int count, int cap, CancellationToken cancellationToken = default)
    {
        var world = World(worldId);
        var spent = world.KeyArtImagesMonth == month ? world.KeyArtImagesGenerated : 0;
        if (spent + count > cap)
        {
            return Task.FromResult(false);
        }

        world.KeyArtImagesMonth = month;
        world.KeyArtImagesGenerated = spent + count;
        return Task.FromResult(true);
    }

    public Task ReleaseKeyArtImagesAsync(Guid worldId, string month, int count, CancellationToken cancellationToken = default)
    {
        var world = World(worldId);
        if (world.KeyArtImagesMonth == month)
        {
            world.KeyArtImagesGenerated = Math.Max(0, world.KeyArtImagesGenerated - count);
        }

        return Task.CompletedTask;
    }
}
