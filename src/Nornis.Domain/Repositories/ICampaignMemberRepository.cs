using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface ICampaignMemberRepository
{
    Task<CampaignMember> CreateAsync(CampaignMember member, CancellationToken cancellationToken = default);

    Task<CampaignMember?> GetByCampaignAndUserAsync(Guid campaignId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CampaignMember>> ListByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task RemoveAsync(CampaignMember member, CancellationToken cancellationToken = default);

    Task<CampaignMember> UpdateAsync(CampaignMember member, CancellationToken cancellationToken = default);

    Task<int> CountByRoleAsync(Guid campaignId, CampaignRole role, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CampaignMember>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
