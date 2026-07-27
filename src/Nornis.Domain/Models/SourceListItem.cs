using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// A source as a list shows it: everything the list DTOs read, and nothing else.
///
/// Deliberately omits <c>Body</c> and <c>DerivedText</c>. Those hold the full session transcript
/// and any machine-derived text, both unbounded — in production one world carries ~1.5 MB across
/// them — and no list view reads either. The sources page polls its list every four seconds while
/// anything is processing, so loading whole rows there meant streaming the world's entire note
/// corpus out of the database repeatedly to render titles and status chips.
/// </summary>
public record SourceListItem(
    Guid Id,
    Guid WorldId,
    SourceType Type,
    string Title,
    DateTimeOffset? OccurredAt,
    DateTimeOffset CreatedAt,
    Guid CreatedByUserId,
    VisibilityScope Visibility,
    SourceProcessingStatus ProcessingStatus,
    Guid? CampaignId,
    string? CampaignName);
