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

    public static Color ScoreColor(int score) => score switch
    {
        >= 60 => Color.Primary,
        >= 30 => Color.Secondary,
        _ => Color.Default
    };
}
