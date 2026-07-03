using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record BatchRejectCommand(
    IReadOnlyList<Guid> ProposalIds,
    Guid CampaignId,
    Guid ActingUserId,
    CampaignRole ActingUserRole);
