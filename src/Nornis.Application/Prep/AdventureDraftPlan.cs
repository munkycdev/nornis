namespace Nornis.Application.Prep;

/// <summary>
/// The rule for a split plan (feature 37), whoever made it: the headings planner, the
/// Loremaster or the GM's edits in the plan dialog. A plan names where sections start and what
/// they are called, never their text, so a plan can't change a word; this checks it can be cut.
/// </summary>
public static class AdventureDraftPlan
{
    public const int MaxTitleChars = 200;

    /// <summary>The draft as the plan counts it: <c>\n</c> line ends, line 1 first.</summary>
    public static string[] LinesOf(string draft) =>
        draft.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    /// <summary>Why the plan can't be cut from these lines, or null when it can.</summary>
    public static string? Validate(IReadOnlyList<DraftSplitPoint> plan, IReadOnlyList<string> lines, int maxSections)
    {
        if (plan.Count == 0)
        {
            return "The plan has no sections.";
        }

        if (plan.Count > maxSections)
        {
            return $"The plan makes {plan.Count:N0} sections; an adventure holds at most {maxSections:N0}.";
        }

        var previous = 0;
        foreach (var point in plan)
        {
            if (point.StartLine < 1 || point.StartLine > lines.Count)
            {
                return $"A section starts at line {point.StartLine}, but the draft has {lines.Count:N0} lines.";
            }

            if (point.StartLine <= previous)
            {
                return $"The sections must start in order down the draft; line {point.StartLine} comes after line {previous}.";
            }

            if (!Enum.IsDefined(point.Kind))
            {
                return $"\"{point.Kind}\" is not a kind of section.";
            }

            if (string.IsNullOrWhiteSpace(point.Title))
            {
                return $"The section starting at line {point.StartLine} has no title.";
            }

            if (point.Title.Trim().Length > MaxTitleChars)
            {
                return $"A section title can be at most {MaxTitleChars} characters.";
            }

            previous = point.StartLine;
        }

        // No text is ever dropped: anything above the first section must be blank.
        if (lines.Take(plan[0].StartLine - 1).Any(l => l.Trim().Length > 0))
        {
            return $"The first section starts at line {plan[0].StartLine}, which would leave out the text above it.";
        }

        return null;
    }
}
