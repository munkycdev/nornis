using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record AcceptProposalCommand(
    Guid ProposalId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole);
