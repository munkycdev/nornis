using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// Files sources that have no campaign under this one. The ids are the GM's confirmed
/// selection from what the campaign page offered; the service checks each is still an
/// unfiled source of this world rather than trusting the offer it made a moment ago.
/// </summary>
public record FileCampaignSourcesCommand(
    Guid CampaignId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    IReadOnlyList<Guid> SourceIds);
