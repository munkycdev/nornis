using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// The world's timeline replay: re-extract sources in story order, one at a time, pausing
/// for review between them. At most one replay is active per world; all operations are
/// GM-gated in the Application service.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/replay")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class ExtractionReplayController : ControllerBase
{
    private readonly IExtractionReplayService _replayService;

    public ExtractionReplayController(IExtractionReplayService replayService)
    {
        _replayService = replayService;
    }

    [HttpGet]
    public async Task<IActionResult> GetActive(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _replayService.GetActiveAsync(worldId, user.Id, member.Role, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(new ExtractionReplayStateResponse(
            result.Value is null ? null : ToResponse(result.Value)));
    }

    [HttpGet("preview")]
    public async Task<IActionResult> Preview(
        Guid worldId, [FromQuery] Guid startSourceId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _replayService.CountFromAsync(worldId, startSourceId, user.Id, member.Role, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(new ExtractionReplayPreviewResponse(result.Value));
    }

    [HttpPost]
    public async Task<IActionResult> Start(
        Guid worldId, [FromBody] StartExtractionReplayRequest request, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _replayService.StartAsync(
            worldId, request.StartSourceId, user.Id, member.Role, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToResponse(result.Value!));
    }

    [HttpDelete]
    public async Task<IActionResult> Cancel(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _replayService.CancelAsync(worldId, user.Id, member.Role, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToResponse(result.Value!));
    }

    private static ExtractionReplayResponse ToResponse(ExtractionReplayInfo info) =>
        new(info.Id, info.Status, info.CurrentSourceId, info.CurrentSourceTitle,
            info.CurrentSourceProcessingStatus, info.RemainingCount, info.CreatedAt);

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
