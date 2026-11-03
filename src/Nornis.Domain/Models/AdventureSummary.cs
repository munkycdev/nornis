namespace Nornis.Domain.Models;

/// <summary>
/// One Adventure as the Prep index lists it, without its section bodies.
/// <see cref="IsExported"/> is true when any source exported from it still names it.
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
    bool IsExported);
