using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record CanonQuery(
    Guid CampaignId,
    Guid ActingUserId,
    CampaignRole ActingUserRole,
    TruthState? TruthState = null);
