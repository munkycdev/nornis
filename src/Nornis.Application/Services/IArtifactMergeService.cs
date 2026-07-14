using Nornis.Application.Errors;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IArtifactMergeService
{
    /// <summary>
    /// Merges the duplicate artifact into the target: facts and relationships move to
    /// the target, the duplicate is archived. Recorded as an accepted MergeArtifact
    /// proposal on a synthetic source so the merge has provenance like any reviewed
    /// change. GM only.
    /// </summary>
    Task<AppResult<Guid>> MergeAsync(
        Guid worldId,
        Guid duplicateArtifactId,
        Guid targetArtifactId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct);
}
