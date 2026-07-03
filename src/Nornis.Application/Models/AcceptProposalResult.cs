using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record AcceptProposalResult(
    Guid ProposalId,
    ReviewProposalStatus Status,
    DateTimeOffset ReviewedAt,
    Guid ReviewedByUserId,
    Guid? CreatedEntityId);
