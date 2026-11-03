using System.Globalization;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Prep;

/// <summary>A file handed to Import Markdown: its name, for a fallback title, and its text.</summary>
public record MarkdownFile(string Name, string Markdown);

public record ImportedSection(AdventureSectionKind Kind, string Title, string BodyMarkdown);

/// <summary>
/// The split. <see cref="Details"/> and <see cref="IgnoredDetails"/> come from the first file's
/// front matter (feature 32): the details it set, and the keys it named that were left out.
/// </summary>
public record ImportedAdventure(
    string Title, IReadOnlyList<ImportedSection> Sections, AdventureDetails Details, IReadOnlyList<string> IgnoredDetails);

/// <summary>
/// Turns Markdown files into an Adventure's sections, with no AI involved. The files are read
/// in the order given and split on their top-level <c>#</c> headings:
///
/// - the first file's first <c>#</c> is the Adventure's title;
/// - a <c>#</c> repeating that title (ignoring case and spacing) starts a Chapter, titled by
///   the <c>##</c> right under it when there is one;
/// - a <c>#</c> beginning "Scene" starts a Scene, and any other starts Notes;
/// - text before a file's first <c>#</c> becomes Notes titled after the file.
///
/// The first file may open with front matter (feature 32): a <c>---</c> line, flat
/// <c>key: value</c> lines, and a closing <c>---</c> within <see cref="MaxFrontMatterLines"/>
/// lines. Its <c>system</c> (or <c>game_system</c>), <c>edition</c>, <c>levels</c> and
/// <c>year</c> become the Adventure's details, and the block is not part of any body. Any
/// other key, and any value outside its bound, is left out and reported, never an error. A
/// later file's block is ordinary text. No YAML library: the block is a flat list, and a YAML
/// parser would accept far more than this maps.
///
/// Headings are found by Markdig, so a <c>#</c> inside a code fence or a quote doesn't split.
/// A body is the file's text between its heading and the next, byte for byte, except that
/// line endings become <c>\n</c> and the blank lines at either end are dropped, which is what
/// the editor's own save produces.
/// </summary>
public static partial class AdventureMarkdownImporter
{
    private const int MaxTitleChars = 200;

    /// <summary>How far down the first file the closing <c>---</c> may be.</summary>
    public const int MaxFrontMatterLines = 50;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\s{0,3}#{1,6}(?:\s+|$)")]
    private static partial Regex AtxOpening();

    [GeneratedRegex(@"\s+#+\s*$")]
    private static partial Regex AtxClosing();

    /// <summary>The split, or the error naming what was too large or missing.</summary>
    public static (ImportedAdventure? Adventure, string? Error) Import(IReadOnlyList<MarkdownFile> files, int currentYear)
    {
        if (files.Count == 0 || files.All(f => string.IsNullOrWhiteSpace(f.Markdown)))
        {
            return (null, "Choose at least one Markdown file with something in it.");
        }

        string? title = null;
        var sections = new List<ImportedSection>();
        var chapterNumber = 0;
        var details = new AdventureDetails();
        IReadOnlyList<string> ignored = [];

        for (var f = 0; f < files.Count; f++)
        {
            var file = files[f];
            var text = (file.Markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            if (f == 0)
            {
                (text, details, ignored) = ReadFrontMatter(text, currentYear);
            }

            var document = Markdown.Parse(text);
            var blocks = document.ToList();
            var headings = blocks
                .Select((block, index) => (Block: block, Index: index))
                .Where(b => b.Block is HeadingBlock { Level: 1 })
                .ToList();

            var fileTitle = FileTitle(file.Name);
            title ??= headings.Count > 0 ? HeadingText(text, (HeadingBlock)headings[0].Block) : fileTitle;

            var preambleEnd = headings.Count > 0 ? headings[0].Block.Span.Start : text.Length;
            AddIfNotBlank(sections, AdventureSectionKind.Notes, fileTitle, text[..preambleEnd]);

            for (var h = 0; h < headings.Count; h++)
            {
                var heading = (HeadingBlock)headings[h].Block;
                var headingText = HeadingText(text, heading);
                var bodyStart = LineEnd(text, heading.Span.End);
                var bodyEnd = h + 1 < headings.Count ? headings[h + 1].Block.Span.Start : text.Length;

                if (SameTitle(headingText, title))
                {
                    chapterNumber++;
                    var chapterTitle = $"Chapter {chapterNumber}";

                    // A chapter's own name is the ## directly under the repeated title.
                    if (headings[h].Index + 1 < blocks.Count
                        && blocks[headings[h].Index + 1] is HeadingBlock { Level: 2 } subtitle
                        && subtitle.Span.Start < bodyEnd)
                    {
                        chapterTitle = HeadingText(text, subtitle);
                        bodyStart = LineEnd(text, subtitle.Span.End);
                    }

                    sections.Add(new ImportedSection(
                        AdventureSectionKind.Chapter, chapterTitle, Body(text, bodyStart, bodyEnd)));
                    continue;
                }

                var kind = headingText.StartsWith("Scene", StringComparison.OrdinalIgnoreCase)
                    ? AdventureSectionKind.Scene
                    : AdventureSectionKind.Notes;
                sections.Add(new ImportedSection(kind, headingText, Body(text, bodyStart, bodyEnd)));
            }
        }

        var adventureTitle = title ?? FileTitle(files[0].Name);
        var longest = sections.Select(s => s.Title.Length).Append(adventureTitle.Length).Max();
        if (longest > MaxTitleChars)
        {
            return (null, $"A heading is {longest:N0} characters; headings can be at most {MaxTitleChars:N0}.");
        }

        return (new ImportedAdventure(adventureTitle, sections, details, ignored), null);
    }

    /// <summary>
    /// The text without its front matter, the details the block set, and the keys it named that
    /// were left out. Text with no block, or with an opening <c>---</c> that never closes within
    /// <see cref="MaxFrontMatterLines"/> lines, comes back unchanged with no details.
    /// </summary>
    public static (string Text, AdventureDetails Details, IReadOnlyList<string> Ignored) ReadFrontMatter(string text, int currentYear)
    {
        var lines = text.Split('\n');
        if (lines[0].Trim() != "---")
        {
            return (text, new AdventureDetails(), []);
        }

        var close = -1;
        for (var i = 1; i < Math.Min(lines.Length, MaxFrontMatterLines + 1); i++)
        {
            if (lines[i].Trim() == "---")
            {
                close = i;
                break;
            }
        }

        if (close < 0)
        {
            return (text, new AdventureDetails(), []);
        }

        string? gameSystem = null, edition = null, levels = null;
        int? year = null;
        var keys = new Dictionary<string, string>();
        var ignored = new List<string>();
        for (var i = 1; i < close; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            var key = (colon < 0 ? line : line[..colon]).Trim();
            var value = colon < 0 ? "" : Unquote(line[(colon + 1)..].Trim());
            switch (key.ToLowerInvariant().Replace("-", "").Replace("_", ""))
            {
                case "system" or "gamesystem":
                    gameSystem = value;
                    keys[nameof(AdventureDetails.GameSystem)] = key;
                    break;
                case "edition":
                    edition = value;
                    keys[nameof(AdventureDetails.Edition)] = key;
                    break;
                case "levels":
                    levels = value;
                    keys[nameof(AdventureDetails.Levels)] = key;
                    break;
                case "year":
                    keys[nameof(AdventureDetails.Year)] = key;
                    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                    {
                        year = parsed;
                    }
                    else if (value.Length > 0)
                    {
                        ignored.Add(key);
                    }

                    break;
                default:
                    ignored.Add(key.Length > MaxIgnoredKeyChars ? key[..MaxIgnoredKeyChars] : key);
                    break;
            }
        }

        // A value outside its bound is dropped, not refused, so an import never fails over its
        // front matter. The same Validate as the details route, one field at a time.
        var details = AdventureDetails.Create(gameSystem, edition, levels, year);
        while (details.Validate(currentYear) is { } field)
        {
            ignored.Add(keys[field]);
            details = field switch
            {
                nameof(AdventureDetails.GameSystem) => details with { GameSystem = null },
                nameof(AdventureDetails.Edition) => details with { Edition = null },
                nameof(AdventureDetails.Levels) => details with { Levels = null },
                _ => details with { Year = null },
            };
        }

        return (string.Join('\n', lines.Skip(close + 1)), details, ignored);
    }

    /// <summary>A reported key is cut to this, so a block of junk can't make the response long.</summary>
    private const int MaxIgnoredKeyChars = 40;

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0] ? value[1..^1].Trim() : value;

    private static void AddIfNotBlank(List<ImportedSection> sections, AdventureSectionKind kind, string title, string text)
    {
        var body = Body(text, 0, text.Length);
        if (body.Length > 0)
        {
            sections.Add(new ImportedSection(kind, title, body));
        }
    }

    private static string Body(string text, int start, int end) =>
        start >= end ? "" : text[start..end].Trim('\n');

    /// <summary>The index just past the newline that ends the line containing <paramref name="position"/>.</summary>
    private static int LineEnd(string text, int position)
    {
        var newline = text.IndexOf('\n', Math.Min(position, text.Length));
        return newline < 0 ? text.Length : newline + 1;
    }

    /// <summary>The heading's text as written: its first line, without the ATX #s.</summary>
    private static string HeadingText(string text, HeadingBlock heading)
    {
        var raw = text.Substring(heading.Span.Start, heading.Span.Length);
        var firstLine = raw.Split('\n')[0];
        var stripped = AtxClosing().Replace(AtxOpening().Replace(firstLine, ""), "");
        return stripped.Trim();
    }

    private static bool SameTitle(string a, string b) =>
        string.Equals(Whitespace().Replace(a.Trim(), " "), Whitespace().Replace(b.Trim(), " "),
            StringComparison.OrdinalIgnoreCase);

    private static string FileTitle(string name)
    {
        var title = Path.GetFileNameWithoutExtension(name ?? "").Trim();
        return title.Length > 0 ? title : "Imported notes";
    }
}
