using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Resolving a storyline settles the claims it established: facts still carrying a provisional
/// truth state (Likely, Rumor, Disputed) are promoted to Confirmed. Deliberate markings stand —
/// already-Confirmed facts, ones marked False or Hidden, and open questions are left untouched
/// (an open question is answered elsewhere, not confirmed by the arc closing).
/// </summary>
internal static class StorylineResolution
{
    private const string OpenQuestionPredicate = "open question";

    /// <summary>A provisional, non-question fact that resolving the storyline settles to Confirmed.</summary>
    public static bool SettlesOnResolve(ArtifactFact fact) =>
        fact.TruthState is TruthState.Likely or TruthState.Rumor or TruthState.Disputed
        && !string.Equals(fact.Predicate, OpenQuestionPredicate, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Promotes a storyline's provisional facts to Confirmed. Callers gate on the artifact being
    /// a Storyline whose status was just set to Resolved, so both the artifact-page action and an
    /// accepted wrap-up/retrospective closure settle facts the same way.
    /// </summary>
    public static async Task SettleFactsAsync(
        IArtifactFactRepository factRepository, Guid storylineId, DateTimeOffset now, CancellationToken ct)
    {
        var facts = await factRepository.ListByArtifactAsync(storylineId, ct);
        foreach (var fact in facts.Where(SettlesOnResolve))
        {
            fact.TruthState = TruthState.Confirmed;
            fact.UpdatedAt = now;
            await factRepository.UpdateAsync(fact, ct);
        }
    }
}
