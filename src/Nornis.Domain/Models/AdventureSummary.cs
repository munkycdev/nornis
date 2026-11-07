namespace Nornis.Domain.Models;

/// <summary>
/// One Adventure as the Prep index lists it, without its section bodies.
/// <see cref="IsExported"/> is true when any source exported from it still names it.
/// <see cref="CoverUpdatedAt"/> is when its cover art was set, null when it has none: the shelf
/// (feature 42) shows the art through the cover proxy with it as the cache-buster.
/// </summary>
public record AdventureSummary(
    Guid Id,
    string Title,
    string? Slug,
    Guid? CampaignId,
    int SectionCount,
    int FilledSectionCount,
    Guid? LastPrintedDocumentId,
    DateTimeOffset UpdatedAt,
    bool IsExported,
    DateTimeOffset? CoverUpdatedAt = null);
