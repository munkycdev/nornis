using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface IReviewService
{
    Task<AppResult<ReviewQueueResult>> ListReviewQueueAsync(
        ReviewQueueQuery query, CancellationToken ct);

    Task<AppResult<AcceptProposalResult>> AcceptProposalAsync(
        AcceptProposalCommand command, CancellationToken ct);

    Task<AppResult<RejectProposalResult>> RejectProposalAsync(
        RejectProposalCommand command, CancellationToken ct);

    Task<AppResult<EditProposalResult>> EditProposalAsync(
        EditProposalCommand command, CancellationToken ct);

    Task<AppResult<BatchOperationResult>> BatchAcceptAsync(
        BatchAcceptCommand command, CancellationToken ct);

    Task<AppResult<BatchOperationResult>> BatchRejectAsync(
        BatchRejectCommand command, CancellationToken ct);
}
