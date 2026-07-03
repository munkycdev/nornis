using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record EditProposalCommand(
    Guid ProposalId,
    Guid CampaignId,
    Guid ActingUserId,
    CampaignRole ActingUserRole,
    string NewProposedValueJson);
