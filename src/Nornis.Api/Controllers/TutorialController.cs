using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
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
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    [HttpPost("steps/{stepKey}")]
    public async Task<IActionResult> ReportStep(Guid worldId, string stepKey, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var result = await _tutorialService.ReportStepAsync(worldId, user.Id, stepKey, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    [HttpGet("session-six")]
    public async Task<IActionResult> GetSessionSix(Guid worldId, CancellationToken ct)
    {
        var result = await _tutorialService.GetSessionSixAsync(ct);
        return result.IsSuccess
            ? Ok(new TutorialSessionSixResponse(result.Value!))
            : result.Error!.ToActionResult();
    }

    private static TutorialChecklistResponse ToResponse(TutorialChecklist checklist) =>
        new(checklist.Steps
            .Select(s => new TutorialStepResponse(s.Key, s.Chapter, s.ClientReported, s.CompletedAt))
            .ToList());
}
