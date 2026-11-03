using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// How the Library organises a document (feature 36): its category, and whether it sits on the
/// Shelf, the default view. The API sends both, so the Library page, a document's page and an
/// Adventure's page can't decide them differently.
/// </summary>
public static class LibraryCategories
{
    /// <summary>
    /// A Prep print. <see cref="LibraryDocument.AdventureId"/> is let go of when its Adventure
    /// is deleted, so a print filed with details is still known by its snapshot.
    /// </summary>
    public static bool IsPrint(LibraryDocument document) =>
        document.AdventureId is not null || document.PrintDetails is not null;

    public static LibraryDocumentCategory Of(LibraryDocument document)
    {
        if (IsPrint(document))
        {
            return LibraryDocumentCategory.Print;
        }

        if (document.Origin == LibraryDocumentOrigin.Bibliotaph)
        {
            return LibraryDocumentCategory.Book;
        }

        return document.Kind switch
        {
            LibraryDocumentKind.Sourcebook => LibraryDocumentCategory.Book,
            LibraryDocumentKind.Map => LibraryDocumentCategory.Map,
            LibraryDocumentKind.Handout => LibraryDocumentCategory.Handout,
            _ => LibraryDocumentCategory.Other,
        };
    }

    /// <summary>
    /// Whether the document shows on the Shelf. An upload that belongs to an Adventure shows
    /// under that Adventure instead; prints are the world's publications and stay.
    /// </summary>
    public static bool IsOnShelf(LibraryDocument document) =>
        document.LinkedAdventureId is null || IsPrint(document);

    /// <summary>Why the GM may not organise this document, or null when they may: only uploads are theirs to file.</summary>
    internal static AppError? WhyNotOrganisable(LibraryDocument document) =>
        IsPrint(document)
            ? new AppError(400, "prints_belong_to_their_adventure", "A print belongs to the adventure it was printed from.")
            : document.Origin == LibraryDocumentOrigin.Bibliotaph
                ? new AppError(400, "books_stay_on_the_shelf", "A book from Bibliotaph is organised in Bibliotaph.")
                : null;
}
