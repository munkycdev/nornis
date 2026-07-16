using System.Text;
using Nornis.Application.Storage;

namespace Nornis.Application.Services;

/// <summary>A chunk ready for embedding: position, starting page, and text.</summary>
public sealed record TextChunk(int Ord, int Page, string Text);

/// <summary>
/// Splits per-page PDF text into overlapping retrieval chunks. Pure logic: paragraphs are
/// packed into ~maxChars windows, a trailing overlap carries context across boundaries, and
/// each chunk remembers the page it started on for "Title, p. N" citations.
/// </summary>
public static class LibraryTextChunker
{
    public static IReadOnlyList<TextChunk> Chunk(
        IReadOnlyList<PdfPageText> pages,
        int maxChars,
        int overlapChars)
    {
        var chunks = new List<TextChunk>();
        var buffer = new StringBuilder();
        var bufferStartPage = 0;
        var ord = 0;

        void Emit()
        {
            var text = buffer.ToString().Trim();
            if (text.Length > 0)
            {
                chunks.Add(new TextChunk(ord++, bufferStartPage, text));
            }

            // Seed the next chunk with the tail of this one so passages spanning a
            // boundary stay findable.
            var overlap = overlapChars > 0 && text.Length > overlapChars
                ? text[^overlapChars..]
                : string.Empty;
            buffer.Clear();
            buffer.Append(overlap);
        }

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text))
            {
                continue;
            }

            foreach (var paragraph in SplitIntoUnits(page.Text, maxChars))
            {
                if (buffer.Length == 0 || buffer.ToString().Trim().Length == 0)
                {
                    bufferStartPage = page.Number;
                }
                else if (bufferStartPage == 0)
                {
                    bufferStartPage = page.Number;
                }

                if (buffer.Length > 0 && buffer.Length + paragraph.Length + 1 > maxChars)
                {
                    Emit();
                    bufferStartPage = page.Number;
                }

                if (buffer.Length > 0)
                {
                    buffer.Append('\n');
                }
                buffer.Append(paragraph);
            }
        }

        if (buffer.ToString().Trim().Length > 0)
        {
            Emit();
        }

        return chunks;
    }

    /// <summary>Paragraphs, with any single paragraph longer than maxChars hard-split.</summary>
    private static IEnumerable<string> SplitIntoUnits(string pageText, int maxChars)
    {
        var paragraphs = pageText.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length <= maxChars)
            {
                yield return paragraph;
                continue;
            }

            for (var i = 0; i < paragraph.Length; i += maxChars)
            {
                yield return paragraph.Substring(i, Math.Min(maxChars, paragraph.Length - i));
            }
        }
    }
}
