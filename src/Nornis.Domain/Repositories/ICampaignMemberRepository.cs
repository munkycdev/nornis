using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface ICampaignMemberRepository
{
    Task<CampaignMember> CreateAsync(CampaignMember member, CancellationToken cancellationToken = default);

    Task<CampaignMember?> GetByCampaignAndUserAsync(Guid campaignId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CampaignMember>> ListByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task RemoveAsync(CampaignMember member, CancellationToken cancellationToken = default);
}
