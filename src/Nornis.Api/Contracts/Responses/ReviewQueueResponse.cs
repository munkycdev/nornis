namespace Nornis.Api.Contracts.Responses;

public record ReviewQueueResponse(
    IReadOnlyList<ReviewProposalResponse> Proposals,
    bool HasMore);
