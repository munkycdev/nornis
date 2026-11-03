using System.Text;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>
/// Gathers the sections back into one draft (feature 37), in order: a Chapter as a <c>#</c>
/// heading and any other section as a <c>##</c> heading, each followed by its text. Unwritten
/// sections are left out, except a Chapter with written sections under it, which stays as its
/// heading alone so the draft keeps its chapters. The headings are the ones <see cref="AdventureDraftPlanner"/> splits on,
/// so a gathered draft splits back into the same sections.
/// </summary>
public static class AdventureDraftGatherer
{
    public static string Join(IEnumerable<AdventureSection> sections)
    {
        var ordered = sections.OrderBy(s => s.SortOrder).ToList();
        var sb = new StringBuilder();
        for (var i = 0; i < ordered.Count; i++)
        {
            var section = ordered[i];
            var written = !AdventureSkeleton.IsUnfilled(section.BodyMarkdown);
            if (!written && !(section.Kind == AdventureSectionKind.Chapter && HasWrittenUnder(ordered, i)))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append("\n\n");
            }

            sb.Append(section.Kind == AdventureSectionKind.Chapter ? "# " : "## ").Append(section.Title.Trim());
            if (written)
            {
                sb.Append("\n\n").Append(section.BodyMarkdown.Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n'));
            }
        }

        return sb.ToString();
    }

    /// <summary>Whether a written section follows the Chapter at <paramref name="index"/> before the next Chapter.</summary>
    private static bool HasWrittenUnder(List<AdventureSection> ordered, int index)
    {
        for (var i = index + 1; i < ordered.Count && ordered[i].Kind != AdventureSectionKind.Chapter; i++)
        {
            if (!AdventureSkeleton.IsUnfilled(ordered[i].BodyMarkdown))
            {
                return true;
            }
        }

        return false;
    }
}
