using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nornis.Domain.Entities;
using Nornis.Domain.Models;

namespace Nornis.Application.Prep;

/// <summary>
/// Prints an Adventure (feature 34): resolves its print details, builds the booklet and renders
/// the PDF. The preview route and one-click filing both come through here, so the GM files
/// exactly what they previewed, and nothing else calls the model or the renderer.
/// </summary>
public class AdventureBookletPrinter
{
    private readonly AdventurePrintDetails _printDetails;
    private readonly IBookletPdfRenderer _renderer;
    private readonly ILogger<AdventureBookletPrinter> _logger;
    private readonly TimeProvider _time;

    public AdventureBookletPrinter(
        AdventurePrintDetails printDetails, IBookletPdfRenderer renderer, ILogger<AdventureBookletPrinter> logger, TimeProvider time)
    {
        _printDetails = printDetails;
        _renderer = renderer;
        _logger = logger;
        _time = time;
    }

    /// <summary>The details a print would carry now, for the Book details card's preview.</summary>
    public Task<PrintDetails> ResolveDetailsAsync(Adventure adventure, CancellationToken ct) =>
        _printDetails.ResolveAsync(adventure, ct);

    public async Task<PrintedBooklet> PrintAsync(Adventure adventure, CancellationToken ct)
    {
        var details = await _printDetails.ResolveAsync(adventure, ct);
        var started = Stopwatch.GetTimestamp();
        var imprint = new BookletImprint(DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime), RightsStatement.CurrentVersion);
        var pdf = _renderer.Render(BookletModel.Build(adventure, details, imprint));

        _logger.LogInformation(
            "booklet_rendered {AdventureId} pages={Pages} bytes={Bytes} ms={Milliseconds}",
            adventure.Id, pdf.Pages, pdf.Bytes.Length, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return new PrintedBooklet(pdf.Bytes, details, FileName(adventure.Title));
    }

    /// <summary>The title as a file name: the characters no file system takes become spaces.</summary>
    public static string FileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var name = string.Join(' ', new string(title.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return (name.Length == 0 ? "booklet" : name) + ".pdf";
    }
}

/// <summary>A printed booklet: the PDF, the details it was printed with, and its file name.</summary>
public sealed record PrintedBooklet(byte[] Pdf, PrintDetails Details, string FileName);
