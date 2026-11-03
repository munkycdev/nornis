namespace Nornis.Application.Prep;

/// <summary>Which Library files a booklet can print as handouts (feature 38): images it draws, and PDFs it appends.</summary>
public static class HandoutFiles
{
    public const string Pdf = "application/pdf";

    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp" };

    public static bool IsImage(string contentType) => Images.Contains(contentType);

    public static bool IsPdf(string contentType) => string.Equals(contentType, Pdf, StringComparison.OrdinalIgnoreCase);

    public static bool IsPrintable(string contentType) => IsImage(contentType) || IsPdf(contentType);
}
