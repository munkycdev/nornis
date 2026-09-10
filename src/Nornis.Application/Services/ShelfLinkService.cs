using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Shelf links: minted and revoked by a GM for players without an account, opened by whoever
/// holds the code. Composes <see cref="ILibraryService"/> rather than the document repository
/// so the shelf a link opens is the Library page as a Player sees it — same list, same rule
/// about unfinished uploads, same download — and cannot drift from it.
/// </summary>
public class ShelfLinkService : IShelfLinkService
{
    /// <summary>What a link holder is, for every library rule: a player at the table.</summary>
    private const WorldRole LinkHolderRole = WorldRole.Player;

    private readonly IShelfLinkRepository _links;
    private readonly IPlayerRepository _players;
    private readonly ILibraryService _library;
    private readonly IInviteCodeGenerator _codes;

    public ShelfLinkService(
        IShelfLinkRepository links,
        IPlayerRepository players,
        ILibraryService library,
        IInviteCodeGenerator codes)
    {
        _links = links;
        _players = players;
        _library = library;
        _codes = codes;
    }

    public async Task<AppResult<ShelfLink>> CreateAsync(
        Guid worldId, Guid playerId, Guid actingUserId, WorldRole actingRole, CancellationToken ct)
    {
        if (RequireGm(actingRole) is { } forbidden)
        {
            return AppResult<ShelfLink>.Fail(forbidden);
        }

        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null || player.WorldId != worldId)
        {
            return AppResult<ShelfLink>.Fail(new AppError(404, "not_found", "Player not found in this world."));
        }

        if (player.WorldMemberId is not null)
        {
            return AppResult<ShelfLink>.Fail(new AppError(409, "player_linked",
                "This player is on Nornis and can open the Library directly."));
        }

        var now = DateTimeOffset.UtcNow;

        // One standing link per player: a second mint is a rotation, not an addition.
        var standing = await _links.GetActiveByPlayerAsync(playerId, ct);
        if (standing is not null)
        {
            await _links.RevokeAsync(standing.Id, now, ct);
        }

        var link = await _links.CreateAsync(new ShelfLink
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            Code = _codes.Generate(),
            CreatedByUserId = actingUserId,
            CreatedAt = now,
        }, ct);

        return AppResult<ShelfLink>.Success(link);
    }

    public async Task<AppResult<IReadOnlyList<ShelfLink>>> ListActiveAsync(Guid worldId, WorldRole actingRole, CancellationToken ct)
    {
        if (RequireGm(actingRole) is { } forbidden)
        {
            return AppResult<IReadOnlyList<ShelfLink>>.Fail(forbidden);
        }

        var links = await _links.ListActiveByWorldAsync(worldId, ct);
        return AppResult<IReadOnlyList<ShelfLink>>.Success(links);
    }

    public async Task<AppResult> RevokeAsync(Guid worldId, Guid linkId, WorldRole actingRole, CancellationToken ct)
    {
        if (RequireGm(actingRole) is { } forbidden)
        {
            return AppResult.Fail(forbidden);
        }

        var link = await _links.GetByIdAsync(linkId, ct);
        if (link is null || link.Player.WorldId != worldId)
        {
            return AppResult.Fail(new AppError(404, "not_found", "Shelf link not found in this world."));
        }

        if (link.IsActive)
        {
            await _links.RevokeAsync(link.Id, DateTimeOffset.UtcNow, ct);
        }

        return AppResult.Success();
    }

    public async Task<AppResult<Shelf>> OpenAsync(string code, CancellationToken ct)
    {
        var resolved = await ResolveAsync(code, ct);
        if (!resolved.IsSuccess)
        {
            return AppResult<Shelf>.Fail(resolved.Error!);
        }

        var link = resolved.Value!;
        var documents = await _library.ListAsync(link.Player.WorldId, LinkHolderRole, ct);
        if (!documents.IsSuccess)
        {
            return AppResult<Shelf>.Fail(documents.Error!);
        }

        // Links exist only for unlinked players, so the member is always null here; the
        // name still goes through the one rule rather than reading Player.Name directly.
        return AppResult<Shelf>.Success(new Shelf(
            link.Player.WorldId,
            link.Player.World.Name,
            PlayerDisplayName.For(link.Player, member: null),
            documents.Value!));
    }

    public async Task<AppResult<LibraryDownload>> DownloadAsync(string code, Guid documentId, CancellationToken ct)
    {
        var resolved = await ResolveAsync(code, ct);
        if (!resolved.IsSuccess)
        {
            return AppResult<LibraryDownload>.Fail(resolved.Error!);
        }

        return await _library.GetDownloadAsync(documentId, resolved.Value!.Player.WorldId, LinkHolderRole, ct);
    }

    /// <summary>
    /// The one path a code takes to a link. Unknown and revoked answer identically so the
    /// surface is not an oracle for which codes exist, and every successful open is stamped.
    /// </summary>
    private async Task<AppResult<ShelfLink>> ResolveAsync(string code, CancellationToken ct)
    {
        var link = string.IsNullOrWhiteSpace(code) ? null : await _links.GetByCodeAsync(code, ct);
        if (link is null || !link.IsActive)
        {
            return AppResult<ShelfLink>.Fail(new AppError(404, "not_found", "This link is not valid."));
        }

        await _links.TouchAsync(link.Id, DateTimeOffset.UtcNow, ct);
        return AppResult<ShelfLink>.Success(link);
    }

    private static AppError? RequireGm(WorldRole actingRole) =>
        actingRole != WorldRole.GM
            ? new AppError(403, "insufficient_role", "Only a GM can manage shelf links.")
            : null;
}
