using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IArtifactService
{
    /// <summary>
    /// Lists artifacts in a world visible to the requesting user's role, most recently
    /// updated first. Optionally filtered by artifact type (e.g. Storyline) and status.
    /// </summary>
    Task<AppResult<IReadOnlyList<Artifact>>> ListAsync(ArtifactListQuery query, CancellationToken ct);

    /// <summary>
    /// Retrieves the full detail for a single artifact — facts, relationships, connected
    /// artifacts, and source references — all scoped to what the requesting role may see.
    /// Returns not-found if the artifact does not exist, belongs to another world, or is
    /// not visible to the requesting role.
    /// </summary>
    /// <summary>Caller-visible artifacts and relationships as a renderable graph.</summary>
    Task<AppResult<ArtifactGraph>> GetGraphAsync(Guid worldId, WorldRole role, CancellationToken ct);

    Task<AppResult<ArtifactDetail>> GetDetailAsync(
        Guid artifactId,
        Guid worldId,
        Guid requestingUserId,
        WorldRole role,
        CancellationToken ct);

    /// <summary>
    /// GM-only: renames an artifact. Artifacts are shared canon, so renaming carries the
    /// same authority requirement as merging.
    /// </summary>
    Task<AppResult<Artifact>> RenameAsync(RenameArtifactCommand command, CancellationToken ct);

    /// <summary>
    /// The storyline timeline: non-archived storylines as lanes of developments dated by
    /// the sessions that established them, all scoped to the caller's visibility.
    /// </summary>
    Task<AppResult<StorylineTimeline>> GetStorylineTimelineAsync(Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct);
}
