using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface IArtifactRepository
{
    Task<Artifact> CreateAsync(Artifact artifact, CancellationToken cancellationToken = default);

    Task<Artifact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Artifact>> ListByCampaignAsync(Guid campaignId, ArtifactType? type = null, VisibilityScope? visibility = null, CancellationToken cancellationToken = default);

    Task<Artifact> UpdateAsync(Artifact artifact, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Artifact>> SearchByNameAsync(Guid campaignId, string searchTerm, CancellationToken cancellationToken = default);
}
