using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>How the Library list is sorted (feature 36).</summary>
public enum LibrarySort
{
    Newest,
    Title,
}

/// <summary>
/// What the Library page shows (feature 36): a category chip (null for All), where to look (the
/// Shelf, everything, or one Adventure's documents), a title search and a sort. The category
/// and the shelf rule are decided by the API (<see cref="LibraryDocumentDto.Category"/>,
/// <see cref="LibraryDocumentDto.OnShelf"/>); this only combines them, in the browser, over the
/// list the page already loads.
/// </summary>
public sealed record LibraryView(
    string? Category = null,
    LibraryScope Scope = default,
    string? Search = null,
    LibrarySort Sort = LibrarySort.Newest);

/// <summary>
/// Where the Library looks. The default is the Shelf. <see cref="Adventure"/> is one Adventure's
/// documents: uploads linked to it and its prints.
/// </summary>
public readonly record struct LibraryScope(LibraryScopeKind Kind, Guid? AdventureId = null)
{
    public static LibraryScope Shelf => new(LibraryScopeKind.Shelf);
    public static LibraryScope Everything => new(LibraryScopeKind.Everything);
    public static LibraryScope ForAdventure(Guid adventureId) => new(LibraryScopeKind.Adventure, adventureId);
}

public enum LibraryScopeKind
{
    Shelf,
    Everything,
    Adventure,
}

public static class LibraryFilter
{
    /// <summary>The documents the view shows, in its order.</summary>
    public static IReadOnlyList<LibraryDocumentDto> Apply(IEnumerable<LibraryDocumentDto> documents, LibraryView view)
    {
        var shown = documents
            .Where(d => InScope(d, view.Scope))
            .Where(d => view.Category is null || d.Category == view.Category)
            .Where(d => MatchesSearch(d, view.Search));

        return (view.Sort == LibrarySort.Title
            ? shown.OrderBy(d => d.Title, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(d => d.CreatedAt)
            : shown.OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Title, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Each category's count in the scope, ignoring the search so chips don't come and go while
    /// typing. Categories with none are left out; the page always shows All.
    /// </summary>
    public static IReadOnlyList<(string Category, int Count)> Counts(IEnumerable<LibraryDocumentDto> documents, LibraryScope scope)
    {
        var inScope = documents.Where(d => InScope(d, scope)).ToList();
        return LibraryDocumentDto.Categories
            .Select(c => (Category: c, Count: inScope.Count(d => d.Category == c)))
            .Where(c => c.Count > 0)
            .ToList();
    }

    /// <summary>How many documents the scope holds, for the All chip.</summary>
    public static int Total(IEnumerable<LibraryDocumentDto> documents, LibraryScope scope) =>
        documents.Count(d => InScope(d, scope));

    /// <summary>
    /// The Adventures that have documents, in the menu's order (by title). An Adventure with
    /// nothing linked and nothing printed isn't offered: choosing it would show an empty list.
    /// </summary>
    public static IReadOnlyList<AdventureSummaryDto> AdventuresWithDocuments(
        IEnumerable<LibraryDocumentDto> documents, IEnumerable<AdventureSummaryDto> adventures)
    {
        var owners = documents
            .SelectMany(d => new[] { d.LinkedAdventureId, d.AdventureId })
            .OfType<Guid>()
            .ToHashSet();
        return adventures
            .Where(a => owners.Contains(a.Id))
            .OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>One Adventure's documents: uploads that belong to it, then its prints.</summary>
    public static bool BelongsTo(LibraryDocumentDto document, Guid adventureId) =>
        document.LinkedAdventureId == adventureId || document.AdventureId == adventureId;

    private static bool InScope(LibraryDocumentDto document, LibraryScope scope) => scope.Kind switch
    {
        LibraryScopeKind.Everything => true,
        LibraryScopeKind.Adventure => scope.AdventureId is { } id && BelongsTo(document, id),
        _ => document.OnShelf,
    };

    private static bool MatchesSearch(LibraryDocumentDto document, string? search) =>
        string.IsNullOrWhiteSpace(search)
        || document.Title.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
