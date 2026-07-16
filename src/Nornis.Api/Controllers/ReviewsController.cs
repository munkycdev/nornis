using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;

namespace Nornis.Api.Controllers;

[ApiController]
[Route("api/worlds/{worldId:guid}/reviews")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewsController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpGet("proposals")]
    public async Task<IActionResult> ListProposals(
        Guid worldId,
        [FromQuery] Guid? batchId,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var query = new ReviewQueueQuery(
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            FilterByBatchId: batchId);

        var result = await _reviewService.ListReviewQueueAsync(query, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var queueResult = result.Value!;
        var response = new ReviewQueueResponse(
            Proposals: queueResult.Proposals
                .Select(p => ToProposalResponse(p, queueResult.Context?.GetValueOrDefault(p.Id)))
                .ToList(),
            HasMore: queueResult.HasMore);

        return Ok(response);
    }

    [HttpPost("proposals/{proposalId:guid}/accept")]
    public async Task<IActionResult> AcceptProposal(
        Guid worldId,
        Guid proposalId,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new AcceptProposalCommand(
            ProposalId: proposalId,
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role);

        var result = await _reviewService.AcceptProposalAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var acceptResult = result.Value!;
        var response = new AcceptProposalResponse(
            ProposalId: acceptResult.ProposalId,
            Status: acceptResult.Status.ToString(),
            ReviewedAt: acceptResult.ReviewedAt,
            ReviewedByUserId: acceptResult.ReviewedByUserId,
            CreatedEntityId: acceptResult.CreatedEntityId);

        return Ok(response);
    }

    [HttpPost("proposals/{proposalId:guid}/reject")]
    public async Task<IActionResult> RejectProposal(
        Guid worldId,
        Guid proposalId,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new RejectProposalCommand(
            ProposalId: proposalId,
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role);

        var result = await _reviewService.RejectProposalAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var rejectResult = result.Value!;
        var response = new RejectProposalResponse(
            ProposalId: rejectResult.ProposalId,
            Status: rejectResult.Status.ToString(),
            ReviewedAt: rejectResult.ReviewedAt,
            ReviewedByUserId: rejectResult.ReviewedByUserId);

        return Ok(response);
    }

    [HttpPost("proposals/{proposalId:guid}/edit")]
    public async Task<IActionResult> EditProposal(
        Guid worldId,
        Guid proposalId,
        [FromBody] EditProposalRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new EditProposalCommand(
            ProposalId: proposalId,
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            NewProposedValueJson: request.ProposedValueJson);

        var result = await _reviewService.EditProposalAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var editResult = result.Value!;
        var response = new EditProposalResponse(
            ProposalId: editResult.ProposalId,
            Status: editResult.Status.ToString(),
            ProposedValueJson: editResult.ProposedValueJson,
            ReviewedAt: editResult.ReviewedAt,
            ReviewedByUserId: editResult.ReviewedByUserId);

        return Ok(response);
    }

    [HttpPost("proposals/batch-accept")]
    public async Task<IActionResult> BatchAccept(
        Guid worldId,
        [FromBody] BatchAcceptRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new BatchAcceptCommand(
            ProposalIds: request.ProposalIds,
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role);

        var result = await _reviewService.BatchAcceptAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var batchResult = result.Value!;
        var response = new BatchOperationResponse(
            Succeeded: batchResult.Succeeded,
            Failed: batchResult.Failed.Select(f => new BatchFailureItem(
                ProposalId: f.ProposalId,
                Code: f.Code,
                Message: f.Message)).ToList());

        return Ok(response);
    }

    [HttpPost("proposals/batch-reject")]
    public async Task<IActionResult> BatchReject(
        Guid worldId,
        [FromBody] BatchRejectRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new BatchRejectCommand(
            ProposalIds: request.ProposalIds,
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role);

        var result = await _reviewService.BatchRejectAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var batchResult = result.Value!;
        var response = new BatchOperationResponse(
            Succeeded: batchResult.Succeeded,
            Failed: batchResult.Failed.Select(f => new BatchFailureItem(
                ProposalId: f.ProposalId,
                Code: f.Code,
                Message: f.Message)).ToList());

        return Ok(response);
    }

    private static ReviewProposalResponse ToProposalResponse(
        ReviewProposal proposal, ReviewProposalContext? context = null)
    {
        return new ReviewProposalResponse(
            Id: proposal.Id,
            ReviewBatchId: proposal.ReviewBatchId,
            ChangeType: proposal.ChangeType.ToString(),
            TargetType: proposal.TargetType.ToString(),
            TargetId: proposal.TargetId,
            ProposedValueJson: proposal.ProposedValueJson,
            Rationale: proposal.Rationale,
            Confidence: proposal.Confidence,
            Status: proposal.Status.ToString(),
            CreatedAt: proposal.CreatedAt,
            SourceId: context?.SourceId,
            SourceTitle: context?.SourceTitle,
            TargetName: context?.TargetName,
            MergeSourceName: context?.MergeSourceName,
            BatchKind: context?.BatchKind);
    }

    private IActionResult MapError(AppError error)
    {
        return error.StatusCode switch
        {
            400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
            403 => StatusCode(403, new ErrorResponse(error.Code, error.Message)),
            404 => NotFound(new ErrorResponse(error.Code, error.Message)),
            409 => Conflict(new ErrorResponse(error.Code, error.Message)),
            _ => StatusCode(error.StatusCode, new ErrorResponse(error.Code, error.Message))
        };
    }
}
