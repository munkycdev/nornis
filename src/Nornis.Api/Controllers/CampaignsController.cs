using Microsoft.AspNetCore.Mvc;
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
        if (request.Status is not null && !EnumParsing.TryParseDefined(request.Status, out status))
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
            return result.Error!.ToActionResult();
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
            return result.Error!.ToActionResult();
        }

        return Ok(result.Value!.Select(ToCampaignResponse).ToList());
    }

    [HttpGet("{campaignId:guid}")]
    public async Task<IActionResult> GetById(Guid worldId, Guid campaignId, CancellationToken ct)
    {
        var result = await _campaignService.GetByIdAsync(campaignId, worldId, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(ToCampaignResponse(result.Value!));
    }

    /// <summary>
    /// The campaign page's read model. Any member may read it; the service assembles it at
    /// their visibility, so a player's copy simply holds less.
    /// </summary>
    [HttpGet("{campaignId:guid}/detail")]
    public async Task<IActionResult> GetDetail(
        Guid worldId,
        Guid campaignId,
        [FromServices] ICharacterService characterService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _campaignService.GetDetailAsync(campaignId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var detail = result.Value!;
        var characters = await characterService.ProjectForReaderAsync(detail.Characters, worldId, user.Id, member.Role, ct);

        return Ok(new CampaignDetailResponse(
            Campaign: ToCampaignResponse(detail.Campaign),
            Characters: characters.Select(CharactersController.ToCharacterResponse).ToList(),
            Artifacts: detail.Rollup.Artifacts
                .Select(a => new CampaignArtifactResponse(
                    a.ArtifactId, a.Name, a.Type.ToString(), a.Summary, a.Status.ToString(), a.SourceCount))
                .ToList(),
            ArtifactTotalCount: detail.Rollup.TotalCount,
            RecentSessions: detail.RecentSessions.Select(SourcesController.ToSourceListItemResponse).ToList(),
            SessionCount: detail.SessionCount,
            FirstSessionAt: detail.FirstSessionAt,
            LastSessionAt: detail.LastSessionAt,
            Recap: ToRecapResponse(detail.Recap)));
    }

    /// <summary>GM-only. Rewrites the world's campaign display order.</summary>
    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder(
        Guid worldId,
        [FromBody] ReorderCampaignsRequest request,
        CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();

        var result = await _campaignService.ReorderAsync(worldId, request.CampaignIds, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(result.Value!.Select(ToCampaignResponse).ToList());
    }

    /// <summary>
    /// GM-only. Makes this the campaign the world is playing now, which is what new captures
    /// default to. Refused for a campaign that is not Active.
    /// </summary>
    [HttpPut("{campaignId:guid}/current")]
    public async Task<IActionResult> SetCurrent(Guid worldId, Guid campaignId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();

        var result = await _campaignService.SetCurrentAsync(campaignId, worldId, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(ToCampaignResponse(result.Value!));
    }

    /// <summary>GM-only. Leaves the world with no current campaign.</summary>
    [HttpDelete("current")]
    public async Task<IActionResult> ClearCurrent(Guid worldId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();

        var result = await _campaignService.ClearCurrentAsync(worldId, member.Role, ct);

        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
    }

    /// <summary>GM-only. Regenerates both renderings of the campaign's "story so far".</summary>
    [HttpPost("{campaignId:guid}/recap")]
    public async Task<IActionResult> GenerateRecap(
        Guid worldId,
        Guid campaignId,
        [FromServices] ICampaignRecapService recapService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await recapService.GenerateAsync(campaignId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(ToRecapResponse(result.Value!));
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
            if (!EnumParsing.TryParseDefined<CampaignStatus>(request.Status, out var parsedStatus))
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
            return result.Error!.ToActionResult();
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
            return result.Error!.ToActionResult();
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
            return campaignResult.Error!.ToActionResult();
        }

        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await characterService.ListByWorldAsync(worldId, user.Id, member.Role, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var assigned = result.Value!
            .Where(c => c.CampaignIds.Contains(campaignId))
            .Select(CharactersController.ToCharacterResponse)
            .ToList();

        return Ok(assigned);
    }

    [HttpPut("{campaignId:guid}/characters")]
    public async Task<IActionResult> AssignCharacters(
        Guid worldId,
        Guid campaignId,
        [FromBody] AssignCampaignCharactersRequest request,
        [FromServices] ICharacterService characterService,
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
            return result.Error!.ToActionResult();
        }

        var assigned = await characterService.ProjectForReaderAsync(result.Value!, worldId, user.Id, member.Role, ct);
        return Ok(assigned.Select(CharactersController.ToCharacterResponse).ToList());
    }

    private static CampaignRecapResponse ToRecapResponse(CampaignRecapView view) =>
        new(view.HasData, view.GeneratedAt, view.Content, view.PartyPreview);

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
}
