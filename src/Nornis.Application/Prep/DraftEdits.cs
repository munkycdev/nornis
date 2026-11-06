using System.Text;
using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>One change the Loremaster suggests to the design draft: an exact passage and what replaces it.</summary>
/// <param name="Find">The passage as it stands in the draft. Empty means the whole draft (feature 41).</param>
/// <param name="Replace">Its replacement. Empty deletes the passage.</param>
public sealed record DraftEdit(string Find, string Replace);

/// <summary>
/// The one reader of suggested edits (feature 37). An answer in Design mode writes each change as
/// a fence:
/// <code>
/// ````edit
/// &lt;&lt;&lt;
/// the exact passage, copied from the draft
/// ===
/// its replacement
/// &gt;&gt;&gt;
/// ````
/// </code>
/// Four backticks, as the prompt teaches since feature 41, so a stat block's own three-backtick
/// fence can sit inside; three or more are read, and the closing line must match the opening's.
/// An answer is stored as written; these are parsed whenever it is shown or applied, so the client
/// can never send its own text to apply. A fence that doesn't follow the shape is not an edit,
/// and stays in the answer's prose for the GM to read.
/// </summary>
public static class DraftEdits
{
    /// <summary>The fence the prompt teaches: four backticks, so the text inside may hold a three-backtick block.</summary>
    public const string FenceOpen = "````edit";
    public const string FindMarker = "<<<";
    public const string Divider = "===";
    public const string EndMarker = ">>>";
    private const string FenceInfo = "edit";

    /// <summary>The convention as the prompt teaches it.</summary>
    public const string ConventionForPrompt = """
        ````edit
        <<<
        the exact passage, copied from the draft, whole lines
        ===
        its replacement
        >>>
        ````
        """;

    /// <summary>The answer's edits, in the order written.</summary>
    public static IReadOnlyList<DraftEdit> Parse(string answer) => Read(answer).Edits;

    /// <summary>The answer with its edit fences taken out, for showing around the edit cards. Null when it has none.</summary>
    public static string? ProseOf(string answer)
    {
        var (edits, prose) = Read(answer);
        return edits.Count == 0 ? null : prose;
    }

    /// <summary>
    /// The draft with one edit applied, or null when the passage isn't in the draft exactly once
    /// (so the edit no longer fits). An empty passage is the whole draft, empty or not (feature 41):
    /// the replacement becomes the draft.
    /// </summary>
    public static string? Apply(string draft, DraftEdit edit)
    {
        var text = Normalise(draft);
        if (edit.Find.Length == 0)
        {
            return edit.Replace;
        }

        var at = text.IndexOf(edit.Find, StringComparison.Ordinal);
        if (at < 0 || text.IndexOf(edit.Find, at + 1, StringComparison.Ordinal) >= 0)
        {
            return null;
        }

        return string.Concat(text.AsSpan(0, at), edit.Replace, text.AsSpan(at + edit.Find.Length));
    }

    /// <summary>
    /// Every edit applied in order, each to the text the one before left (feature 41). An edit
    /// that doesn't fit leaves the text as it was and is <see cref="DraftEditOutcome.DidNotFit"/>;
    /// the rest still apply.
    /// </summary>
    public static (string Draft, IReadOnlyList<DraftEditOutcome> Outcomes) ApplyAll(string draft, IReadOnlyList<DraftEdit> edits)
    {
        var text = Normalise(draft);
        var outcomes = new List<DraftEditOutcome>(edits.Count);
        foreach (var edit in edits)
        {
            if (Apply(text, edit) is { } changed)
            {
                text = changed;
                outcomes.Add(DraftEditOutcome.Applied);
            }
            else
            {
                outcomes.Add(DraftEditOutcome.DidNotFit);
            }
        }

        return (text, outcomes);
    }

    /// <summary>
    /// True when an edit fence was opened and never closed: the answer stopped partway through
    /// writing it, so it was cut off however the provider ended the stream (feature 41).
    /// </summary>
    public static bool HasUnclosedFence(string answer)
    {
        string? open = null;
        foreach (var line in Normalise(answer).Split('\n'))
        {
            var trimmed = line.Trim();
            if (open is null)
            {
                open = OpeningTicks(trimmed);
            }
            else if (trimmed == open)
            {
                open = null;
            }
        }

        return open is not null;
    }

    /// <summary>
    /// The backticks an edit fence opens with, when <paramref name="trimmedLine"/> opens one: three
    /// or more, then <c>edit</c>. Null for any other line. The closing line is the same backticks.
    /// </summary>
    public static string? OpeningTicks(string trimmedLine)
    {
        var ticks = 0;
        while (ticks < trimmedLine.Length && trimmedLine[ticks] == '`')
        {
            ticks++;
        }

        return ticks >= 3 && trimmedLine.AsSpan(ticks).SequenceEqual(FenceInfo) ? trimmedLine[..ticks] : null;
    }

    /// <summary>
    /// One edit as a line of the conversation's history (feature 41): what it changed and how long
    /// the new text is, never the text itself, which the draft already carries in Design mode.
    /// </summary>
    public static string Describe(DraftEdit edit, DraftEditOutcome? outcome)
    {
        var what = edit.Find.Length == 0
            ? $"wrote the whole draft, {edit.Replace.Length} characters"
            : edit.Replace.Length == 0
                ? $"took out \"{Opening(edit.Find)}\""
                : $"replaced \"{Opening(edit.Find)}\" with {edit.Replace.Length} characters starting \"{Opening(edit.Replace)}\"";
        var how = outcome switch
        {
            DraftEditOutcome.Applied => " (applied)",
            DraftEditOutcome.DidNotFit => " (didn't fit, not applied)",
            DraftEditOutcome.NotApplied => " (not applied)",
            null => "",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "An unclassified edit outcome."),
        };
        return $"[Edit to the draft: {what}{how}]";
    }

    /// <summary>The first words of a passage, on one line, for <see cref="Describe"/>.</summary>
    private static string Opening(string text)
    {
        const int MaxChars = 60;
        var line = string.Join(' ', text.Split((char[])['\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MaxChars ? line : line[..MaxChars].TrimEnd() + "…";
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static (IReadOnlyList<DraftEdit> Edits, string Prose) Read(string answer)
    {
        var lines = Normalise(answer).Split('\n');
        var edits = new List<DraftEdit>();
        var prose = new StringBuilder();

        var i = 0;
        while (i < lines.Length)
        {
            if (OpeningTicks(lines[i].Trim()) is { } ticks && TryReadFence(lines, i, ticks, out var edit, out var next))
            {
                edits.Add(edit);
                i = next;
                continue;
            }

            prose.Append(lines[i]).Append('\n');
            i++;
        }

        return (edits, CollapseBlankRuns(prose.ToString()).Trim());
    }

    /// <summary>
    /// Reads one fence from its opening line; false, consuming nothing, when it doesn't follow the
    /// shape. The text inside may hold any line but the markers, including a stat block's own
    /// fence (feature 41); a line that opens another edit means this one never ended, so it isn't one.
    /// </summary>
    private static bool TryReadFence(string[] lines, int open, string ticks, out DraftEdit edit, out int next)
    {
        edit = null!;
        next = open;
        var i = open + 1;
        if (i >= lines.Length || lines[i].Trim() != FindMarker)
        {
            return false;
        }

        i++;
        var find = new List<string>();
        while (i < lines.Length && lines[i].Trim() != Divider)
        {
            if (lines[i].Trim() == EndMarker || OpeningTicks(lines[i].Trim()) is not null)
            {
                return false;
            }

            find.Add(lines[i]);
            i++;
        }

        if (i >= lines.Length)
        {
            return false;
        }

        i++;
        var replace = new List<string>();
        while (i < lines.Length && lines[i].Trim() != EndMarker)
        {
            if (OpeningTicks(lines[i].Trim()) is not null)
            {
                return false;
            }

            replace.Add(lines[i]);
            i++;
        }

        if (i >= lines.Length)
        {
            return false;
        }

        i++;
        if (i >= lines.Length || lines[i].Trim() != ticks)
        {
            return false;
        }

        edit = new DraftEdit(string.Join('\n', find), string.Join('\n', replace));
        next = i + 1;
        return true;
    }

    private static string CollapseBlankRuns(string text)
    {
        var sb = new StringBuilder(text.Length);
        var blanks = 0;
        foreach (var line in text.Split('\n'))
        {
            blanks = line.Trim().Length == 0 ? blanks + 1 : 0;
            if (blanks <= 1)
            {
                sb.Append(line).Append('\n');
            }
        }

        return sb.ToString();
    }
}
