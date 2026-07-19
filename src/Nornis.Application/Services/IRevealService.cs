using Nornis.Application.Errors;
using Nornis.Application.Models;

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
}
