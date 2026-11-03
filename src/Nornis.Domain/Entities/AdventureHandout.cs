namespace Nornis.Domain.Entities;

/// <summary>
/// A Library map or handout chosen to print at the back of one Adventure's booklet (feature 38),
/// in <see cref="SortOrder"/>. At most <c>AdventureDesignService.MaxHandouts</c>. Choosing one
/// never changes the document's visibility.
/// </summary>
public class AdventureHandout
{
    public Guid AdventureId { get; set; }

    public Guid LibraryDocumentId { get; set; }

    public int SortOrder { get; set; }

    public LibraryDocument LibraryDocument { get; set; } = null!;
}
