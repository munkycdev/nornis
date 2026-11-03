using MudBlazor;
using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>
/// Presentation for library document status. The list and the detail page must say the
/// same thing — the detail page used to render a raw "IndexFailed" where the list said
/// "Index failed".
/// </summary>
public static class LibraryDisplay
{
    public static string StatusLabel(string status, int chunkCount) => status switch
    {
        "Indexed" => $"Indexed · {chunkCount} passages",
        "Indexing" => "Indexing…",
        "IndexFailed" => "Index failed",
        "Stored" => "Stored",
        _ => status,
    };

    public static Color StatusColor(string status) => status switch
    {
        "Indexed" => Color.Success,
        "Indexing" => Color.Info,
        "IndexFailed" => Color.Error,
        _ => Color.Default,
    };

    /// <summary>The icon for a document's kind, wherever documents are listed — the Library and the shelf.</summary>
    public static string KindIcon(string kind) => kind switch
    {
        "Sourcebook" => Icons.Material.Outlined.MenuBook,
        "Map" => Icons.Material.Outlined.Map,
        "Handout" => Icons.Material.Outlined.Description,
        _ => Icons.Material.Outlined.InsertDriveFile,
    };

    /// <summary>A category chip's label: "Books", "Maps", "Handouts", "Prints", "Other".</summary>
    public static string ChipLabel(string category) => category switch
    {
        LibraryDocumentDto.OtherCategory => "Other",
        _ => category + "s",
    };

    /// <summary>
    /// The categories a GM can give an upload, with the kind each one stores. Book is stored as
    /// the kind Sourcebook; Print is never chosen, because only filing a print makes one.
    /// </summary>
    public static readonly IReadOnlyList<(string Category, string Kind)> ChoosableCategories =
    [
        (LibraryDocumentDto.BookCategory, "Sourcebook"),
        (LibraryDocumentDto.MapCategory, "Map"),
        (LibraryDocumentDto.HandoutCategory, "Handout"),
        (LibraryDocumentDto.OtherCategory, "Other"),
    ];

    /// <summary>The kind an upload of <paramref name="category"/> is stored as.</summary>
    public static string KindFor(string category) =>
        ChoosableCategories.FirstOrDefault(c => c.Category == category).Kind ?? "Other";

    /// <summary>What the list says when the chips, the Adventure menu and the search leave nothing.</summary>
    public static string EmptyFilterText(string? category, bool searching) =>
        searching ? "No documents here match that search."
        : category is null ? "Nothing here yet."
        : $"No {ChipLabel(category).ToLowerInvariant()} here.";
}
