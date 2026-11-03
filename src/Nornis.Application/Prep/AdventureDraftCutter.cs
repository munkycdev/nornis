using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>One section cut from the draft: the plan's kind and title, and the draft's own lines as its body.</summary>
public sealed record CutSection(AdventureSectionKind Kind, string Title, string BodyMarkdown);

/// <summary>Thrown when a cut's sections don't hold exactly the draft's words; it can only mean a bug, so nothing is applied.</summary>
public sealed class DraftCutCheckFailedException(string message) : Exception(message);

/// <summary>
/// The only code that slices the design draft into sections (feature 37). Each section's body
/// is the draft's lines from its start line up to the next start. A start line that is a
/// heading whose text is the plan's title is taken out of the body, because the title now holds
/// those words; a heading the GM renamed stays in the body, so no word leaves the sections. Then
/// the cut is checked: the taken-out headings and the bodies, in order, must hold exactly the
/// draft's words.
/// Words are split on whitespace and heading marks; nothing else is normalised. A cut that fails
/// the check throws, because no plan can make it fail and only a bug can.
/// </summary>
public static class AdventureDraftCutter
{
    /// <summary>The cut, or <see cref="DraftCutCheckFailedException"/>. The plan has passed <see cref="AdventureDraftPlan.Validate"/>.</summary>
    public static IReadOnlyList<CutSection> Cut(string draft, IReadOnlyList<DraftSplitPoint> plan)
    {
        var lines = AdventureDraftPlan.LinesOf(draft);
        var sections = new List<CutSection>(plan.Count);
        var pieces = new List<string>(plan.Count * 2);

        for (var i = 0; i < plan.Count; i++)
        {
            var start = plan[i].StartLine - 1;
            var end = i + 1 < plan.Count ? plan[i + 1].StartLine - 1 : lines.Length;

            var bodyStart = start;
            if (AdventureDraftPlanner.IsHeadingLine(lines[start])
                && AdventureDraftPlanner.HeadingText(lines[start]) == plan[i].Title.Trim())
            {
                pieces.Add(lines[start]);
                bodyStart++;
            }

            var body = string.Join('\n', lines[bodyStart..end]).Trim('\n');
            pieces.Add(body);
            sections.Add(new CutSection(plan[i].Kind, plan[i].Title.Trim(), body));
        }

        Verify(draft, pieces);
        return sections;
    }

    /// <summary>The check: the pieces, in order, hold exactly the draft's words.</summary>
    internal static void Verify(string draft, IReadOnlyList<string> pieces)
    {
        var expected = WordsOf(draft);
        var actual = pieces.SelectMany(WordsOf).ToList();
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            var at = expected.Zip(actual).TakeWhile(p => p.First == p.Second).Count();
            throw new DraftCutCheckFailedException(
                $"The cut changed the draft's words at word {at + 1} of {expected.Count} (the sections hold {actual.Count}).");
        }
    }

    /// <summary>The words of a text: split on whitespace, with tokens that are only heading marks left out.</summary>
    internal static List<string> WordsOf(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Any(c => c != '#'))
            .ToList();
}
