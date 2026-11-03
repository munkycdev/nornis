using System.Text;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Prep.Covers;

/// <summary>
/// The art rules (feature 38), in one place: what the Loremaster is asked for when it writes a
/// cover's art brief, and what every image request carries whatever brief the GM sends.
///
/// The brief reads only what a cover may show: the title, the pitch, the Overview and the game
/// system. Never the world's knowledge, so a cover can't give away a Hidden truth once the book is
/// shared, and never the rest of the sections.
/// </summary>
public static class CoverArtPrompt
{
    /// <summary>How much of the Overview the brief reads: its opening is what a cover sells.</summary>
    public const int OverviewChars = 2_000;

    public const string BriefSystemPrompt = """
        You write art briefs for the cover of a tabletop role-playing adventure. An image model paints
        from your brief; a designer sets every word of type on the cover afterwards.

        Write one paragraph of 60 to 120 words describing a single striking scene that sells the
        adventure: the setting, the light, the mood, one focal subject, and the palette. Write it as a
        description of a painting, in plain present tense.

        Rules:
        - No words, letters, numbers, titles, logos, signatures or watermarks anywhere in the image.
        - Do not name any living artist, studio or illustrator, and do not ask for any publisher's
          house style or trade dress.
        - Do not reveal twists, villains' identities or secrets; show the hook, not the ending.
        - Portrait composition, with calm empty space across the top quarter for the title.

        Answer with the paragraph only.
        """;

    /// <summary>What every image request says after the GM's brief, so no edit of the brief can drop a rule.</summary>
    public const string ImageRules =
        "Painted illustration for a book cover, portrait orientation. Leave calm, uncluttered space across the top quarter. "
        + "Absolutely no text, letters, numbers, logos, signatures or watermarks anywhere in the image.";

    public static string BuildBriefMessage(Adventure adventure, string? systemLine)
    {
        var sb = new StringBuilder();
        sb.Append("Title: ").AppendLine(adventure.Title.Trim());
        if (!string.IsNullOrWhiteSpace(systemLine))
        {
            sb.Append("Game system: ").AppendLine(systemLine);
        }

        if (!string.IsNullOrWhiteSpace(adventure.Pitch))
        {
            sb.AppendLine().AppendLine("Pitch:").AppendLine(adventure.Pitch.Trim());
        }

        var overview = adventure.Sections
            .OrderBy(s => s.SortOrder)
            .FirstOrDefault(s => s.Kind == AdventureSectionKind.Overview && !AdventureSkeleton.IsUnfilled(s.BodyMarkdown));
        if (overview?.BodyMarkdown is { } body)
        {
            var text = body.Trim();
            sb.AppendLine().AppendLine("Overview:").AppendLine(text.Length <= OverviewChars ? text : text[..OverviewChars] + " …");
        }

        return sb.ToString();
    }

    /// <summary>The image request: the brief, then the rules.</summary>
    public static string ImagePrompt(string brief) => brief.Trim() + "\n\n" + ImageRules;

    /// <summary>The brief as the GM edited it, or why it can't be used.</summary>
    public static string? Invalid(string? brief) => brief switch
    {
        null or "" => "Write a brief, or ask the Loremaster for one.",
        _ when string.IsNullOrWhiteSpace(brief) => "Write a brief, or ask the Loremaster for one.",
        _ when brief.Trim().Length > AdventureCover.MaxBriefChars => $"A brief can be at most {AdventureCover.MaxBriefChars} characters.",
        _ => null,
    };
}
