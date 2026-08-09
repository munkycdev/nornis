using Nornis.Domain.Entities;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

public interface ICampaignRepository
{
    /// <summary>
    /// Creates the campaign, positioning it last in the world's display order. The position
    /// is assigned here rather than by the caller so every creation path gets one.
    /// </summary>
    Task<Campaign> CreateAsync(Campaign campaign, CancellationToken cancellationToken = default);

    Task<Campaign?> GetByIdAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Campaign>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    Task<Campaign> UpdateAsync(Campaign campaign, CancellationToken cancellationToken = default);

    /// <summary>
    /// The artifacts this campaign's sources evidence, most-cited first, with the total the
    /// page was cut from.
    ///
    /// <paramref name="filter"/> is applied to sources, facts, relationships and artifacts
    /// alike, in SQL. All four matter: a reader who cannot see a GMOnly artifact must not
    /// learn its name from this list, and one who cannot read a GMOnly <em>source</em> must
    /// not have it contribute to a count either.
    /// </summary>
    Task<CampaignRollup> GetRollupAsync(
        Guid worldId,
        Guid campaignId,
        VisibilityFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes 1-based display positions across the world in the order given. Every campaign
    /// in the world is rewritten in one pass — a partial write would leave positioned rows
    /// interleaved with unpositioned zeroes, which sort ahead of them.
    /// </summary>
    /// <returns>The world's campaigns in their new order.</returns>
    Task<IReadOnlyList<Campaign>> ReorderAsync(
        Guid worldId, IReadOnlyList<Guid> orderedCampaignIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the campaign after detaching dependents that the database does not
    /// cascade: clears Source.CampaignId and removes campaign-character assignments
    /// and the generated recap. Knowledge and sources are never deleted with a campaign.
    /// </summary>
    Task DeleteAsync(Guid campaignId, CancellationToken cancellationToken = default);
}
