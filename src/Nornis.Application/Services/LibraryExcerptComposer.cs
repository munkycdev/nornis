using System.Text;
using Nornis.Domain.Models;

namespace Nornis.Application.Services;

/// <summary>
/// The one place an excerpt's title and body take their shape. The body opens with a filing
/// line — which document, which pages, and the codex entry it was filed for when there is
/// one — followed by the passages in reading order. Nothing parses the body back; the source
/// row carries the document and pages as columns, and the extraction prompt describes the
/// filing line by reference to this class.
///
/// The entry is named, not identified: an id would go stale on merge or removal, while the
/// name is exactly what name-matching finds to put the entry into extraction's existing-
/// artifacts context, where the dedup rule already sends proposals at the listed id.
/// </summary>
public static class LibraryExcerptComposer
{
    /// <summary>Same ceiling as every source title; the composed title is cut to fit.</summary>
    public const int MaxTitleLength = 200;

    public static string BuildTitle(string? artifactName, string documentTitle, int pageFrom, int pageTo)
    {
        var pages = FormatPages(pageFrom, pageTo);
        var title = string.IsNullOrWhiteSpace(artifactName)
            ? $"{documentTitle}, {pages}"
            : $"{artifactName.Trim()} — {documentTitle}, {pages}";

        return title.Length <= MaxTitleLength
            ? title
            : title[..(MaxTitleLength - 1)] + "…";
    }

    /// <summary>
    /// The filing line, then each passage. The chunker carries a trailing overlap into the
    /// next chunk so retrieval hits read whole; laid end to end that overlap would repeat, so
    /// it is stripped from a chunk that verifiably starts with the previous chunk's tail — and
    /// left alone when it does not, because a guess would cut real text.
    /// </summary>
    public static string BuildBody(
        string? artifactName,
        string documentTitle,
        int pageFrom,
        int pageTo,
        IReadOnlyList<LibraryChunkHit> chunksInOrder,
        int overlapChars)
    {
        var sb = new StringBuilder();
        sb.Append(BuildFilingLine(artifactName, documentTitle, pageFrom, pageTo));

        string? previous = null;
        foreach (var chunk in chunksInOrder)
        {
            var text = chunk.Text;
            if (previous is not null && overlapChars > 0 && previous.Length > overlapChars)
            {
                var tail = previous[^overlapChars..];
                if (text.StartsWith(tail, StringComparison.Ordinal))
                {
                    text = text[overlapChars..].TrimStart();
                }
            }

            sb.Append("\n\n").Append(text.Trim());
            previous = chunk.Text;
        }

        return sb.ToString();
    }

    public static string BuildFilingLine(string? artifactName, string documentTitle, int pageFrom, int pageTo)
    {
        var pages = FormatPages(pageFrom, pageTo);
        return string.IsNullOrWhiteSpace(artifactName)
            ? $"Excerpt from “{documentTitle}”, {pages}."
            : $"Excerpt from “{documentTitle}”, {pages}, filed for the codex entry “{artifactName.Trim()}”.";
    }

    private static string FormatPages(int pageFrom, int pageTo) =>
        pageFrom == pageTo ? $"p. {pageFrom}" : $"pp. {pageFrom}–{pageTo}";
}
