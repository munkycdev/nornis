namespace Nornis.Api.Contracts.Responses;

public record EditProposalResponse(
    Guid ProposalId,
    string Status,
    string ProposedValueJson,
    DateTimeOffset ReviewedAt,
    Guid ReviewedByUserId);
