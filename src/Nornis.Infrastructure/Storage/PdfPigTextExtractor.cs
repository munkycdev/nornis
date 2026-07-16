using Nornis.Application.Storage;
using UglyToad.PdfPig;

namespace Nornis.Infrastructure.Storage;

/// <summary>Per-page text extraction via PdfPig. Digital PDFs only — image-only pages yield
/// empty text and the chunker skips them.</summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public async Task<IReadOnlyList<PdfPageText>> ExtractPagesAsync(Stream pdfStream, CancellationToken ct)
    {
        // PdfPig needs a seekable stream; blob read streams aren't.
        using var buffer = new MemoryStream();
        await pdfStream.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var pages = new List<PdfPageText>();
        using var document = PdfDocument.Open(buffer);
        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            pages.Add(new PdfPageText(page.Number, page.Text));
        }

        return pages;
    }
}
