namespace Nornis.Domain.Enums;

/// <summary>
/// Where a library document's book lives. An <see cref="Upload"/> is a file in Nornis storage;
/// a <see cref="Bibliotaph"/> row is a book in the world creator's Bibliotaph collection, with
/// no file in Nornis (feature 31). An upload that Bibliotaph has catalogued stays an Upload and
/// carries the Bibliotaph ids as well, so it is one row.
/// </summary>
public enum LibraryDocumentOrigin
{
    Upload,
    Bibliotaph,
}
