using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>
/// What the Handouts card's picker offers (feature 38): the world's maps and handouts a booklet
/// can print, this Adventure's own first, through the Library's chips and search (feature 36).
/// The API is the rule (<c>AdventureDesignService.SetHandoutsAsync</c>); this only leaves out what
/// it would refuse, so the picker doesn't offer a choice that fails.
/// </summary>
public static class HandoutChoices
{
    /// <summary>Mirrors <c>AdventureDesignService.MaxHandouts</c>; HandoutsMirrorTests pins it.</summary>
    public const int MaxHandouts = 20;

    /// <summary>The categories a handout can be, as the picker's chips offer them.</summary>
    public static readonly IReadOnlyList<string> Categories = [LibraryDocumentDto.MapCategory, LibraryDocumentDto.HandoutCategory];

    /// <summary>Mirrors <c>HandoutFiles</c>: the images a booklet draws and the PDFs it appends.</summary>
    public static readonly IReadOnlySet<string> PrintableTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp", "application/pdf" };

    /// <summary>Whether the booklet can print <paramref name="document"/> as a handout.</summary>
    public static bool CanPrint(LibraryDocumentDto document) =>
        Categories.Contains(document.Category)
        && PrintableTypes.Contains(document.ContentType)
        && document.Status != "PendingUpload";

    /// <summary>
    /// The picker's two lists, each by title: this Adventure's documents, then the rest of the
    /// Library. Documents already chosen are left out. <paramref name="category"/> null is All.
    /// </summary>
    public static (IReadOnlyList<LibraryDocumentDto> Own, IReadOnlyList<LibraryDocumentDto> Others) For(
        IEnumerable<LibraryDocumentDto> library, Guid adventureId, IEnumerable<Guid> chosen, string? category, string? search)
    {
        var taken = chosen.ToHashSet();
        var shown = LibraryFilter.Apply(
            library.Where(d => CanPrint(d) && !taken.Contains(d.Id)),
            new LibraryView(category, LibraryScope.Everything, search, LibrarySort.Title));
        return (
            shown.Where(d => LibraryFilter.BelongsTo(d, adventureId)).ToList(),
            shown.Where(d => !LibraryFilter.BelongsTo(d, adventureId)).ToList());
    }

    /// <summary>Each chip's count over everything the picker could offer, ignoring the search, as the Library's chips do.</summary>
    public static int Count(IEnumerable<LibraryDocumentDto> library, IEnumerable<Guid> chosen, string? category)
    {
        var taken = chosen.ToHashSet();
        return library.Count(d => CanPrint(d) && !taken.Contains(d.Id) && (category is null || d.Category == category));
    }

    /// <summary>The list with the handout at <paramref name="index"/> moved by <paramref name="offset"/>; unchanged at either end.</summary>
    public static IReadOnlyList<Guid> Move(IReadOnlyList<Guid> ids, int index, int offset)
    {
        var target = index + offset;
        if (index < 0 || index >= ids.Count || target < 0 || target >= ids.Count)
        {
            return ids;
        }

        var moved = ids.ToList();
        (moved[index], moved[target]) = (moved[target], moved[index]);
        return moved;
    }
}
