using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

/// <summary>
/// Storylines are artifacts with <see cref="ArtifactType.Storyline"/>. This is a dedicated
/// read view over the same artifact data, matching the Storylines navigation item.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/storylines")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class StorylinesController : ControllerBase
{
    private readonly IArtifactService _artifactService;
    private readonly IStorylineRetrospectiveService _retrospectiveService;

    public StorylinesController(
        IArtifactService artifactService,
        IStorylineRetrospectiveService retrospectiveService)
    {
        _artifactService = artifactService;
        _retrospectiveService = retrospectiveService;
    }

    /// <summary>
    /// GM-only: assess every Active storyline against the record and propose closures
    /// as review proposals.
    /// </summary>
    [HttpPost("retrospective")]
    public async Task<IActionResult> RunRetrospective(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _retrospectiveService.RunAsync(worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var value = result.Value!;
        return Ok(new RetrospectiveResponse(value.AssessedCount, value.ProposedCount, value.ReviewBatchId));
    }

    [HttpGet]
    public async Task<IActionResult> List(
        Guid worldId,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        ArtifactStatus? statusFilter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<ArtifactStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                return BadRequest(new ErrorResponse("invalid_artifact_status", $"'{status}' is not a valid artifact status."));
            }
            statusFilter = parsedStatus;
        }

        var query = new ArtifactListQuery(
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            Type: ArtifactType.Storyline,
            Status: statusFilter);

        var result = await _artifactService.ListAsync(query, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var response = result.Value!.Select(ArtifactsController.ToListItemResponse).ToList();

        return Ok(response);
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
