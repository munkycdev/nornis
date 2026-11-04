namespace Nornis.Evals.Scoring;

/// <summary>
/// Matches one case's proposals against the baseline's kept ones, with no model in the loop,
/// so a score never depends on a judge. Two proposals meet when they are the same kind of
/// change about the same artifact (names compared without case, articles or punctuation, and a
/// name that is the other's words with some missing still counts: "Quell" and "Harbormaster
/// Quell"). Facts must also say something alike: their predicate and value share enough words.
/// Each baseline proposal takes its best unmatched partner, so nothing is counted twice.
/// </summary>
public static class Scorer
{
    /// <summary>How alike two facts' words must be to be the same fact (Jaccard over the
    /// predicate and value). Low on purpose: models word a fact differently, and the report shows
    /// every matched pair whose wording differs for a person to judge.</summary>
    public const double FactThreshold = 0.2;

    /// <summary>A matched pair whose text is less alike than this is listed as a difference.</summary>
    public const double DifferenceThreshold = 0.5;

    public static CaseScore Score(IReadOnlyList<ProposalView> baseline, IReadOnlyList<ProposalView> answer)
    {
        var unmatched = answer.ToList();
        var found = new List<(ProposalView Baseline, ProposalView Answer)>();
        var missed = new List<ProposalView>();

        foreach (var expected in baseline)
        {
            var best = unmatched
                .Select(candidate => (Candidate: candidate, Score: Similarity(expected, candidate)))
                .Where(c => c.Score > 0)
                .OrderByDescending(c => c.Score)
                .FirstOrDefault();
            if (best.Candidate is null)
            {
                missed.Add(expected);
                continue;
            }

            found.Add((expected, best.Candidate));
            unmatched.Remove(best.Candidate);
        }

        return new CaseScore(found, missed, unmatched);
    }

    /// <summary>Zero when the two can't be the same proposal; otherwise higher the more alike.</summary>
    internal static double Similarity(ProposalView a, ProposalView b)
    {
        if (a.Kind != b.Kind)
        {
            return 0;
        }

        if (a.Kind == ProposalView.LinkKind)
        {
            // Either direction: "A employs B" and "B works for A" are one link.
            var same = (SameName(a.Subject, b.Subject) && SameName(a.Other, b.Other))
                       || (SameName(a.Subject, b.Other) && SameName(a.Other, b.Subject));
            return same ? 1 + Jaccard(a.Label, b.Label) : 0;
        }

        if (!SameName(a.Subject, b.Subject))
        {
            return 0;
        }

        switch (a.Kind)
        {
            case ProposalView.FactKind:
                var words = Jaccard($"{a.Label} {a.Text}", $"{b.Label} {b.Text}");
                return words >= FactThreshold ? 1 + words : 0;

            case ProposalView.MergeKind:
                return SameName(a.Other, b.Other) ? 1 : 0;

            default:
                return 1 + Jaccard(a.Text, b.Text) + (a.ChangeType == b.ChangeType ? 0.5 : 0);
        }
    }

    internal static bool SameName(string? a, string? b)
    {
        var left = ProposalView.Normalize(a).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var right = ProposalView.Normalize(b).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return left.Count > 0 && right.Count > 0 && (left.IsSubsetOf(right) || right.IsSubsetOf(left));
    }

    /// <summary>Shared words over all words, 0 to 1; two empty texts are alike.</summary>
    public static double Jaccard(string? a, string? b)
    {
        var left = ProposalView.Words(a).ToHashSet();
        var right = ProposalView.Words(b).ToHashSet();
        if (left.Count == 0 && right.Count == 0)
        {
            return 1;
        }

        var union = left.Union(right).Count();
        return union == 0 ? 0 : (double)left.Intersect(right).Count() / union;
    }
}

public sealed record CaseScore(
    IReadOnlyList<(ProposalView Baseline, ProposalView Answer)> Found,
    IReadOnlyList<ProposalView> Missed,
    IReadOnlyList<ProposalView> Invented)
{
    /// <summary>Matched pairs whose wording differs enough for a person to look at.</summary>
    public IEnumerable<(ProposalView Baseline, ProposalView Answer)> Differences =>
        Found.Where(f => Scorer.Jaccard(f.Baseline.Text, f.Answer.Text) < Scorer.DifferenceThreshold
                         || f.Baseline.ChangeType != f.Answer.ChangeType);
}
