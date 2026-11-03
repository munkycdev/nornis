namespace Nornis.Domain.Entities;

/// <summary>
/// A Library book chosen as material for one Adventure (feature 37): drafting, the side
/// conversation and the split planner read passages from these books first. At most five per
/// Adventure, enforced by <c>AdventureDesignService</c>.
/// </summary>
public class AdventureSourceBook
{
    public Guid AdventureId { get; set; }

    public Guid LibraryDocumentId { get; set; }

    public LibraryDocument LibraryDocument { get; set; } = null!;
}
