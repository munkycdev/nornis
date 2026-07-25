using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// The demo-world tutorial checklist (feature 20 phase C). Member-scoped, deliberately not
/// GM-gated: chapter one runs while the GM is viewing as player, so these endpoints must
/// work for a downgraded membership.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/tutorial")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class TutorialController : ControllerBase
{
    private readonly ITutorialService _tutorialService;

    public TutorialController(ITutorialService tutorialService)
    {
        _tutorialService = tutorialService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var result = await _tutorialService.GetChecklistAsync(worldId, user.Id, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : MapError(result.Error!);
    }

    [HttpPost("steps/{stepKey}")]
    public async Task<IActionResult> ReportStep(Guid worldId, string stepKey, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var result = await _tutorialService.ReportStepAsync(worldId, user.Id, stepKey, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : MapError(result.Error!);
    }

    [HttpGet("session-six")]
    public async Task<IActionResult> GetSessionSix(Guid worldId, CancellationToken ct)
    {
        var result = await _tutorialService.GetSessionSixAsync(ct);
        return result.IsSuccess
            ? Ok(new TutorialSessionSixResponse(result.Value!))
            : MapError(result.Error!);
    }

    private static TutorialChecklistResponse ToResponse(TutorialChecklist checklist) =>
        new(checklist.Steps
            .Select(s => new TutorialStepResponse(s.Key, s.Chapter, s.ClientReported, s.CompletedAt))
            .ToList());

    private IActionResult MapError(AppError error) => error.StatusCode switch
    {
        400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
        404 => NotFound(new ErrorResponse(error.Code, error.Message)),
        409 => Conflict(new ErrorResponse(error.Code, error.Message)),
        _ => StatusCode(error.StatusCode, new ErrorResponse(error.Code, error.Message))
    };
}
