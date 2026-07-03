using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;

namespace Nornis.Application.Services;

public interface ICampaignService
{
    Task<AppResult<Campaign>> CreateAsync(CreateCampaignCommand command, CancellationToken ct);
    Task<AppResult<Campaign>> GetByIdAsync(Guid campaignId, Guid requestingUserId, CancellationToken ct);
    Task<AppResult<Campaign>> UpdateAsync(UpdateCampaignCommand command, CancellationToken ct);
    Task<AppResult<IReadOnlyList<CampaignWithRoleDto>>> ListForUserAsync(Guid userId, CancellationToken ct);
}
