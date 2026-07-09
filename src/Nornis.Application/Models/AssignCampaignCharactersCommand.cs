using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// Replaces the full set of characters assigned to a campaign.
/// </summary>
public record AssignCampaignCharactersCommand(
    Guid CampaignId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    IReadOnlyCollection<Guid> CharacterIds);
