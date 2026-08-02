using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
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
            return result.Error!.ToActionResult();
        }

        var members = result.Value!;
        var response = members.Select(ToWorldMemberResponse).ToList();

        return Ok(response);
    }

    /// <summary>The calling user's own membership in this world.</summary>
    [HttpGet("me")]
    public IActionResult GetMe()
    {
        return Ok(ToWorldMemberResponse(HttpContext.GetWorldMember()));
    }

    /// <summary>Updates the calling user's own membership — currently just the display name.</summary>
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(
        Guid worldId,
        [FromBody] UpdateMyMemberRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();

        var result = await _worldMemberService.UpdateDisplayNameAsync(worldId, user.Id, request.DisplayName, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(ToWorldMemberResponse(result.Value!));
    }

    /// <summary>
    /// Candidates for this world's add-member picker: users who are not already members, matched
    /// on username, capped.
    ///
    /// <para>This replaced <c>GET /api/users</c>, which handed every username and id in the system
    /// to any authenticated caller in one request. Only this picker ever used it.</para>
    ///
    /// <para><b>What the role check does and does not buy.</b> It stops a Player or an outsider
    /// asking about <i>your</i> world. It does not make the directory unreadable, because anyone
    /// can create a world and be its GM — so a determined caller can still ask, just about a world
    /// of their own. What actually raises the cost of reassembling the directory is the rest of
    /// this: a search term is required (there is no listing mode), results are capped, and the
    /// endpoint is rate limited per user. Enumeration goes from one request to a throttled crawl.
    /// The gate below is defence in depth over the service's own check, which is where the
    /// authorization decision is made.</para>
    /// </summary>
    [HttpGet("addable")]
    [EnableRateLimiting("user-search")]
    public async Task<IActionResult> ListAddable(
        Guid worldId,
        [FromQuery] string? q,
        CancellationToken ct)
    {
        // The one inline GM check that stays. SearchAddableUsersAsync re-checks it too, and
        // that duplication is deliberate: this is the only query in the application that reads
        // across the user table rather than within a world, so it gets two locks rather than
        // one. See IWorldMemberService for the longer note.
        if (HttpContext.GetWorldMember().Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can search for users to add."));
        }

        var result = await _worldMemberService.SearchAddableUsersAsync(
            worldId, HttpContext.GetNornisUser().Id, q, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(result.Value!.Select(u => new UserSummaryResponse(u.Id, u.Username)).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> AddMember(
        Guid worldId,
        [FromBody] AddWorldMemberRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (!EnumParsing.TryParseDefined<WorldRole>(request.Role, out var role))
        {
            return BadRequest(new ErrorResponse("invalid_role", $"'{request.Role}' is not a valid world role."));
        }

        var command = new AddMemberCommand(
            WorldId: worldId,
            TargetUserId: request.UserId,
            Role: role,
            ActingUserId: user.Id,
            ActingUserRole: member.Role);

        var result = await _worldMemberService.AddMemberAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
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

        if (!EnumParsing.TryParseDefined<WorldRole>(request.Role, out var newRole))
        {
            return BadRequest(new ErrorResponse("invalid_role", $"'{request.Role}' is not a valid world role."));
        }

        var command = new UpdateMemberRoleCommand(
            WorldId: worldId,
            TargetUserId: userId,
            NewRole: newRole,
            ActingUserId: user.Id,
            ActingUserRole: member.Role);

        var result = await _worldMemberService.UpdateRoleAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
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

        var result = await _worldMemberService.RemoveMemberAsync(worldId, userId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
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
}
