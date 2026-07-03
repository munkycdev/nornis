using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record RejectProposalCommand(
    Guid ProposalId,
    Guid CampaignId,
    Guid ActingUserId,
    CampaignRole ActingUserRole);
