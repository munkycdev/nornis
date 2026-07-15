using Nornis.Application.Authorization;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class WorldMemberService : IWorldMemberService
{
    private readonly IWorldMemberRepository _memberRepository;
    private readonly IUserRepository _userRepository;

    public WorldMemberService(
        IWorldMemberRepository memberRepository,
        IUserRepository userRepository)
    {
        _memberRepository = memberRepository;
        _userRepository = userRepository;
    }

    public async Task<AppResult<WorldMember>> AddMemberAsync(AddMemberCommand command, CancellationToken ct)
    {
        // Validate role value
        if (!Enum.IsDefined(typeof(WorldRole), command.Role))
        {
            return AppResult<WorldMember>.Fail(
                new AppError(400, "invalid_role", "Role must be GM, Player, or Observer."));
        }

        // Verify acting user is a GM in this world
        var actingMember = await _memberRepository.GetByWorldAndUserAsync(command.WorldId, command.ActingUserId, ct);
        if (actingMember is null || actingMember.Role != WorldRole.GM)
        {
            return AppResult<WorldMember>.Fail(
                new AppError(403, "insufficient_role", "Only a GM can add members to a world."));
        }

        // Verify target user exists
        var targetUser = await _userRepository.GetByIdAsync(command.TargetUserId, ct);
        if (targetUser is null)
        {
            return AppResult<WorldMember>.Fail(
                new AppError(404, "not_found", "The specified user does not exist."));
        }

        // Check target is not already a member
        var existingMember = await _memberRepository.GetByWorldAndUserAsync(command.WorldId, command.TargetUserId, ct);
        if (existingMember is not null)
        {
            return AppResult<WorldMember>.Fail(
                new AppError(409, "conflict", "The user is already a member of this world."));
        }

        // Create the membership
        var member = new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            UserId = command.TargetUserId,
            Role = command.Role,
            JoinedAt = DateTimeOffset.UtcNow
        };

        var created = await _memberRepository.CreateAsync(member, ct);
        return AppResult<WorldMember>.Success(created);
    }

    public async Task<AppResult> RemoveMemberAsync(Guid worldId, Guid targetUserId, Guid actingUserId, CancellationToken ct)
    {
        // Verify acting user is a GM
        var actingMember = await _memberRepository.GetByWorldAndUserAsync(worldId, actingUserId, ct);
        if (actingMember is null || actingMember.Role != WorldRole.GM)
        {
            return AppResult.Fail(
                new AppError(403, "insufficient_role", "Only a GM can remove members from a world."));
        }

        // Find target member
        var targetMember = await _memberRepository.GetByWorldAndUserAsync(worldId, targetUserId, ct);
        if (targetMember is null)
        {
            return AppResult.Fail(
                new AppError(404, "not_found", "The specified member was not found in this world."));
        }

        // Last-GM protection: if removing a GM, check there's more than one
        if (targetMember.Role == WorldRole.GM)
        {
            var gmCount = await _memberRepository.CountByRoleAsync(worldId, WorldRole.GM, ct);
            if (gmCount <= 1)
            {
                return AppResult.Fail(
                    new AppError(409, "conflict", "Cannot remove the last GM. The world must retain at least one GM."));
            }
        }

        await _memberRepository.RemoveAsync(targetMember, ct);
        return AppResult.Success();
    }

    public async Task<AppResult<WorldMember>> UpdateRoleAsync(UpdateMemberRoleCommand command, CancellationToken ct)
    {
        // Validate new role value
        if (!Enum.IsDefined(typeof(WorldRole), command.NewRole))
        {
            return AppResult<WorldMember>.Fail(
                new AppError(400, "invalid_role", "Role must be GM, Player, or Observer."));
        }

        // Verify acting user is a GM
        var actingMember = await _memberRepository.GetByWorldAndUserAsync(command.WorldId, command.ActingUserId, ct);
        if (actingMember is null || actingMember.Role != WorldRole.GM)
        {
            return AppResult<WorldMember>.Fail(
                new AppError(403, "insufficient_role", "Only a GM can change member roles."));
        }

        // Find target member
        var targetMember = await _memberRepository.GetByWorldAndUserAsync(command.WorldId, command.TargetUserId, ct);
        if (targetMember is null)
        {
            return AppResult<WorldMember>.Fail(
                new AppError(404, "not_found", "The specified member was not found in this world."));
        }

        // Last-GM protection: if downgrading a GM, check there's more than one
        if (targetMember.Role == WorldRole.GM && command.NewRole != WorldRole.GM)
        {
            var gmCount = await _memberRepository.CountByRoleAsync(command.WorldId, WorldRole.GM, ct);
            if (gmCount <= 1)
            {
                return AppResult<WorldMember>.Fail(
                    new AppError(409, "conflict", "Cannot change the role of the last GM. The world must retain at least one GM."));
            }
        }

        // Update role
        targetMember.Role = command.NewRole;
        var updated = await _memberRepository.UpdateAsync(targetMember, ct);
        return AppResult<WorldMember>.Success(updated);
    }

    public async Task<AppResult<WorldMember>> UpdateDisplayNameAsync(Guid worldId, Guid actingUserId, string? displayName, CancellationToken ct)
    {
        var member = await _memberRepository.GetByWorldAndUserAsync(worldId, actingUserId, ct);
        if (member is null)
        {
            return AppResult<WorldMember>.Fail(
                new AppError(404, "not_found", "World membership not found."));
        }

        var trimmed = displayName?.Trim();
        if (trimmed is { Length: > 200 })
        {
            return AppResult<WorldMember>.Fail(
                new AppError(400, "validation_error", "Display name must be 200 characters or fewer."));
        }

        member.DisplayName = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        var updated = await _memberRepository.UpdateAsync(member, ct);
        return AppResult<WorldMember>.Success(updated);
    }

    public async Task<AppResult<IReadOnlyList<WorldMember>>> ListMembersAsync(Guid worldId, Guid requestingUserId, CancellationToken ct)
    {
        // Verify requesting user is a member
        var requestingMember = await _memberRepository.GetByWorldAndUserAsync(worldId, requestingUserId, ct);
        if (requestingMember is null)
        {
            return AppResult<IReadOnlyList<WorldMember>>.Fail(
                new AppError(403, "access_denied", "You must be a member of the world to view its members."));
        }

        // Return all members ordered by JoinedAt ascending
        var members = await _memberRepository.ListByWorldAsync(worldId, ct);
        var ordered = members.OrderBy(m => m.JoinedAt).ToList();
        return AppResult<IReadOnlyList<WorldMember>>.Success(ordered);
    }
}
