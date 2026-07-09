using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record EditProposalCommand(
    Guid ProposalId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string NewProposedValueJson);
