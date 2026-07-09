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
[Route("api/worlds/{worldId:guid}/campaigns")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class CampaignsController : ControllerBase
{
    private readonly ICampaignService _campaignService;

    public CampaignsController(ICampaignService campaignService)
    {
        _campaignService = campaignService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid worldId,
        [FromBody] CreateCampaignRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var status = CampaignStatus.Active;
        if (request.Status is not null && !Enum.TryParse(request.Status, ignoreCase: true, out status))
        {
            return BadRequest(new ErrorResponse("invalid_status", $"'{request.Status}' is not a valid campaign status."));
        }

        var command = new CreateCampaignCommand(
            WorldId: worldId,
            Name: request.Name,
            CreatingUserId: user.Id,
            CreatingUserRole: member.Role,
            Description: request.Description,
            Status: status,
            StartedAt: request.StartedAt,
            EndedAt: request.EndedAt);

        var result = await _campaignService.CreateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var campaign = result.Value!;
        return CreatedAtAction(nameof(GetById), new { worldId, campaignId = campaign.Id }, ToCampaignResponse(campaign));
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, CancellationToken ct)
    {
        var result = await _campaignService.ListByWorldAsync(worldId, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(result.Value!.Select(ToCampaignResponse).ToList());
    }

    [HttpGet("{campaignId:guid}")]
    public async Task<IActionResult> GetById(Guid worldId, Guid campaignId, CancellationToken ct)
    {
        var result = await _campaignService.GetByIdAsync(campaignId, worldId, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToCampaignResponse(result.Value!));
    }

    [HttpPut("{campaignId:guid}")]
    public async Task<IActionResult> Update(
        Guid worldId,
        Guid campaignId,
        [FromBody] UpdateCampaignRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        CampaignStatus? status = null;
        if (request.Status is not null)
        {
            if (!Enum.TryParse<CampaignStatus>(request.Status, ignoreCase: true, out var parsedStatus))
            {
                return BadRequest(new ErrorResponse("invalid_status", $"'{request.Status}' is not a valid campaign status."));
            }
            status = parsedStatus;
        }

        var command = new UpdateCampaignCommand(
            CampaignId: campaignId,
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            Name: request.Name,
            Description: request.Description,
            Status: status,
            StartedAt: request.StartedAt,
            EndedAt: request.EndedAt);

        var result = await _campaignService.UpdateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToCampaignResponse(result.Value!));
    }

    [HttpDelete("{campaignId:guid}")]
    public async Task<IActionResult> Delete(Guid worldId, Guid campaignId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _campaignService.DeleteAsync(campaignId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return NoContent();
    }

    [HttpGet("{campaignId:guid}/characters")]
    public async Task<IActionResult> ListCharacters(Guid worldId, Guid campaignId, [FromServices] ICharacterService characterService, CancellationToken ct)
    {
        // Confirm the campaign belongs to this world before listing.
        var campaignResult = await _campaignService.GetByIdAsync(campaignId, worldId, ct);
        if (!campaignResult.IsSuccess)
        {
            return MapError(campaignResult.Error!);
        }

        var result = await characterService.ListByWorldAsync(worldId, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var assigned = result.Value!
            .Where(c => c.CampaignCharacters.Any(cc => cc.CampaignId == campaignId))
            .Select(CharactersController.ToCharacterResponse)
            .ToList();

        return Ok(assigned);
    }

    [HttpPut("{campaignId:guid}/characters")]
    public async Task<IActionResult> AssignCharacters(
        Guid worldId,
        Guid campaignId,
        [FromBody] AssignCampaignCharactersRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new AssignCampaignCharactersCommand(
            CampaignId: campaignId,
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            CharacterIds: request.CharacterIds);

        var result = await _campaignService.AssignCharactersAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(result.Value!.Select(CharactersController.ToCharacterResponse).ToList());
    }

    private static CampaignResponse ToCampaignResponse(Campaign campaign)
    {
        return new CampaignResponse(
            Id: campaign.Id,
            WorldId: campaign.WorldId,
            Name: campaign.Name,
            Description: campaign.Description,
            Status: campaign.Status.ToString(),
            StartedAt: campaign.StartedAt,
            EndedAt: campaign.EndedAt,
            CreatedAt: campaign.CreatedAt,
            UpdatedAt: campaign.UpdatedAt,
            CreatedByUserId: campaign.CreatedByUserId);
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
