using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record RejectProposalResult(
    Guid ProposalId,
    ReviewProposalStatus Status,
    DateTimeOffset ReviewedAt,
    Guid ReviewedByUserId);
