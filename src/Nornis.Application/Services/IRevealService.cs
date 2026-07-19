using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IRevealService
{
    /// <summary>
    /// Promotes a GM-curated set of GM-only artifacts, facts, and relationships to
    /// <c>PartyVisible</c>, applying optional belief-corrections in the same event. Recorded as
    /// accepted <c>Update*</c> proposals on a synthetic party-visible reveal source so the
    /// players' new knowledge carries provenance. GM only; one transaction; one-way.
    ///
    /// Returns a <see cref="RevealResult"/> whose <see cref="RevealResult.MissingArtifactIds"/>
    /// is non-empty (and nothing applied) when the set is not reference-closed.
    /// </summary>
    Task<AppResult<RevealResult>> RevealAsync(RevealCommand command, CancellationToken ct);

    /// <summary>
    /// GM-only: lifts a GM-only source (and, with it, its attachments — e.g. a map image) to
    /// <c>PartyVisible</c>. This is the sanctioned exception to the post-extraction visibility
    /// lock that <c>SourceService</c> enforces for ordinary edits. Canon derived from the source
    /// is NOT revealed — that goes through <see cref="RevealAsync"/>. Idempotent; one-way.
    /// </summary>
    Task<AppResult<RevealSourceResult>> RevealSourceAsync(
        Guid worldId, Guid sourceId, Guid actingUserId, WorldRole role, CancellationToken ct);
}
