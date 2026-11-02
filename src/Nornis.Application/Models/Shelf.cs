using Nornis.Domain.Entities;

namespace Nornis.Application.Models;

/// <summary>One document a shelf link may open, and who holds the link.</summary>
public record ShelfDocument(Guid WorldId, Guid PlayerId, string PlayerName, LibraryDocument Document);

/// <summary>
/// What a shelf link opens: the world and player it was minted for, and the party shelf as a
/// member with the Player role would see it on the Library page.
/// </summary>
public record Shelf(
    Guid WorldId,
    string WorldName,
    string PlayerName,
    IReadOnlyList<LibraryDocument> Documents);
