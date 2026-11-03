using System.Globalization;
using System.Text;
using System.Text.Json;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>
/// The prompt for the Loremaster's split plan (feature 37). The draft goes with its line numbers,
/// and the answer is only where sections start, their titles and their kinds, never text: the
/// cut is code's, so the Loremaster can't change a word by planning.
/// </summary>
public static class AdventureDraftPlanPrompt
{
    public static readonly string SystemPrompt = $$"""
        You divide a game master's adventure draft into the sections of their Prep outline. You
        decide only where each section starts, what it is called and what kind it is. You never
        write or change the draft's text; code cuts it at the lines you name.

        ## What you are given
        - The game system, the characters' levels and a brief on the table, when the GM gave one.
        - The world's record and reference passages, for context only.
        - The draft, one line per row, each starting with its line number and a bar.

        ## Kinds of section
        - Overview: the pitch, the background and what the GM needs to know first. Usually first.
        - SessionClock: a timeline or countdown of what happens if the players do nothing.
        - Chapter: a divider that groups the scenes after it.
        - Scene: one place or situation the players play through. Most sections are scenes.
        - QuickReference: names, stat blocks or tables to look up at the table.
        - Notes: anything else.

        ## Rules
        - The first section starts at the first line with text.
        - Start lines go down the draft in order, and every line belongs to the section above it.
        - Start a section on a heading line when there is one; use the heading's own words as its
          title. Elsewhere, start at the line where the new part begins and give a short title.
        - Keep scenes whole. Never start a section inside a fenced block (between ``` lines).
        - Titles are at most {{AdventureDraftPlan.MaxTitleChars}} characters.

        ## Format
        Answer with a JSON array and nothing else, one object per section, in order:
        [{"startLine": 1, "title": "Overview", "kind": "Overview"}, {"startLine": 14, "title": "The Baths", "kind": "Scene"}]
        """;

    public static string BuildUserMessage(PrepPromptContext context, Adventure adventure, IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        context.AppendTo(sb);

        sb.AppendLine($"## The adventure: {adventure.Title}");
        sb.AppendLine();
        sb.AppendLine("## The draft, numbered");
        sb.AppendLine();
        var width = lines.Count.ToString(CultureInfo.InvariantCulture).Length;
        for (var i = 0; i < lines.Count; i++)
        {
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width)).Append("| ").AppendLine(lines[i]);
        }

        sb.AppendLine();
        sb.AppendLine("Answer with the plan as a JSON array.");
        return sb.ToString();
    }

    /// <summary>The plan the answer names, or null when it isn't a JSON array of start lines, titles and known kinds.</summary>
    public static IReadOnlyList<DraftSplitPoint>? ParseAnswer(string answer)
    {
        var start = answer.IndexOf('[', StringComparison.Ordinal);
        var end = answer.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(answer[start..(end + 1)]);
            var points = new List<DraftSplitPoint>();
            foreach (var row in json.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object
                    || !row.TryGetProperty("startLine", out var line) || line.ValueKind != JsonValueKind.Number
                    || !line.TryGetInt32(out var startLine)
                    || !row.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String
                    || !row.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
                    || !Enum.TryParse<AdventureSectionKind>(kind.GetString(), ignoreCase: true, out var parsed)
                    || !Enum.IsDefined(parsed)
                    || int.TryParse(kind.GetString(), out _))
                {
                    return null;
                }

                points.Add(new DraftSplitPoint(startLine, title.GetString()!.Trim(), parsed));
            }

            return points;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
