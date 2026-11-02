namespace Nornis.Domain.Entities;

/// <summary>
/// Someone opened a Bibliotaph book from Nornis (feature 31), so the GM can see who read what.
/// A log, not a relationship: the reader is kept as a name taken at the time, with the user or
/// player id beside it, and neither id is a foreign key, so the record outlives a player's claim
/// or removal. Kept 90 days.
/// </summary>
public class LibraryReadingRecord
{
    public const int MaxViewerNameLength = 100;

    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public Guid LibraryDocumentId { get; set; }

    /// <summary>The member who opened it. Null when it was opened from a shelf link.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The shelf-link player who opened it. Null when a member opened it.</summary>
    public Guid? PlayerId { get; set; }

    public string ViewerName { get; set; } = string.Empty;

    public DateTimeOffset OpenedAt { get; set; }
}
