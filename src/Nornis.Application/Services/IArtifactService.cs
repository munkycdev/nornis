using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IArtifactService
{
    /// <summary>
    /// Lists artifacts in a campaign visible to the requesting user's role, most recently
    /// updated first. Optionally filtered by artifact type (e.g. Storyline) and status.
    /// </summary>
    Task<AppResult<IReadOnlyList<Artifact>>> ListAsync(ArtifactListQuery query, CancellationToken ct);

    /// <summary>
    /// Retrieves the full detail for a single artifact — facts, relationships, connected
    /// artifacts, and source references — all scoped to what the requesting role may see.
    /// Returns not-found if the artifact does not exist, belongs to another campaign, or is
    /// not visible to the requesting role.
    /// </summary>
    Task<AppResult<ArtifactDetail>> GetDetailAsync(
        Guid artifactId,
        Guid campaignId,
        Guid requestingUserId,
        CampaignRole role,
        CancellationToken ct);
}
