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
[Route("api/campaigns")]
public class CampaignsController : ControllerBase
{
    private readonly ICampaignService _campaignService;

    public CampaignsController(ICampaignService campaignService)
    {
        _campaignService = campaignService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCampaignRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();

        var command = new CreateCampaignCommand(
            Name: request.Name,
            Description: request.Description,
            GameSystem: request.GameSystem,
            CreatingUserId: user.Id);

        var result = await _campaignService.CreateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var campaign = result.Value!;
        var response = ToCampaignResponse(campaign, CampaignRole.GM);

        return CreatedAtAction(nameof(GetById), new { campaignId = campaign.Id }, response);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();

        var result = await _campaignService.ListForUserAsync(user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var campaigns = result.Value!;
        var response = campaigns.Select(c => new CampaignListItemResponse(
            Id: c.Campaign.Id,
            Name: c.Campaign.Name,
            Description: c.Campaign.Description,
            GameSystem: c.Campaign.GameSystem,
            MyRole: c.Role.ToString())).ToList();

        return Ok(response);
    }

    [HttpGet("{campaignId:guid}")]
    [ServiceFilter(typeof(CampaignMemberActionFilter))]
    public async Task<IActionResult> GetById(Guid campaignId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        var result = await _campaignService.GetByIdAsync(campaignId, user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var campaign = result.Value!;
        var response = ToCampaignResponse(campaign, member.Role);

        return Ok(response);
    }

    [HttpPut("{campaignId:guid}")]
    [ServiceFilter(typeof(CampaignMemberActionFilter))]
    public async Task<IActionResult> Update(
        Guid campaignId,
        [FromBody] UpdateCampaignRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        if (member.Role != CampaignRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can update campaign settings."));
        }

        var command = new UpdateCampaignCommand(
            CampaignId: campaignId,
            Name: request.Name,
            Description: request.Description,
            GameSystem: request.GameSystem,
            ActingUserId: user.Id);

        var result = await _campaignService.UpdateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var campaign = result.Value!;
        var response = ToCampaignResponse(campaign, member.Role);

        return Ok(response);
    }

    private static CampaignResponse ToCampaignResponse(Campaign campaign, CampaignRole role)
    {
        return new CampaignResponse(
            Id: campaign.Id,
            Name: campaign.Name,
            Description: campaign.Description,
            GameSystem: campaign.GameSystem,
            CreatedByUserId: campaign.CreatedByUserId,
            CreatedAt: campaign.CreatedAt,
            UpdatedAt: campaign.UpdatedAt,
            MyRole: role.ToString());
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
