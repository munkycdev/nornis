namespace Nornis.Domain.Enums;

/// <summary>
/// The selectable slices of a world export. Each category becomes one JSON file in the
/// exported zip; <see cref="Attachments"/> and <see cref="Library"/> also carry the
/// original blob-backed files.
/// </summary>
public enum WorldExportCategory
{
    Members,
    Campaigns,
    Characters,
    Sources,
    Attachments,
    Codex,
    MapPins,
    Library,
    Reviews,
    Health,
    AiUsage,
}
