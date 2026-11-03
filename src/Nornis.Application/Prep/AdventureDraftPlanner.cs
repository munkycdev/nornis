using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>
/// The default split plan (feature 37), from the draft's own headings and with no AI: a <c>#</c>
/// heading starts a Chapter, a <c>##</c> heading a section whose kind is read from its title,
/// and text before the first heading becomes an Overview. Headings are found by Markdig, so a
/// <c>#</c> inside a code fence (a stat block, say) or a quote never splits. Only ATX headings
/// count; an underlined heading stays in its section's text.
/// </summary>
public static partial class AdventureDraftPlanner
{
    public const string OverviewTitle = "Overview";

    [GeneratedRegex(@"^\s{0,3}#{1,6}(?:\s+|$)")]
    private static partial Regex AtxOpening();

    [GeneratedRegex(@"\s+#+\s*$")]
    private static partial Regex AtxClosing();

    public static IReadOnlyList<DraftSplitPoint> FromHeadings(string draft)
    {
        var lines = AdventureDraftPlan.LinesOf(draft);
        var document = Markdown.Parse(string.Join('\n', lines));
        var points = new List<DraftSplitPoint>();

        foreach (var heading in document.OfType<HeadingBlock>().Where(h => h is { Level: 1 or 2, IsSetext: false }))
        {
            var line = heading.Line + 1;
            var title = HeadingText(lines[heading.Line]);
            if (title.Length == 0)
            {
                continue;
            }

            if (title.Length > AdventureDraftPlan.MaxTitleChars)
            {
                title = title[..AdventureDraftPlan.MaxTitleChars].TrimEnd();
            }

            points.Add(new DraftSplitPoint(line, title, heading.Level == 1 ? AdventureSectionKind.Chapter : KindFromTitle(title)));
        }

        var firstStart = points.Count > 0 ? points[0].StartLine : lines.Length + 1;
        if (lines.Take(firstStart - 1).Any(l => l.Trim().Length > 0))
        {
            points.Insert(0, new DraftSplitPoint(FirstTextLine(lines), OverviewTitle, AdventureSectionKind.Overview));
        }

        return points;
    }

    /// <summary>A section's kind from its title: the skeleton's names, otherwise a Scene.</summary>
    public static AdventureSectionKind KindFromTitle(string title)
    {
        var t = title.Trim();
        return t.StartsWith("Overview", StringComparison.OrdinalIgnoreCase) ? AdventureSectionKind.Overview
            : t.StartsWith("Session clock", StringComparison.OrdinalIgnoreCase) ? AdventureSectionKind.SessionClock
            : t.StartsWith("Quick reference", StringComparison.OrdinalIgnoreCase) ? AdventureSectionKind.QuickReference
            : t.StartsWith("Notes", StringComparison.OrdinalIgnoreCase) ? AdventureSectionKind.Notes
            : AdventureSectionKind.Scene;
    }

    /// <summary>Whether a line opens an ATX heading of any level; the cutter takes such a start line as the title.</summary>
    public static bool IsHeadingLine(string line) => AtxOpening().IsMatch(line);

    /// <summary>The heading's text as written, without its #s.</summary>
    public static string HeadingText(string line) => AtxClosing().Replace(AtxOpening().Replace(line, ""), "").Trim();

    private static int FirstTextLine(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length > 0)
            {
                return i + 1;
            }
        }

        return 1;
    }
}
