using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Prep;

namespace Nornis.Application.Tests.Fakes;

/// <summary>An <see cref="AdventureBookletPrinter"/> over a fake renderer, for fixtures that view or print Adventures.</summary>
public static class TestBooklets
{
    public static AdventureBookletPrinter Printer(
        AdventurePrintDetails details, FakeBookletPdfRenderer? renderer = null, TimeProvider? time = null,
        FakeBlobStorageService? blobs = null) =>
        new(details, renderer ?? new FakeBookletPdfRenderer(), NullLogger<AdventureBookletPrinter>.Instance, time ?? TimeProvider.System,
            blobs ?? new FakeBlobStorageService());
}

/// <summary>
/// Records each booklet it is asked to lay out and returns bytes that name the booklet's title,
/// so a test can tell which booklet a stored PDF came from. <see cref="Throw"/> makes it fail.
/// </summary>
public sealed class FakeBookletPdfRenderer : IBookletPdfRenderer
{
    public List<Booklet> Rendered { get; } = [];

    public bool Throw { get; set; }

    /// <summary>The size of the PDF it returns; the default is small.</summary>
    public int Size { get; set; } = 64;

    public static byte[] BytesFor(Booklet booklet, int size)
    {
        var head = System.Text.Encoding.UTF8.GetBytes($"%PDF-fake {booklet.Cover.Title}\n");
        var bytes = new byte[Math.Max(size, head.Length)];
        head.CopyTo(bytes, 0);
        return bytes;
    }

    public BookletPdf Render(Booklet booklet)
    {
        if (Throw)
        {
            throw new InvalidOperationException("The renderer failed.");
        }

        Rendered.Add(booklet);
        return new BookletPdf(BytesFor(booklet, Size), 1);
    }

    public List<BookletProps> RenderedProps { get; } = [];

    public BookletPdf RenderProps(BookletProps props)
    {
        RenderedProps.Add(props);
        return new BookletPdf(System.Text.Encoding.UTF8.GetBytes($"%PDF-fake props {props.Title}\n"), props.Props.Count);
    }
}
