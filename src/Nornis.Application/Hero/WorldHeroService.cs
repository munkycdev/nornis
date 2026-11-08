using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Hero;

/// <summary>What the hero shows behind the world's name.</summary>
public enum HeroPictureKind
{
    None,
    KeyArt,
    Map,
}

/// <summary>
/// The hero's picture. Key art is addressed by the Web's own image route, so it comes with its
/// stamp (the cache-buster); a map comes with the URL the Map page already shows it by.
/// <see cref="FocusY"/> is the vertical position, 0 to 100, the band keeps when it crops.
/// </summary>
public sealed record HeroPicture(HeroPictureKind Kind, DateTimeOffset? KeyArtStamp, string? MapUrl, int FocusY)
{
    public static readonly HeroPicture None = new(HeroPictureKind.None, null, null, 50);
}

/// <summary>
/// The top of Home in one read (feature 42): the world's name and system, what the caller can see
/// of its play (dated sessions, the latest of them, codex entries), and the picture.
/// </summary>
public sealed record WorldHero(
    Guid WorldId,
    string Name,
    string? GameSystem,
    int SessionCount,
    DateTimeOffset? LastSessionAt,
    int EntryCount,
    HeroPicture Picture);

/// <summary>
/// Decides, once and on the server, what a world's hero shows: its key art, else the journey map
/// the caller can already see on the Map page, else nothing. The map comes from the Map page's own
/// read with the caller's own role, so the hero can never show a player a map the Map page would
/// not (correctness property 1). The counts are what the caller can see, through the same
/// visibility filters as the Sources and Codex lists.
/// </summary>
public class WorldHeroService
{
    /// <summary>The public site reads as an Observer with no user, as every <c>/api/public</c> read does.</summary>
    private static readonly Guid AnonymousUserId = Guid.Empty;

    private readonly IWorldRepository _worlds;
    private readonly ISourceRepository _sources;
    private readonly IArtifactService _artifacts;
    private readonly IJourneyMapService _journey;

    public WorldHeroService(
        IWorldRepository worlds, ISourceRepository sources, IArtifactService artifacts, IJourneyMapService journey)
    {
        _worlds = worlds;
        _sources = sources;
        _artifacts = artifacts;
        _journey = journey;
    }

    /// <summary>The hero for a member, as their role sees the world. The caller has established membership.</summary>
    public async Task<AppResult<WorldHero>> GetAsync(Guid worldId, Guid userId, WorldRole role, CancellationToken ct)
    {
        var world = await _worlds.GetByIdAsync(worldId, ct);
        return world is null
            ? AppResult<WorldHero>.Fail(new AppError(404, "not_found", "World not found."))
            : AppResult<WorldHero>.Success(await BuildAsync(world, userId, role, ct));
    }

    /// <summary>
    /// The hero for the public site, for a world the caller resolved by slug with public access on;
    /// it reads as an Observer, so only party-visible sessions, entries and maps count.
    /// </summary>
    public async Task<AppResult<WorldHero>> GetForPublicAsync(World world, CancellationToken ct) =>
        world.PublicAccessEnabled
            ? AppResult<WorldHero>.Success(await BuildAsync(world, AnonymousUserId, WorldRole.Observer, ct))
            : AppResult<WorldHero>.Fail(new AppError(404, "not_found", "World not found."));

    private async Task<WorldHero> BuildAsync(World world, Guid userId, WorldRole role, CancellationToken ct)
    {
        var sessions = (await _sources.ListSummariesByWorldAsync(world.Id, userId, role, cancellationToken: ct))
            .Where(s => s.Type == SourceType.SessionNote && s.OccurredAt is not null)
            .Select(s => s.OccurredAt!.Value)
            .ToList();
        var entries = await _artifacts.ListAsync(new ArtifactListQuery(world.Id, userId, role), ct);

        return new WorldHero(
            world.Id,
            world.Name,
            world.GameSystem,
            sessions.Count,
            sessions.Count > 0 ? sessions.Max() : null,
            entries.Value?.Count ?? 0,
            await PictureAsync(world, userId, role, ct));
    }

    private async Task<HeroPicture> PictureAsync(World world, Guid userId, WorldRole role, CancellationToken ct)
    {
        if (world.KeyArt is { } art)
        {
            return new HeroPicture(HeroPictureKind.KeyArt, art.UpdatedAt, null, FocusY(art.Anchor));
        }

        var journey = await _journey.GetJourneyAsync(world.Id, null, userId, role, ct);
        return journey.IsSuccess && !string.IsNullOrWhiteSpace(journey.Value!.ImageUrl)
            ? new HeroPicture(HeroPictureKind.Map, null, journey.Value.ImageUrl, 50)
            : HeroPicture.None;
    }

    /// <summary>The anchor as the CSS background position the band uses.</summary>
    public static int FocusY(KeyArtAnchor anchor) => anchor switch
    {
        KeyArtAnchor.Top => 0,
        KeyArtAnchor.Bottom => 100,
        _ => 50,
    };
}
