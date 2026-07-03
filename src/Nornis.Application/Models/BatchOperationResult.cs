namespace Nornis.Application.Models;

public record BatchOperationResult(
    IReadOnlyList<Guid> Succeeded,
    IReadOnlyList<BatchFailureDetail> Failed);

public record BatchFailureDetail(
    Guid ProposalId,
    string Code,
    string Message);
