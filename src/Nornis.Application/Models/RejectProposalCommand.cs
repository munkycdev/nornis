using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record RejectProposalCommand(
    Guid ProposalId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole);
