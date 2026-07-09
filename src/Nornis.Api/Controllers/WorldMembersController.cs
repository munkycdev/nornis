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

[ApiController]
[Route("api/worlds/{worldId:guid}/members")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class WorldMembersController : ControllerBase
{
    private readonly IWorldMemberService _worldMemberService;

    public WorldMembersController(IWorldMemberService worldMemberService)
    {
        _worldMemberService = worldMemberService;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();

        var result = await _worldMemberService.ListMembersAsync(worldId, user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var members = result.Value!;
        var response = members.Select(ToWorldMemberResponse).ToList();

        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> AddMember(
        Guid worldId,
        [FromBody] AddWorldMemberRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (member.Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can add members."));
        }

        if (!Enum.TryParse<WorldRole>(request.Role, ignoreCase: true, out var role))
        {
            return BadRequest(new ErrorResponse("invalid_role", $"'{request.Role}' is not a valid world role."));
        }

        var command = new AddMemberCommand(
            WorldId: worldId,
            TargetUserId: request.UserId,
            Role: role,
            ActingUserId: user.Id);

        var result = await _worldMemberService.AddMemberAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var addedMember = result.Value!;
        var response = ToWorldMemberResponse(addedMember);

        return CreatedAtAction(nameof(List), new { worldId }, response);
    }

    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> UpdateRole(
        Guid worldId,
        Guid userId,
        [FromBody] UpdateWorldMemberRoleRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (member.Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can update member roles."));
        }

        if (!Enum.TryParse<WorldRole>(request.Role, ignoreCase: true, out var newRole))
        {
            return BadRequest(new ErrorResponse("invalid_role", $"'{request.Role}' is not a valid world role."));
        }

        var command = new UpdateMemberRoleCommand(
            WorldId: worldId,
            TargetUserId: userId,
            NewRole: newRole,
            ActingUserId: user.Id);

        var result = await _worldMemberService.UpdateRoleAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var updatedMember = result.Value!;
        var response = ToWorldMemberResponse(updatedMember);

        return Ok(response);
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid worldId,
        Guid userId,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (member.Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can remove members."));
        }

        var result = await _worldMemberService.RemoveMemberAsync(worldId, userId, user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return NoContent();
    }

    private static WorldMemberResponse ToWorldMemberResponse(WorldMember worldMember)
    {
        return new WorldMemberResponse(
            Id: worldMember.Id,
            WorldId: worldMember.WorldId,
            UserId: worldMember.UserId,
            Role: worldMember.Role.ToString(),
            DisplayName: worldMember.DisplayName,
            JoinedAt: worldMember.JoinedAt);
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
