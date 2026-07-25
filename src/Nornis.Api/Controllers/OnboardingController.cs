using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// Per-user onboarding flags (feature 20 phase C). Both flags are one-way: they are set
/// once and never reset — the first-login prompt never reappears, and a dismissed
/// tutorial stays dismissed on every world and every login.
/// </summary>
[ApiController]
[Route("api/onboarding")]
public class OnboardingController : ControllerBase
{
    private readonly ITutorialService _tutorialService;

    public OnboardingController(ITutorialService tutorialService)
    {
        _tutorialService = tutorialService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var state = await _tutorialService.GetOnboardingAsync(user.Id, ct);
        return Ok(new OnboardingStateResponse(state.PromptSeen, state.TutorialDismissed));
    }

    [HttpPost("prompt-seen")]
    public async Task<IActionResult> MarkPromptSeen(CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        await _tutorialService.MarkPromptSeenAsync(user.Id, ct);
        return NoContent();
    }

    [HttpPost("dismiss-tutorial")]
    public async Task<IActionResult> DismissTutorial(CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        await _tutorialService.DismissTutorialAsync(user.Id, ct);
        return NoContent();
    }
}
