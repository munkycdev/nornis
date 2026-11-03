namespace Nornis.Domain.Enums;

/// <summary>
/// What a Library document is, as the Library's filter chips show it (feature 36). Decided by
/// <c>LibraryCategories.Of</c>, never stored: Print comes from a print's own fields, and Book
/// covers Bibliotaph rows as well as uploaded sourcebooks.
/// </summary>
public enum LibraryDocumentCategory
{
    Book,
    Map,
    Handout,
    Print,
    Other,
}
