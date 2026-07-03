namespace Nornis.Api.Contracts.Responses;

public record BatchOperationResponse(
    IReadOnlyList<Guid> Succeeded,
    IReadOnlyList<BatchFailureItem> Failed);

public record BatchFailureItem(
    Guid ProposalId,
    string Code,
    string Message);
