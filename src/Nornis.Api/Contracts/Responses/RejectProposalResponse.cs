namespace Nornis.Api.Contracts.Responses;

public record RejectProposalResponse(
    Guid ProposalId,
    string Status,
    DateTimeOffset ReviewedAt,
    Guid ReviewedByUserId);
