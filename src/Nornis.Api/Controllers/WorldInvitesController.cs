using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

/// <summary>
/// GM management of a world's reusable invite links. World-scoped: the
/// <see cref="WorldMemberActionFilter"/> resolves membership, and every action is GM-only.
/// Redemption of an invite lives on the separate, non-world-scoped <see cref="InvitesController"/>,
/// because an invitee is by definition not yet a member.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/invites")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class WorldInvitesController : ControllerBase
{
    private readonly IWorldInviteService _inviteService;

    public WorldInvitesController(IWorldInviteService inviteService)
    {
        _inviteService = inviteService;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, CancellationToken ct)
    {
        if (HttpContext.GetWorldMember().Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can view invites."));
        }

        var user = HttpContext.GetNornisUser();
        var result = await _inviteService.ListAsync(worldId, user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var response = result.Value!.Select(ToResponse).ToList();
        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid worldId,
        [FromBody] CreateInviteRequest request,
        CancellationToken ct)
    {
        if (HttpContext.GetWorldMember().Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can create invites."));
        }

        if (!Enum.TryParse<WorldRole>(request.Role, ignoreCase: true, out var role))
        {
            return BadRequest(new ErrorResponse("invalid_role", $"'{request.Role}' is not a valid world role."));
        }

        var user = HttpContext.GetNornisUser();
        var command = new CreateInviteCommand(worldId, user.Id, role, request.ExpiresAt, request.MaxUses);

        var result = await _inviteService.CreateAsync(command, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var response = ToResponse(result.Value!);
        return CreatedAtAction(nameof(List), new { worldId }, response);
    }

    [HttpDelete("{inviteId:guid}")]
    public async Task<IActionResult> Revoke(Guid worldId, Guid inviteId, CancellationToken ct)
    {
        if (HttpContext.GetWorldMember().Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can revoke invites."));
        }

        var user = HttpContext.GetNornisUser();
        var result = await _inviteService.RevokeAsync(worldId, inviteId, user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return NoContent();
    }

    private static WorldInviteResponse ToResponse(WorldInvite invite)
    {
        return new WorldInviteResponse(
            Id: invite.Id,
            WorldId: invite.WorldId,
            Code: invite.Code,
            Role: invite.Role.ToString(),
            Status: invite.StatusAt(DateTimeOffset.UtcNow).ToString(),
            UseCount: invite.UseCount,
            MaxUses: invite.MaxUses,
            ExpiresAt: invite.ExpiresAt,
            CreatedAt: invite.CreatedAt,
            RevokedAt: invite.RevokedAt);
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
