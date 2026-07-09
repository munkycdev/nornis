using System.Text.RegularExpressions;

namespace Nornis.Application.Ai;

/// <summary>
/// Cleans up notes imported from external note-taking systems before extraction.
/// Exports carry YAML frontmatter and UUID-based wikilink markup; this reduces every
/// link form to a plain <c>[[Label]]</c> marker so the AI sees clean text with the
/// link signal preserved. Applied transiently at extraction time — the stored source
/// body remains the raw imported record.
/// </summary>
public static partial class ImportedNoteNormalizer
{
    public static string Normalize(string body)
    {
        var text = FrontMatter().Replace(body, string.Empty);

        // [[<uuid>|Label]] → [[Label]]
        text = UuidPipedLink().Replace(text, "[[$1]]");

        // [[[[Label]]#^blockref|Alias]] → [[Alias]] (link to a block inside another
        // note, displayed under its own name — the alias is the entity indicated)
        text = BlockRefAliasedLink().Replace(text, "[[$2]]");

        // [[[[Label]]]] → [[Label]] (export doubles brackets around resolved links);
        // loop for deeper accidental nesting
        string previous;
        do
        {
            previous = text;
            text = DoubledBrackets().Replace(text, "$1");
        } while (text != previous);

        // Any remaining [[target|Alias]] → [[Alias]]
        text = PipedLink().Replace(text, "[[$1]]");

        return text.Trim();
    }

    [GeneratedRegex(@"\A---\s*\r?\n.*?\r?\n---\s*\r?\n", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();

    [GeneratedRegex(@"\[\[[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\|([^\[\]|]+)\]\]")]
    private static partial Regex UuidPipedLink();

    [GeneratedRegex(@"\[\[\[\[([^\[\]|]+)\]\]#\^[^\[\]|]*\|([^\[\]|]+)\]\]")]
    private static partial Regex BlockRefAliasedLink();

    [GeneratedRegex(@"\[\[(\[\[[^\[\]]+\]\])\]\]")]
    private static partial Regex DoubledBrackets();

    [GeneratedRegex(@"\[\[[^\[\]|]+\|([^\[\]|]+)\]\]")]
    private static partial Regex PipedLink();
}
