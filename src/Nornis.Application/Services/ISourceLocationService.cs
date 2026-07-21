using Nornis.Application.Errors;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// One Location artifact a session is linked to. <paramref name="Summary"/> feeds the same
/// hover-card (ArtifactTip) the rest of the app uses.
/// </summary>
public sealed record LinkedLocation(Guid ArtifactId, string Name, string? Summary);

/// <summary>
/// Reads and writes the user-authored links between a session (Source) and the Location artifacts
/// it took place at. Every link is an ordinary <c>SourceReference</c> (Artifact target), so it
/// feeds the Locations view and the Journey trail through the same "visited" derivation — a person
/// drawing one here corrects both surfaces at once.
/// </summary>
public interface ISourceLocationService
{
    /// <summary>The caller-visible Location artifacts this session is linked to, ordered by name.</summary>
    Task<AppResult<IReadOnlyList<LinkedLocation>>> ListLocationsAsync(
        Guid sourceId, Guid worldId, Guid userId, WorldRole role, CancellationToken ct);

    /// <summary>
    /// Links a session to a Location artifact and returns the updated set. Idempotent — linking a
    /// place already tied to the session (by hand or by extraction) is a no-op. <c>400 not_a_location</c>
    /// when the target is not a caller-visible, non-archived Location in the world; <c>403</c> when
    /// the caller may not edit the source; <c>404</c> when the source is absent.
    /// </summary>
    Task<AppResult<IReadOnlyList<LinkedLocation>>> LinkLocationAsync(
        Guid sourceId, Guid worldId, Guid artifactId, Guid userId, WorldRole role, CancellationToken ct);

    /// <summary>
    /// Removes a session's link to a Location and returns the updated set. Remove-any: an editor may
    /// drop any of the session's location links, including extractor-authored ones (the model keeps
    /// no user/extractor distinction on the row). A no-op when the link is absent. <c>403</c> when
    /// the caller may not edit the source; <c>404</c> when the source is absent.
    /// </summary>
    Task<AppResult<IReadOnlyList<LinkedLocation>>> UnlinkLocationAsync(
        Guid sourceId, Guid worldId, Guid artifactId, Guid userId, WorldRole role, CancellationToken ct);
}
