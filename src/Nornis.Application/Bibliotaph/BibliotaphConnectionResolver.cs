using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Bibliotaph;

/// <summary>
/// Whose Bibliotaph a world uses: its creator's. <see cref="Connection"/> is null when the
/// creator hasn't connected; the record itself is null when the integration is off, the world is
/// gone, or it is a demo or template world, none of which ever reads Bibliotaph.
/// </summary>
public sealed record BibliotaphWorldOwner(Guid WorldId, Guid UserId, string DisplayName, BibliotaphConnection? Connection);

public interface IBibliotaphConnectionResolver
{
    Task<BibliotaphWorldOwner?> ForWorldAsync(Guid worldId, CancellationToken ct);
}

/// <summary>
/// The one place that decides whose Bibliotaph a world reads (feature 31, design.md, check 1).
/// The Library sync, the retriever, covers, reading tickets and excerpts all ask it. In a world
/// with several GMs only the creator's collection is used.
/// </summary>
public sealed class BibliotaphConnectionResolver : IBibliotaphConnectionResolver
{
    private readonly IWorldRepository _worlds;
    private readonly IUserRepository _users;
    private readonly IBibliotaphConnectionRepository _connections;
    private readonly BibliotaphOptions _options;

    public BibliotaphConnectionResolver(
        IWorldRepository worlds,
        IUserRepository users,
        IBibliotaphConnectionRepository connections,
        IOptions<BibliotaphOptions> options)
    {
        _worlds = worlds;
        _users = users;
        _connections = connections;
        _options = options.Value;
    }

    public async Task<BibliotaphWorldOwner?> ForWorldAsync(Guid worldId, CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            return null;
        }

        var world = await _worlds.GetByIdAsync(worldId, ct);
        if (world is null || world.IsDemo || world.IsTemplate)
        {
            return null;
        }

        var connection = await _connections.GetByUserIdAsync(world.CreatedByUserId, ct);
        var creator = connection is null ? null : await _users.GetByIdAsync(world.CreatedByUserId, ct);
        return new BibliotaphWorldOwner(worldId, world.CreatedByUserId, creator?.Username ?? string.Empty, connection);
    }
}
