using System.Text;

namespace Nornis.Application.Prep;

/// <summary>One change the Loremaster suggests to the design draft: an exact passage and what replaces it.</summary>
/// <param name="Find">The passage as it stands in the draft. Empty only for a draft that is empty.</param>
/// <param name="Replace">Its replacement. Empty deletes the passage.</param>
public sealed record DraftEdit(string Find, string Replace);

/// <summary>
/// The one reader of suggested edits (feature 37). An answer in Design mode writes each change as
/// a fence:
/// <code>
/// ```edit
/// &lt;&lt;&lt;
/// the exact passage, copied from the draft
/// ===
/// its replacement
/// &gt;&gt;&gt;
/// ```
/// </code>
/// An answer is stored as written; these are parsed whenever it is shown or applied, so the client
/// can never send its own text to apply. A fence that doesn't follow the shape is not an edit,
/// and stays in the answer's prose for the GM to read.
/// </summary>
public static class DraftEdits
{
    public const string FenceOpen = "```edit";
    public const string FindMarker = "<<<";
    public const string Divider = "===";
    public const string EndMarker = ">>>";
    private const string FenceClose = "```";

    /// <summary>The convention as the prompt teaches it.</summary>
    public const string ConventionForPrompt = """
        ```edit
        <<<
        the exact passage, copied from the draft, whole lines
        ===
        its replacement
        >>>
        ```
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
    /// (so the edit no longer fits). An empty passage fits only an empty draft, and replaces it whole.
    /// </summary>
    public static string? Apply(string draft, DraftEdit edit)
    {
        var text = Normalise(draft);
        if (edit.Find.Length == 0)
        {
            return text.Trim().Length == 0 ? edit.Replace : null;
        }

        var at = text.IndexOf(edit.Find, StringComparison.Ordinal);
        if (at < 0 || text.IndexOf(edit.Find, at + 1, StringComparison.Ordinal) >= 0)
        {
            return null;
        }

        return string.Concat(text.AsSpan(0, at), edit.Replace, text.AsSpan(at + edit.Find.Length));
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
            if (lines[i].Trim() == FenceOpen && TryReadFence(lines, i, out var edit, out var next))
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

    /// <summary>Reads one fence from its opening line; false, consuming nothing, when it doesn't follow the shape.</summary>
    private static bool TryReadFence(string[] lines, int open, out DraftEdit edit, out int next)
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
            if (lines[i].Trim() is FenceClose or EndMarker)
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
            if (lines[i].Trim() == FenceClose)
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
        if (i >= lines.Length || lines[i].Trim() != FenceClose)
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
