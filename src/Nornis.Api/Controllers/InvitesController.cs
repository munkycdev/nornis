using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Application.Errors;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// Invite redemption for the invitee. Deliberately NOT world-scoped and NOT behind the
/// <see cref="Filters.WorldMemberActionFilter"/> — the caller is a prospective member, so a
/// membership check would 403 every legitimate redemption. Still authenticated-by-default via
/// the global fallback policy, so <see cref="Middleware.UserProvisioningMiddleware"/> has
/// resolved (and, for brand-new users, created) the Nornis user before these actions run.
/// </summary>
[ApiController]
[Route("api/invites")]
public class InvitesController : ControllerBase
{
    private readonly IWorldInviteService _inviteService;

    public InvitesController(IWorldInviteService inviteService)
    {
        _inviteService = inviteService;
    }

    /// <summary>Describes an invite so the landing page can greet the invitee.</summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> Preview(string code, CancellationToken ct)
    {
        var result = await _inviteService.PreviewAsync(code, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var preview = result.Value!;
        return Ok(new InvitePreviewResponse(
            preview.WorldId,
            preview.WorldName,
            preview.Role.ToString(),
            preview.Status.ToString()));
    }

    /// <summary>Redeems the invite for the calling user, joining them to the world.</summary>
    [HttpPost("{code}/accept")]
    public async Task<IActionResult> Accept(string code, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var result = await _inviteService.RedeemAsync(code, user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var redemption = result.Value!;
        return Ok(new AcceptInviteResponse(
            redemption.WorldId,
            redemption.WorldName,
            redemption.AlreadyMember));
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
