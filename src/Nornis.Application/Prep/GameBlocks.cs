using System.Text;
using Markdig;
using Markdig.Syntax;
using Nornis.Domain.Models;

namespace Nornis.Application.Prep;

public enum GameBlockKind
{
    Stat,
    Item,
}

/// <summary>
/// A stat block or an item block (feature 35), parsed from its fence. Every number is the GM's
/// text: nothing here is computed and no label is assumed, so it holds for any game system.
/// </summary>
/// <param name="Groups">The block's lines in groups; the first has no heading and holds the lines before any <c>## Heading</c>.</param>
/// <param name="Description">An item's lines after <c>---</c>; empty for a stat block.</param>
public sealed record GameBlock(
    GameBlockKind Kind,
    string? Name,
    string? Subtitle,
    IReadOnlyList<GameBlockGroup> Groups,
    IReadOnlyList<string> Description)
{
    /// <summary>Every line in every group, in order.</summary>
    public IEnumerable<GameBlockLine> Lines => Groups.SelectMany(g => g.Lines);

    /// <summary>Whether the block holds nothing to print or file.</summary>
    public bool IsEmpty => Name is null && Subtitle is null && !Lines.Any() && Description.Count == 0
        && Groups.All(g => g.Heading is null);
}

public sealed record GameBlockGroup(string? Heading, IReadOnlyList<GameBlockLine> Lines);

/// <summary>An entry ("Label: value"), or a text line when <see cref="Label"/> is null.</summary>
public sealed record GameBlockLine(string? Label, string Value);

/// <summary>
/// The one place Prep's game-block grammar is read (feature 35). The booklet, the block warnings
/// and Export to Nornis all parse through here, so a block means the same thing to each:
///
/// - <c># Name</c> as the first line names the block; <c>*subtitle*</c> straight after it.
///   A first entry labelled <c>Name</c> is read as the name too, as blocks written before the
///   grammar had it.
/// - <c>## Heading</c> starts a group (stat blocks only).
/// - <c>---</c> ends an item's entries; the lines after it are its description.
/// - <c>Label: value</c> is an entry; any other line is text.
/// </summary>
public static class GameBlocks
{
    /// <summary>The longest export line: <c>ArtifactFact.Value</c>'s column, which the line becomes.</summary>
    public const int MaxExportLineChars = 2_000;

    private const string Ellipsis = " …";

    private static readonly MarkdownPipeline FencePipeline = new MarkdownPipelineBuilder().Build();

    public static bool IsGameBlock(string? info) => KindOf(info) is not null;

    public static GameBlockKind? KindOf(string? info) => info?.Trim() switch
    {
        PrepMarkup.StatBlockInfo => GameBlockKind.Stat,
        PrepMarkup.ItemBlockInfo => GameBlockKind.Item,
        _ => null,
    };

    public static GameBlock Parse(GameBlockKind kind, IEnumerable<string> lines)
    {
        string? name = null;
        string? subtitle = null;
        var groups = new List<(string? Heading, List<GameBlockLine> Lines)> { (null, []) };
        var description = new List<string>();
        var inDescription = false;
        var first = true;
        var afterName = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (inDescription)
            {
                description.Add(line);
                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (first && line.StartsWith("# ", StringComparison.Ordinal) && line.Length > 2)
            {
                name = line[2..].Trim();
                first = false;
                afterName = true;
                continue;
            }

            if (afterName && IsSubtitle(line))
            {
                subtitle = line[1..^1].Trim();
                afterName = false;
                continue;
            }

            first = false;
            afterName = false;

            if (kind == GameBlockKind.Stat && line.StartsWith("## ", StringComparison.Ordinal) && line.Length > 3)
            {
                groups.Add((line[3..].Trim(), []));
                continue;
            }

            if (kind == GameBlockKind.Item && line == "---")
            {
                inDescription = true;
                continue;
            }

            var colon = line.IndexOf(':');
            var entry = colon > 0
                ? new GameBlockLine(line[..colon].Trim(), line[(colon + 1)..].Trim())
                : new GameBlockLine(null, line);

            // The feature 29 template wrote the name as an entry; its first one still names the block.
            if (name is null && entry.Label is { } label && label.Equals("Name", StringComparison.OrdinalIgnoreCase)
                && groups.Count == 1 && groups[0].Lines.Count == 0 && entry.Value.Length > 0)
            {
                name = entry.Value;
                afterName = true;
                continue;
            }

            groups[^1].Lines.Add(entry);
        }

        // Trailing blank description lines carry nothing.
        while (description.Count > 0 && description[^1].Length == 0)
        {
            description.RemoveAt(description.Count - 1);
        }

        return new GameBlock(
            kind,
            string.IsNullOrWhiteSpace(name) ? null : name,
            string.IsNullOrWhiteSpace(subtitle) ? null : subtitle,
            groups.Where((g, i) => i == 0 || g.Heading is not null).Select(g => new GameBlockGroup(g.Heading, g.Lines)).ToList(),
            description);
    }

    /// <summary>Parses a fence, or null when its info string isn't a game block's.</summary>
    public static GameBlock? Parse(FencedCodeBlock fence) =>
        KindOf(fence.Info) is { } kind
            ? Parse(kind, fence.Lines.Lines.Take(fence.Lines.Count).Select(l => l.Slice.ToString()))
            : null;

    /// <summary>Every game block in a section's text, in order.</summary>
    public static IReadOnlyList<GameBlock> InMarkdown(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        var document = Markdown.Parse(markdown.Replace("\r\n", "\n"), FencePipeline);
        return document.Descendants<FencedCodeBlock>().Select(Parse).OfType<GameBlock>().ToList();
    }

    /// <summary>
    /// A section's text as Export to Nornis files it (feature 35): each named game block's fence
    /// becomes its <see cref="ExportLineFor"/> line, so the extractor reads one fact; everything
    /// else, unnamed blocks included, is left exactly as written.
    /// </summary>
    public static string WithExportLines(string markdown, string? gameSystem)
    {
        var text = markdown.Replace("\r\n", "\n");
        var document = Markdown.Parse(text, FencePipeline);
        var replacements = document.Descendants<FencedCodeBlock>()
            .Select(fence => (Fence: fence, Line: Parse(fence) is { } block ? ExportLineFor(block, gameSystem) : null))
            .Where(r => r.Line is not null)
            .OrderByDescending(r => r.Fence.Span.Start)
            .ToList();

        var result = new StringBuilder(text);
        foreach (var (fence, line) in replacements)
        {
            // An unclosed fence runs to the end of its container, past where Markdig's span stops.
            var lastLine = fence.Lines.Count > 0 ? fence.Lines.Lines[fence.Lines.Count - 1].Slice.End : 0;
            var end = Math.Min(Math.Max(fence.Span.End, lastLine), text.Length - 1);
            result.Remove(fence.Span.Start, end - fence.Span.Start + 1).Insert(fence.Span.Start, line!.Text);
        }

        return result.ToString();
    }

    /// <summary>
    /// The one line Export to Nornis files a named block as (feature 35): the extractor makes it
    /// one fact on the Artifact it names. Null for an unnamed block, which exports as its lines.
    ///
    /// Whether a line is cut depends on the block alone, never on the game system: the line
    /// keeps <see cref="SystemReserveChars"/> free for the longest system an Adventure can name,
    /// so the block warnings (which run where no system is at hand) and the export can't
    /// disagree. A cut falls after the last whole entry that fits.
    /// </summary>
    public static ExportLine? ExportLineFor(GameBlock block, string? gameSystem)
    {
        if (block.Name is not { } fullName)
        {
            return null;
        }

        var name = fullName.Length <= MaxExportNameChars ? fullName : fullName[..MaxExportNameChars].TrimEnd() + "…";
        var lead = block.Kind == GameBlockKind.Stat ? $"Stat block for \"{name}\"" : $"Item \"{name}\"";
        var system = gameSystem?.Trim() is { Length: > 0 } named
            ? $" ({(named.Length <= MaxSystemChars ? named : named[..MaxSystemChars].TrimEnd())})"
            : "";

        var pieces = Pieces(block);
        var body = new StringBuilder(":");
        var cut = lead.Length + 1 + pieces.Sum(p => p.Length) > MaxBodyChars;
        foreach (var piece in pieces)
        {
            if (cut && lead.Length + body.Length + piece.Length > MaxBodyChars - Ellipsis.Length)
            {
                break;
            }

            body.Append(piece);
        }

        if (cut)
        {
            body.Append(Ellipsis);
        }

        return new ExportLine(lead + system + body, cut);
    }

    /// <summary>The name's longest run in an export line; a longer one is cut, so the head alone can't fill the line.</summary>
    public const int MaxExportNameChars = 200;

    /// <summary>The longest system text an Adventure can name: its game system (or the world's), a space and its edition.</summary>
    private const int MaxSystemChars = AdventureDetails.MaxGameSystemLength + 1 + AdventureDetails.MaxEditionLength;

    /// <summary>What an export line keeps free for the system, " (…)" included.</summary>
    public const int SystemReserveChars = MaxSystemChars + 3;

    private const int MaxBodyChars = MaxExportLineChars - SystemReserveChars;

    private static List<string> Pieces(GameBlock block)
    {
        var pieces = new List<string>();
        if (block.Subtitle is { } subtitle)
        {
            pieces.Add($" *{subtitle}*.");
        }

        foreach (var group in block.Groups)
        {
            var lines = group.Lines.Select(Entry).ToList();
            if (group.Heading is { } heading)
            {
                if (lines.Count == 0)
                {
                    continue;
                }

                lines[0] = $"{heading} — {lines[0]}";
            }

            for (var i = 0; i < lines.Count; i++)
            {
                // The entry's own full stop gives way to the separator, so a line never ends "..".
                pieces.Add(" " + lines[i].TrimEnd('.') + (i == lines.Count - 1 ? "." : ";"));
            }
        }

        if (block.Description.Count > 0)
        {
            pieces.Add(" " + string.Join(' ', block.Description.Where(l => l.Length > 0)));
        }

        return pieces;
    }

    private static string Entry(GameBlockLine line) =>
        line.Label is { } label ? $"{label}: {line.Value}".TrimEnd() : line.Value;

    private static bool IsSubtitle(string line) =>
        line.Length > 2 && line[0] == '*' && line[^1] == '*' && line[1] != '*' && line[^2] != '*';
}

/// <summary>A named block's export line, and whether it had to be cut to fit a fact.</summary>
public sealed record ExportLine(string Text, bool WasCut);
