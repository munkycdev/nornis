using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// The design draft as it was before a change replaced it (feature 37): what Undo restores.
/// Kept for every change but the GM's own typing, which has the editor's unsaved-edit backup.
/// Bounded per Adventure like section revisions, oldest trimmed first.
/// </summary>
public class AdventureDraftRevision
{
    public Guid Id { get; set; }

    public Guid AdventureId { get; set; }

    public string BodyMarkdown { get; set; } = string.Empty;

    public AdventureDraftChange Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
