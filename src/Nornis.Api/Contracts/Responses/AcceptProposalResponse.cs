namespace Nornis.Api.Contracts.Responses;

public record AcceptProposalResponse(
    Guid ProposalId,
    string Status,
    DateTimeOffset ReviewedAt,
    Guid ReviewedByUserId,
    Guid? CreatedEntityId);
