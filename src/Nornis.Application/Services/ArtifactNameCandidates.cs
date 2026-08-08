using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Services;

/// <summary>
/// "Did you mean…" for an artifact name a proposal references and canon does not hold.
///
/// The scoring is <see cref="ArtifactRelevance"/>'s — this is not a second relevance policy —
/// but three things differ from the search bar, each for a reason:
///
/// Names are scored in BOTH directions. Extraction is as likely to write the fuller name as
/// the shorter one, and a one-directional score catches only half of that: "Kaelen" against
/// canon's "Kaelen Vorr" is a prefix hit, while "Kaelen Vorr" against canon's "Kaelen" is
/// nothing at all.
///
/// Summaries are not searched and the bar is <see cref="ArtifactRelevance.NameWord"/>: the two
/// names must share a whole word or a prefix ("Voss" and "Vossberg" qualify — that is a
/// resemblance a person should glance at). What does not qualify is a substring buried inside
/// a longer name, or an artifact whose summary merely mentions the word; neither is a reason to
/// hang a fact on it.
///
/// The bar cuts both ways, so it is set where the cheaper mistake is. Every candidate found
/// here suppresses the automatic create below and costs the reviewer a decision; every one
/// missed mints an artifact they may have to merge away. A decision is the cheaper of the two,
/// so the bar sits low.
///
/// Typos are deliberately out of scope: no edit distance. A misspelt name creating its own
/// artifact is visible, obvious and mergeable; a name silently bound one letter away from the
/// intended one is none of those.
/// </summary>
public static class ArtifactNameCandidates
{
    /// <summary>Most alternatives worth putting in front of a reviewer at once.</summary>
    public const int MaxCandidates = 5;

    /// <summary>
    /// The artifacts in <paramref name="artifacts"/> that could plausibly be what
    /// <paramref name="name"/> meant, best first, seen through <paramref name="filter"/>'s eyes.
    /// Archived artifacts are merge leftovers and never candidates.
    /// </summary>
    public static IReadOnlyList<Artifact> Rank(
        IEnumerable<Artifact> artifacts,
        string? name,
        VisibilityFilter filter,
        int max = MaxCandidates)
    {
        var needle = ArtifactNameKey.Collapse(name);
        if (needle.Length == 0)
        {
            return [];
        }

        // Ties break toward the shorter name and then the more recently touched artifact,
        // matching what global search does with the same scores.
        return artifacts
            .Where(a => a.Status != ArtifactStatus.Archived)
            .Where(a => filter.CanSee(a.Visibility, a.CreatedByUserId))
            .Select(a => new { Artifact = a, Score = Similarity(a.Name, needle) })
            .Where(x => x.Score >= ArtifactRelevance.NameWord)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Artifact.Name.Length)
            .ThenByDescending(x => x.Artifact.UpdatedAt)
            .Take(Math.Clamp(max, 1, MaxCandidates))
            .Select(x => x.Artifact)
            .ToList();
    }

    /// <summary>
    /// Whether two artifact names resemble each other closely enough that a person should be
    /// asked which is meant. The same bar <see cref="Rank"/> applies, exposed for candidates
    /// that are not artifacts yet — a Create proposal sitting undecided in the batch has a
    /// name and no row, and auto-creating something that resembles it is exactly the twin this
    /// is here to prevent.
    /// </summary>
    public static bool Resembles(string? a, string? b)
    {
        var left = ArtifactNameKey.Collapse(a);
        var right = ArtifactNameKey.Collapse(b);

        return left.Length > 0 && right.Length > 0 && Similarity(left, right) >= ArtifactRelevance.NameWord;
    }

    private static int Similarity(string? candidateName, string needle) =>
        Math.Max(
            ArtifactRelevance.Score(candidateName, null, needle),
            ArtifactRelevance.Score(needle, null, candidateName));
}
