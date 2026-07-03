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
[Route("api/campaigns/{campaignId:guid}/members")]
[ServiceFilter(typeof(CampaignMemberActionFilter))]
public class CampaignMembersController : ControllerBase
{
    private readonly ICampaignMemberService _campaignMemberService;

    public CampaignMembersController(ICampaignMemberService campaignMemberService)
    {
        _campaignMemberService = campaignMemberService;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid campaignId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();

        var result = await _campaignMemberService.ListMembersAsync(campaignId, user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var members = result.Value!;
        var response = members.Select(ToCampaignMemberResponse).ToList();

        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> AddMember(
        Guid campaignId,
        [FromBody] AddCampaignMemberRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        if (member.Role != CampaignRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can add members."));
        }

        if (!Enum.TryParse<CampaignRole>(request.Role, ignoreCase: true, out var role))
        {
            return BadRequest(new ErrorResponse("invalid_role", $"'{request.Role}' is not a valid campaign role."));
        }

        var command = new AddMemberCommand(
            CampaignId: campaignId,
            TargetUserId: request.UserId,
            Role: role,
            ActingUserId: user.Id);

        var result = await _campaignMemberService.AddMemberAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var addedMember = result.Value!;
        var response = ToCampaignMemberResponse(addedMember);

        return CreatedAtAction(nameof(List), new { campaignId }, response);
    }

    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> UpdateRole(
        Guid campaignId,
        Guid userId,
        [FromBody] UpdateCampaignMemberRoleRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        if (member.Role != CampaignRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can update member roles."));
        }

        if (!Enum.TryParse<CampaignRole>(request.Role, ignoreCase: true, out var newRole))
        {
            return BadRequest(new ErrorResponse("invalid_role", $"'{request.Role}' is not a valid campaign role."));
        }

        var command = new UpdateMemberRoleCommand(
            CampaignId: campaignId,
            TargetUserId: userId,
            NewRole: newRole,
            ActingUserId: user.Id);

        var result = await _campaignMemberService.UpdateRoleAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var updatedMember = result.Value!;
        var response = ToCampaignMemberResponse(updatedMember);

        return Ok(response);
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid campaignId,
        Guid userId,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        if (member.Role != CampaignRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can remove members."));
        }

        var result = await _campaignMemberService.RemoveMemberAsync(campaignId, userId, user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return NoContent();
    }

    private static CampaignMemberResponse ToCampaignMemberResponse(CampaignMember campaignMember)
    {
        return new CampaignMemberResponse(
            Id: campaignMember.Id,
            CampaignId: campaignMember.CampaignId,
            UserId: campaignMember.UserId,
            Role: campaignMember.Role.ToString(),
            DisplayName: campaignMember.DisplayName,
            CharacterName: campaignMember.CharacterName,
            JoinedAt: campaignMember.JoinedAt);
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
