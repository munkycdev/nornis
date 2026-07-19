using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IArtifactRemovalService
{
    /// <summary>Summarizes what removing an artifact from canon would delete. Read-only.</summary>
    Task<AppResult<ArtifactRemovalPreview>> PreviewAsync(
        Guid worldId, Guid artifactId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>
    /// Removes an artifact and the knowledge attached to it (its facts, the relationships
    /// touching it, its map pins, and provenance), clearing any player-character link first.
    /// GM-only. Other artifacts are never touched.
    /// </summary>
    Task<AppResult> RemoveAsync(RemoveArtifactCommand command, CancellationToken ct);
}
