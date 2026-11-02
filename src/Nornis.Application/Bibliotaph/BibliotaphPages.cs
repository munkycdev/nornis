namespace Nornis.Application.Bibliotaph;

/// <summary>
/// Bibliotaph counts pages from 0; Nornis, its excerpts and its citations count from 1. This is
/// the only place the two meet.
/// </summary>
public static class BibliotaphPages
{
    public static int ToPageNumber(int pageIndex) => pageIndex + 1;

    public static int ToPageIndex(int pageNumber) =>
        pageNumber >= 1
            ? pageNumber - 1
            : throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Page numbers start at 1.");
}
