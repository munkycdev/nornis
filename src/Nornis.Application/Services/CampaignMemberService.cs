using Nornis.Application.Authorization;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class CampaignMemberService : ICampaignMemberService
{
    private readonly ICampaignMemberRepository _memberRepository;
    private readonly IUserRepository _userRepository;

    public CampaignMemberService(
        ICampaignMemberRepository memberRepository,
        IUserRepository userRepository)
    {
        _memberRepository = memberRepository;
        _userRepository = userRepository;
    }

    public async Task<AppResult<CampaignMember>> AddMemberAsync(AddMemberCommand command, CancellationToken ct)
    {
        // Validate role value
        if (!Enum.IsDefined(typeof(CampaignRole), command.Role))
        {
            return AppResult<CampaignMember>.Fail(
                new AppError(400, "invalid_role", "Role must be GM, Player, or Observer."));
        }

        // Verify acting user is a GM in this campaign
        var actingMember = await _memberRepository.GetByCampaignAndUserAsync(command.CampaignId, command.ActingUserId, ct);
        if (actingMember is null || actingMember.Role != CampaignRole.GM)
        {
            return AppResult<CampaignMember>.Fail(
                new AppError(403, "insufficient_role", "Only a GM can add members to a campaign."));
        }

        // Verify target user exists
        var targetUser = await _userRepository.GetByIdAsync(command.TargetUserId, ct);
        if (targetUser is null)
        {
            return AppResult<CampaignMember>.Fail(
                new AppError(404, "not_found", "The specified user does not exist."));
        }

        // Check target is not already a member
        var existingMember = await _memberRepository.GetByCampaignAndUserAsync(command.CampaignId, command.TargetUserId, ct);
        if (existingMember is not null)
        {
            return AppResult<CampaignMember>.Fail(
                new AppError(409, "conflict", "The user is already a member of this campaign."));
        }

        // Create the membership
        var member = new CampaignMember
        {
            Id = Guid.NewGuid(),
            CampaignId = command.CampaignId,
            UserId = command.TargetUserId,
            Role = command.Role,
            JoinedAt = DateTimeOffset.UtcNow
        };

        var created = await _memberRepository.CreateAsync(member, ct);
        return AppResult<CampaignMember>.Success(created);
    }

    public async Task<AppResult> RemoveMemberAsync(Guid campaignId, Guid targetUserId, Guid actingUserId, CancellationToken ct)
    {
        // Verify acting user is a GM
        var actingMember = await _memberRepository.GetByCampaignAndUserAsync(campaignId, actingUserId, ct);
        if (actingMember is null || actingMember.Role != CampaignRole.GM)
        {
            return AppResult.Fail(
                new AppError(403, "insufficient_role", "Only a GM can remove members from a campaign."));
        }

        // Find target member
        var targetMember = await _memberRepository.GetByCampaignAndUserAsync(campaignId, targetUserId, ct);
        if (targetMember is null)
        {
            return AppResult.Fail(
                new AppError(404, "not_found", "The specified member was not found in this campaign."));
        }

        // Last-GM protection: if removing a GM, check there's more than one
        if (targetMember.Role == CampaignRole.GM)
        {
            var gmCount = await _memberRepository.CountByRoleAsync(campaignId, CampaignRole.GM, ct);
            if (gmCount <= 1)
            {
                return AppResult.Fail(
                    new AppError(409, "conflict", "Cannot remove the last GM. The campaign must retain at least one GM."));
            }
        }

        await _memberRepository.RemoveAsync(targetMember, ct);
        return AppResult.Success();
    }

    public async Task<AppResult<CampaignMember>> UpdateRoleAsync(UpdateMemberRoleCommand command, CancellationToken ct)
    {
        // Validate new role value
        if (!Enum.IsDefined(typeof(CampaignRole), command.NewRole))
        {
            return AppResult<CampaignMember>.Fail(
                new AppError(400, "invalid_role", "Role must be GM, Player, or Observer."));
        }

        // Verify acting user is a GM
        var actingMember = await _memberRepository.GetByCampaignAndUserAsync(command.CampaignId, command.ActingUserId, ct);
        if (actingMember is null || actingMember.Role != CampaignRole.GM)
        {
            return AppResult<CampaignMember>.Fail(
                new AppError(403, "insufficient_role", "Only a GM can change member roles."));
        }

        // Find target member
        var targetMember = await _memberRepository.GetByCampaignAndUserAsync(command.CampaignId, command.TargetUserId, ct);
        if (targetMember is null)
        {
            return AppResult<CampaignMember>.Fail(
                new AppError(404, "not_found", "The specified member was not found in this campaign."));
        }

        // Last-GM protection: if downgrading a GM, check there's more than one
        if (targetMember.Role == CampaignRole.GM && command.NewRole != CampaignRole.GM)
        {
            var gmCount = await _memberRepository.CountByRoleAsync(command.CampaignId, CampaignRole.GM, ct);
            if (gmCount <= 1)
            {
                return AppResult<CampaignMember>.Fail(
                    new AppError(409, "conflict", "Cannot change the role of the last GM. The campaign must retain at least one GM."));
            }
        }

        // Update role
        targetMember.Role = command.NewRole;
        var updated = await _memberRepository.UpdateAsync(targetMember, ct);
        return AppResult<CampaignMember>.Success(updated);
    }

    public async Task<AppResult<IReadOnlyList<CampaignMember>>> ListMembersAsync(Guid campaignId, Guid requestingUserId, CancellationToken ct)
    {
        // Verify requesting user is a member
        var requestingMember = await _memberRepository.GetByCampaignAndUserAsync(campaignId, requestingUserId, ct);
        if (requestingMember is null)
        {
            return AppResult<IReadOnlyList<CampaignMember>>.Fail(
                new AppError(403, "access_denied", "You must be a member of the campaign to view its members."));
        }

        // Return all members ordered by JoinedAt ascending
        var members = await _memberRepository.ListByCampaignAsync(campaignId, ct);
        var ordered = members.OrderBy(m => m.JoinedAt).ToList();
        return AppResult<IReadOnlyList<CampaignMember>>.Success(ordered);
    }
}
