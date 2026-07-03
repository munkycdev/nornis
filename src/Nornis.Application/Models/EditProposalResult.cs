using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record EditProposalResult(
    Guid ProposalId,
    ReviewProposalStatus Status,
    string ProposedValueJson,
    DateTimeOffset ReviewedAt,
    Guid ReviewedByUserId);
