namespace Nornis.Application.Storage;

/// <summary>One page of extracted PDF text; <paramref name="Number"/> is 1-based.</summary>
public sealed record PdfPageText(int Number, string Text);

/// <summary>Extracts per-page text from a PDF stream (digital PDFs only — no OCR).</summary>
public interface IPdfTextExtractor
{
    Task<IReadOnlyList<PdfPageText>> ExtractPagesAsync(Stream pdfStream, CancellationToken ct);
}
