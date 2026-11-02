namespace Nornis.Domain.Entities;

/// <summary>
/// A section's body as it was before an AI write replaced it: what Undo restores. Kept only
/// for AI writes, which are the one destructive event in Prep; the GM's own keystrokes have
/// the editor's unsaved-edit backup. Bounded per section, oldest trimmed first.
/// </summary>
public class AdventureSectionRevision
{
    public Guid Id { get; set; }

    public Guid SectionId { get; set; }

    public string BodyMarkdown { get; set; } = string.Empty;

    /// <summary>What the GM asked for. Null means a plain Draft, which carries no instruction.</summary>
    public string? Instruction { get; set; }

    /// <summary>The model that made the write this revision undoes, snapshotted at the time.</summary>
    public string Model { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public AdventureSection Section { get; set; } = null!;
}
