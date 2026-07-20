using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

/// <summary>
/// Reads and writes the GM-declared campaign memberships of storylines
/// (<see cref="StorylineCampaign"/>). The join is storyline-only by convention; the service
/// layer enforces that the artifact is a Storyline.
/// </summary>
public interface IStorylineCampaignRepository
{
    /// <summary>Every declared membership for the given storyline artifacts, in one query.</summary>
    Task<IReadOnlyList<StorylineCampaign>> ListByArtifactIdsAsync(
        IReadOnlyCollection<Guid> artifactIds, CancellationToken cancellationToken = default);

    /// <summary>The declared campaign memberships of a single storyline.</summary>
    Task<IReadOnlyList<StorylineCampaign>> ListByArtifactIdAsync(
        Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a storyline's declared campaign set with <paramref name="campaignIds"/>:
    /// adds the missing links, removes the surplus, leaves the rest untouched.
    /// </summary>
    Task ReplaceForStorylineAsync(
        Guid artifactId, IReadOnlyCollection<Guid> campaignIds, Guid? actingUserId, CancellationToken cancellationToken = default);
}
