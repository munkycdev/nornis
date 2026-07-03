using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record AcceptProposalCommand(
    Guid ProposalId,
    Guid CampaignId,
    Guid ActingUserId,
    CampaignRole ActingUserRole);
