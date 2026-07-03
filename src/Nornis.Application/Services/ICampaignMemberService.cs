using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;

namespace Nornis.Application.Services;

public interface ICampaignMemberService
{
    Task<AppResult<CampaignMember>> AddMemberAsync(AddMemberCommand command, CancellationToken ct);
    Task<AppResult> RemoveMemberAsync(Guid campaignId, Guid targetUserId, Guid actingUserId, CancellationToken ct);
    Task<AppResult<CampaignMember>> UpdateRoleAsync(UpdateMemberRoleCommand command, CancellationToken ct);
    Task<AppResult<IReadOnlyList<CampaignMember>>> ListMembersAsync(Guid campaignId, Guid requestingUserId, CancellationToken ct);
}
