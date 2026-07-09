using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface ICharacterRepository
{
    Task<Character> CreateAsync(Character character, CancellationToken cancellationToken = default);

    Task<Character?> GetByIdAsync(Guid characterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// All characters in a world, with campaign assignments included.
    /// </summary>
    Task<IReadOnlyList<Character>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Character>> ListByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task<Character> UpdateAsync(Character character, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid characterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the set of characters assigned to a campaign.
    /// </summary>
    Task ReplaceCampaignAssignmentsAsync(Guid campaignId, IReadOnlyCollection<Guid> characterIds, CancellationToken cancellationToken = default);
}
