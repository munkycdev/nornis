namespace Nornis.Domain.Models;

/// <summary>One Adventure as the Prep index lists it, without its section bodies.</summary>
public record AdventureSummary(
    Guid Id,
    string Title,
    string? Slug,
    Guid? CampaignId,
    int SectionCount,
    int FilledSectionCount,
    Guid? LastPrintedDocumentId,
    DateTimeOffset UpdatedAt);
