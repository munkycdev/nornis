using MudBlazor;
using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>
/// Presentation for the convergence gauge. Every phrase is built from an observation the API
/// already sent — the page never recomputes a component, so the score and the sentence next to
/// it cannot disagree about the same candidate.
/// </summary>
public static class ConvergenceDisplay
{
    /// <summary>
    /// The reasons a candidate ranks where it does, most decisive first. A lopsided candidate
    /// reads as lopsided: the phrases that do not apply are simply absent.
    /// </summary>
    public static IReadOnlyList<string> Phrases(ConvergenceCandidateDto candidate)
    {
        var components = candidate.Components;
        var phrases = new List<string>();

        if (components.ContradictionSeverity is not null)
        {
            phrases.Add(
                $"contradicts what the party believes ({components.ContradictionSeverity.ToLowerInvariant()})");
        }

        phrases.Add(components.DaysHidden switch
        {
            0 => "hidden since today",
            1 => "hidden for a day",
            _ => $"hidden for {components.DaysHidden} days"
        });

        phrases.Add(components.IsSelfContained
            ? "reveals cleanly on its own"
            : components.MissingArtifactCount == 1
                ? "brings 1 other entry with it"
                : $"brings {components.MissingArtifactCount} other entries with it");

        if (components.PartyVisibleFactsOnAnchor == 0)
        {
            phrases.Add("the party has not met this entry");
        }

        if (components.StorylineStatus is not null)
        {
            phrases.Add($"storyline {components.StorylineStatus.ToLowerInvariant()}");
        }

        return phrases;
    }

    /// <summary>
    /// How full to draw a candidate's ring, relative to the strongest candidate in the same
    /// gauge — so the top row reads as the top row.
    ///
    /// The score itself is absolute: it measures a candidate against an ideal, not against its
    /// neighbours. That is the right thing for the ranking and the wrong thing for a ring, and
    /// a real world showed why. A young world with no contradictions caps every score around 30
    /// — arithmetically correct, since the heaviest signal is absent — and a page of one-third
    /// rings reads as "none of this matters" when those are the best candidates that exist.
    ///
    /// Normalising the *number* instead would have been the lie: it would show 100 for the top
    /// candidate of a world where nothing is ready. So the fill moves and the number does not.
    /// </summary>
    public static int RelativeFill(int score, int topScore)
    {
        if (topScore <= 0 || score <= 0)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Round(score * 100.0 / topScore), 0, 100);
    }

    /// <summary>
    /// Deliberately absolute, where <see cref="RelativeFill"/> is relative. A full ring in a
    /// muted colour is the honest reading of "the best thing available, and it is not urgent" —
    /// colouring by rank would make every world look like it had something burning in it.
    /// </summary>
    public static Color ScoreColor(int score) => score switch
    {
        >= 60 => Color.Primary,
        >= 30 => Color.Secondary,
        _ => Color.Default
    };
}
