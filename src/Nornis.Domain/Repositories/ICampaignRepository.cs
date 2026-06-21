using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface ICampaignRepository
{
    Task<Campaign> CreateAsync(Campaign campaign, CancellationToken cancellationToken = default);

    Task<Campaign?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Campaign> UpdateAsync(Campaign campaign, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Campaign>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
