using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class PlayerService : IPlayerService
{
    private readonly IPlayerRepository _players;
    private readonly IWorldMemberRepository _members;
    private readonly ICharacterRepository _characters;

    public PlayerService(IPlayerRepository players, IWorldMemberRepository members, ICharacterRepository characters)
    {
        _players = players;
        _members = members;
        _characters = characters;
    }

    public async Task<AppResult<IReadOnlyList<PlayerView>>> ListByWorldAsync(Guid worldId, CancellationToken ct)
    {
        var players = await _players.ListByWorldAsync(worldId, ct);
        var members = await _members.ListByWorldAsync(worldId, ct);

        var views = players
            .Select(p => ToView(p, members))
            .OrderBy(v => v.Role == WorldRole.GM ? 0 : 1)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return AppResult<IReadOnlyList<PlayerView>>.Success(views);
    }

    public async Task<AppResult<PlayerView>> GetMineAsync(Guid worldId, Guid actingUserId, CancellationToken ct)
    {
        var member = await _members.GetByWorldAndUserAsync(worldId, actingUserId, ct);
        if (member is null)
        {
            return AppResult<PlayerView>.Fail(NotAMember());
        }

        var player = await OwnPlayerAsync(member, ct);
        return AppResult<PlayerView>.Success(ToView(player, [member]));
    }

    public async Task<AppResult<Player>> CreateAsync(Guid worldId, string name, WorldRole actingRole, CancellationToken ct)
    {
        if (RequireGm(actingRole) is { } forbidden)
        {
            return AppResult<Player>.Fail(forbidden);
        }

        if (ValidateName(name) is { } invalid)
        {
            return AppResult<Player>.Fail(invalid);
        }

        var now = DateTimeOffset.UtcNow;
        var player = await _players.CreateAsync(new Player
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Name = name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        }, ct);

        return AppResult<Player>.Success(player);
    }

    public async Task<AppResult<Player>> RenameAsync(Guid playerId, Guid worldId, string name, WorldRole actingRole, CancellationToken ct)
    {
        if (RequireGm(actingRole) is { } forbidden)
        {
            return AppResult<Player>.Fail(forbidden);
        }

        if (ValidateName(name) is { } invalid)
        {
            return AppResult<Player>.Fail(invalid);
        }

        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null || player.WorldId != worldId)
        {
            return AppResult<Player>.Fail(NotFound());
        }

        if (player.WorldMemberId is not null)
        {
            return AppResult<Player>.Fail(new AppError(409, "player_linked",
                "A member's player is named by the member. Change the display name instead."));
        }

        player.Name = name.Trim();
        player.UpdatedAt = DateTimeOffset.UtcNow;
        player = await _players.UpdateAsync(player, ct);

        return AppResult<Player>.Success(player);
    }

    public async Task<AppResult> DeleteAsync(Guid playerId, Guid worldId, WorldRole actingRole, CancellationToken ct)
    {
        if (RequireGm(actingRole) is { } forbidden)
        {
            return AppResult.Fail(forbidden);
        }

        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null || player.WorldId != worldId)
        {
            return AppResult.Fail(NotFound());
        }

        if (player.WorldMemberId is not null)
        {
            return AppResult.Fail(new AppError(409, "player_linked",
                "A member's player goes when the member does. Remove the member instead."));
        }

        var characters = (await _characters.ListByWorldAsync(worldId, ct)).Where(c => c.PlayerId == playerId).ToList();
        if (characters.Count > 0)
        {
            var names = string.Join(", ", characters.Select(c => c.Name).OrderBy(n => n));
            return AppResult.Fail(new AppError(409, "player_has_characters",
                $"{player.Name} still plays {names}. Move or delete the characters first."));
        }

        await _players.DeleteAsync(playerId, ct);
        return AppResult.Success();
    }

    public async Task<AppResult<Player>> ClaimAsync(Guid playerId, Guid worldId, Guid actingUserId, WorldRole actingRole, CancellationToken ct)
    {
        if (actingRole == WorldRole.Observer)
        {
            return AppResult<Player>.Fail(new AppError(403, "insufficient_role", "Observers cannot claim players."));
        }

        var member = await _members.GetByWorldAndUserAsync(worldId, actingUserId, ct);
        if (member is null)
        {
            return AppResult<Player>.Fail(NotAMember());
        }

        return await MergeIntoMemberAsync(playerId, worldId, member, ct);
    }

    public async Task<AppResult<Player>> LinkAsync(Guid playerId, Guid worldId, Guid worldMemberId, WorldRole actingRole, CancellationToken ct)
    {
        if (RequireGm(actingRole) is { } forbidden)
        {
            return AppResult<Player>.Fail(forbidden);
        }

        var members = await _members.ListByWorldAsync(worldId, ct);
        var member = members.FirstOrDefault(m => m.Id == worldMemberId);
        if (member is null)
        {
            return AppResult<Player>.Fail(new AppError(400, "invalid_member", "The target member does not belong to this world."));
        }

        return await MergeIntoMemberAsync(playerId, worldId, member, ct);
    }

    /// <summary>
    /// The one operation behind claiming and linking: the unlinked player's characters move to
    /// the member's own player, and the unlinked player is deleted. Nothing about the
    /// characters changes but whose they are. Returns the member's player.
    /// </summary>
    private async Task<AppResult<Player>> MergeIntoMemberAsync(Guid playerId, Guid worldId, WorldMember member, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null || player.WorldId != worldId)
        {
            return AppResult<Player>.Fail(NotFound());
        }

        var own = await OwnPlayerAsync(member, ct);

        if (player.Id == own.Id)
        {
            return AppResult<Player>.Success(own);
        }

        if (player.WorldMemberId is not null)
        {
            return AppResult<Player>.Fail(new AppError(409, "player_linked",
                "That player is already someone on Nornis."));
        }

        await _players.MoveCharactersAndDeleteAsync(player.Id, own.Id, ct);

        return AppResult<Player>.Success(own);
    }

    private Task<Player> OwnPlayerAsync(WorldMember member, CancellationToken ct) =>
        _players.GetOrCreateByMemberAsync(member, ct);

    private static PlayerView ToView(Player player, IReadOnlyList<WorldMember> members)
    {
        var member = player.WorldMemberId is { } id ? members.FirstOrDefault(m => m.Id == id) : null;
        return new PlayerView(
            Id: player.Id,
            WorldId: player.WorldId,
            Name: PlayerDisplayName.For(player, member),
            WorldMemberId: member?.Id,
            Role: member?.Role);
    }

    private static AppError? RequireGm(WorldRole role) =>
        role != WorldRole.GM ? new AppError(403, "insufficient_role", "Only a GM can manage the table's players.") : null;

    private static AppError? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new AppError(400, "validation_error", "A player needs a name.");
        }

        if (name.Trim().Length > Player.MaxNameChars)
        {
            return new AppError(400, "validation_error", $"A player's name must be {Player.MaxNameChars} characters or fewer.");
        }

        return null;
    }

    private static AppError NotFound() => new(404, "not_found", "Player not found.");

    private static AppError NotAMember() => new(404, "not_found", "World membership not found.");
}
