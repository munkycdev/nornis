namespace Nornis.Application.Prep;

/// <summary>
/// Lays a <see cref="Booklet"/> out as a PDF (feature 34). Infrastructure's QuestPDF renderer is
/// the one implementation, and every layout rule lives there; <see cref="AdventureBookletPrinter"/>
/// is its one caller.
/// </summary>
public interface IBookletPdfRenderer
{
    BookletPdf Render(Booklet booklet);
}

/// <summary>The rendered PDF and how many pages it came to.</summary>
public sealed record BookletPdf(byte[] Bytes, int Pages);
