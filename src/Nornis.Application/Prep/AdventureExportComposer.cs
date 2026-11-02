using System.Text;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>
/// The body of an Export to Nornis (feature 29, Requirement 8): a snapshot of the Adventure's
/// sections in order, under one filing line that says what this is. Chapters become <c>#</c>
/// headings and every other section a <c>##</c>, so the extractor reads the same structure the
/// GM wrote. Unwritten sections carry nothing and are left out.
/// </summary>
public static class AdventureExportComposer
{
    /// <summary>A title is cut to this inside the filing line, which keeps the line within its allowance.</summary>
    private const int MaxTitleInFilingLine = 100;

    private const int MaxSourceTitleChars = 200;

    /// <summary>
    /// The sections an export takes, in order: all of them, or one Chapter and the sections after
    /// it up to the next Chapter. Null when <paramref name="chapterSectionId"/> isn't one of this
    /// Adventure's Chapters.
    /// </summary>
    public static IReadOnlyList<AdventureSection>? Select(Adventure adventure, Guid? chapterSectionId)
    {
        var ordered = adventure.Sections.OrderBy(s => s.SortOrder).ToList();
        if (chapterSectionId is not { } chapterId)
        {
            return ordered;
        }

        var start = ordered.FindIndex(s => s.Id == chapterId && s.Kind == AdventureSectionKind.Chapter);
        if (start < 0)
        {
            return null;
        }

        var end = ordered.FindIndex(start + 1, s => s.Kind == AdventureSectionKind.Chapter);
        return ordered.GetRange(start, (end < 0 ? ordered.Count : end) - start);
    }

    /// <summary>Whether any of the sections has something to extract.</summary>
    public static bool HasContent(IEnumerable<AdventureSection> sections) =>
        sections.Any(s => !AdventureSkeleton.IsUnfilled(s.BodyMarkdown));

    public static string BuildTitle(string adventureTitle, string? chapterTitle)
    {
        var title = chapterTitle is null
            ? $"{adventureTitle.Trim()} (prep)"
            : $"{adventureTitle.Trim()}: {chapterTitle.Trim()} (prep)";
        return title.Length <= MaxSourceTitleChars ? title : title[..(MaxSourceTitleChars - 1)].TrimEnd() + "…";
    }

    public static string BuildFilingLine(string adventureTitle, string? chapterTitle, DateTimeOffset at)
    {
        var chapter = chapterTitle is null ? "" : $", chapter \"{Cut(chapterTitle)}\"";
        return $"Adventure \"{Cut(adventureTitle)}\"{chapter}, exported {at:yyyy-MM-dd} by the GM as prep";
    }

    public static string BuildBody(string adventureTitle, string? chapterTitle, IReadOnlyList<AdventureSection> sections, DateTimeOffset at)
    {
        var body = new StringBuilder();
        body.Append(BuildFilingLine(adventureTitle, chapterTitle, at));

        foreach (var section in sections)
        {
            var isChapter = section.Kind == AdventureSectionKind.Chapter;
            var unfilled = AdventureSkeleton.IsUnfilled(section.BodyMarkdown);
            if (!isChapter && unfilled)
            {
                continue;
            }

            body.Append("\n\n").Append(isChapter ? "# " : "## ").Append(section.Title.Trim());
            if (!unfilled)
            {
                body.Append("\n\n").Append(section.BodyMarkdown.Trim());
            }
        }

        return body.ToString();
    }

    private static string Cut(string title)
    {
        var trimmed = title.Trim();
        return trimmed.Length <= MaxTitleInFilingLine ? trimmed : trimmed[..(MaxTitleInFilingLine - 1)].TrimEnd() + "…";
    }
}
